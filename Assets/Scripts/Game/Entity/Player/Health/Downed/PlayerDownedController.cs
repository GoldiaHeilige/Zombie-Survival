using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

#if FUSION_WEAVER
using Fusion;
#endif

[DisallowMultipleComponent]
public class PlayerDownedController : MonoBehaviour
{
    [Header("Refs")]
    public PlayerLifeController life;
    public PlayerMovementController move;
    public PlayerInput playerInput;
    public PlayerRefs playerRefs;                 // dùng để auto lấy weaponCam
    public UpperBodyAimRigDriver upperBodyAim;
    public PlayerAppearance appearance;

    [Header("Revive stand-up")]
    [Tooltip("Thời gian khoá input + xoay model sau khi được revive, trong lúc play anim đứng dậy.")]
    public float reviveStandupDuration = 1.0f;

    bool _reviveStandupActive;
    float _reviveStandupTimer;

    [Header("Weapon Camera (auto bind)")]
    [Tooltip("Để trống cũng được: sẽ tự lấy từ PlayerRefs khi CameraBinder báo sẵn sàng.")]
    public Camera weaponCamera;                   // chỉ cần tắt/bật cam là ẩn được súng
    public bool autoGrabWeaponCam = true;         // bật để tự nhận camera từ PlayerRefs

    [Header("Disable while DOWNED (Behaviours)")]
    public List<UnityEngine.Behaviour> disableWhileDowned = new(); // ví dụ: WeaponController, Interactor, Inventory...

    [Header("Input blocking")]
    [Tooltip("Tắt toàn bộ PlayerInput (thường KHÔNG cần).")]
    public bool disableAllInputMaps = false;
    [Tooltip("Nếu có action map riêng cho revive thì nhập tên map.")]
    public string reviveOnlyActionMap = "";
    [Tooltip("Tên các InputAction cần disable khi DOWNED (vd: Fire, Reload, NextWeapon, PrevWeapon, Interact...)")]
    public string[] actionsToDisable = new string[] { "Fire", "AltFire", "Reload", "NextWeapon", "PrevWeapon", "Interact" };
    private readonly List<InputAction> _disabledActions = new();

    // runtime
    private bool _wasInputEnabled;
    private string _prevActionMap;
    private LifeState _lastState;

#if FUSION_WEAVER
    private FusionNetBridge _bridge;
#endif

    /// <summary>
    /// Local hay không: 
    /// - Nếu không có Runner (SP) => luôn true
    /// - Nếu có Runner (MP)      => dựa vào FusionNetBridge.IsLocalOwner
    /// </summary>
    private bool IsLocal
    {
        get
        {
#if FUSION_WEAVER
            if (_bridge == null)
                _bridge = GetComponentInParent<FusionNetBridge>();

            // Không có bridge => prefab SP / test → coi như local
            if (_bridge == null)
                return true;

            // Có bridge nhưng không có Runner hoặc Runner chưa chạy → coi như SP
            if (_bridge.Runner == null || !_bridge.Runner.IsRunning)
                return true;

            // Thực sự đang chạy MP → dùng HasInputAuthority
            return _bridge.IsLocalOwner;
#else
        // Build không dùng Fusion → luôn là local
        return true;
#endif
        }
    }


    void Reset()
    {
        // Dùng InParent để hỗ trợ cả prefab có cấu trúc nhiều level
        life = GetComponentInParent<PlayerLifeController>();
        move = GetComponentInParent<PlayerMovementController>();
        playerInput = GetComponentInParent<PlayerInput>();
        appearance = GetComponentInParent<PlayerAppearance>();
        playerRefs = GetComponentInParent<PlayerRefs>();
        if (!upperBodyAim)
            upperBodyAim = GetComponentInChildren<UpperBodyAimRigDriver>(true);
    }

    void Awake()
    {
        if (!life) life = GetComponentInParent<PlayerLifeController>();
        if (!move) move = GetComponentInParent<PlayerMovementController>();
        if (!playerInput) playerInput = GetComponentInParent<PlayerInput>();
        if (!playerRefs) playerRefs = GetComponentInParent<PlayerRefs>();
        appearance = GetComponentInParent<PlayerAppearance>();
        if (!upperBodyAim) upperBodyAim = GetComponentInChildren<UpperBodyAimRigDriver>(true);

#if FUSION_WEAVER
        _bridge = GetComponentInParent<FusionNetBridge>();
#endif
    }

    void OnEnable()
    {
/*        if (!life) Debug.LogError($"[DownedCtrl] life NULL trên {gameObject.name}");
        else Debug.Log($"[DownedCtrl] Enabled, life={life.name}, initial state={life.state}, IsLocal={IsLocal}");*/
        // 🔵 LUÔN bind event, kể cả proxy – phần disable input sẽ tự check IsLocal
        if (life != null)
        {
            life.OnDowned += OnDowned;
            life.OnRevived += OnRevivedOrRespawned;
            life.OnRespawned += OnRevivedOrRespawned;
            life.OnDead += OnDead;
        }

        _lastState = life ? life.state : LifeState.Alive;
        ApplyState(_lastState);

     //   Debug.Log($"[DownedCtrl] Enabled, initial state: {_lastState}, IsLocal={IsLocal}, go={gameObject.name}");
    }

    void OnDisable()
    {
        if (life != null)
        {
            life.OnDowned -= OnDowned;
            life.OnRevived -= OnRevivedOrRespawned;
            life.OnRespawned -= OnRevivedOrRespawned;
            life.OnDead -= OnDead;
        }

        // chỉ trả input cho local
        if (IsLocal)
            ApplyRevivedEnable();
    }

    void LateUpdate()
    {
        if (!life || !move) return;

        // 🔹 Local only: xử lý input & camera
        if (IsLocal)
        {
            if (autoGrabWeaponCam && !weaponCamera && playerRefs && playerRefs.cameraReady)
                weaponCamera = playerRefs.weaponCam;

            if (life.state == LifeState.Downed || life.state == LifeState.Dead)
            {
                move.wantsSprint = false;
                move.jumpPressedThisFrame = false;
            }
        }

        // 🔹 Tick lock đứng dậy sau revive (fallback nếu không dùng Animation Event)
        if (_reviveStandupActive)
        {
            _reviveStandupTimer -= Time.deltaTime;
            if (_reviveStandupTimer <= 0f)
            {
                EndReviveStandup();
            }
        }

        // 🔹 Mọi instance đều áp state (để lock body / tắt aim rig)
        if (life.state != _lastState)
        {
            ApplyState(life.state);
            _lastState = life.state;
        }
    }

    // ===== Event handlers from LifeCtrl =====
    void OnDowned(PlayerLifeController who)
    {
        // Debug.Log($"[DownedCtrl] OnDowned - THIS player: {life.gameObject.name}, IsLocal={IsLocal}");
        //        ApplyDownedDisable();
        ApplyState(who.state);
        SetDownedOverlay(true);
        SetDeadOverlay(false);
    }

    void OnRevivedOrRespawned(PlayerLifeController who)
    {
        //     Debug.Log($"[DownedCtrl] OnRevived - THIS player: {life.gameObject.name}, IsLocal={IsLocal}");
        //   ApplyRevivedEnable();
        ApplyState(who.state);
        SetDownedOverlay(false);
        SetDeadOverlay(false);
    }

    void OnDead(PlayerLifeController who)
    {
        //     Debug.Log($"[DownedCtrl] OnDead - THIS player: {life.gameObject.name}, IsLocal={IsLocal}");
        //   ApplyDownedDisable();
        ApplyState(who.state);
        SetDownedOverlay(false);
        SetDeadOverlay(true);
    }

    void ApplyState(LifeState s)
    {
        // 1) Lock xoay body khi Downed/Dead, unlock khi Alive
        if (move != null)
        {
            bool freezeBody = (s == LifeState.Downed || s == LifeState.Dead);
            move.SetBodyYawFrozen(freezeBody);
        }

        // 2) Bật / tắt upper body aim rig
        if (upperBodyAim != null)
        {
            bool enableAim = (s == LifeState.Alive);
            upperBodyAim.SetAimEnabled(enableAim, disableRig: true);
            // Dead / Downed -> weight Rig = 0 -> không còn giữ pose "nhìn"
        }

        // ✅ Local-only: Downed/Dead => show body (spectator), Alive => hide body (FPS)
        if (IsLocal && appearance != null)
        {
            bool showBody = (s == LifeState.Downed || s == LifeState.Dead);
            appearance.SetLocalWorldModelHidden(hidden: !showBody);
        }

        // 2) Xử lý input + overlay như cũ
        if (s == LifeState.Downed)
        {
            // Đã nằm → chắc chắn huỷ mọi lock đứng dậy cũ
            CancelReviveStandup();

            ApplyDownedDisable();
            SetDownedOverlay(true);
            SetDeadOverlay(false);
        }
        else if (s == LifeState.Dead)
        {
            CancelReviveStandup();

            ApplyDownedDisable();

            // DEAD: khóa luôn xoay camera (fix: downed->dead và SP skip downed)
            if (IsLocal)
                InputBlockerSystem.Add(InputBlocker.CameraLook);

            SetDownedOverlay(false);
            SetDeadOverlay(true);
        }
        else // Alive
        {
            SetDownedOverlay(false);
            SetDeadOverlay(false);

            // Nếu vừa được revive từ trạng thái Downed → khoá anim đứng dậy
            if (_lastState == LifeState.Downed)
            {
                BeginReviveStandup();
            }
            else
            {
                // Spawn / respawn bình thường → mở luôn như cũ
                CancelReviveStandup();
                ApplyRevivedEnable();
            }
        }
    }

    void BeginReviveStandup()
    {
        if (_reviveStandupActive)
            return;

        _reviveStandupActive = true;
        _reviveStandupTimer = reviveStandupDuration;

        // Khoá xoay thân (cho cả local lẫn proxy)
        if (move != null)
            move.SetBodyYawFrozen(true);

        if (!IsLocal)
            return;

        // Giữ block input gameplay, nhưng vẫn cho xoay camera
        InputBlockerSystem.Add(InputBlocker.Movement | InputBlocker.Combat | InputBlocker.Interaction);
        InputBlockerSystem.Remove(InputBlocker.CameraLook);

        // Weapon camera vẫn tắt trong lúc đang đứng dậy
        if (weaponCamera)
            weaponCamera.enabled = false;
    }

    void EndReviveStandup()
    {
        if (!_reviveStandupActive)
            return;

        _reviveStandupActive = false;
        _reviveStandupTimer = 0f;

        // Chỉ mở khoá nếu thực sự đang Alive (phòng trường hợp chết lại trong lúc đứng dậy)
        if (life != null && life.state == LifeState.Alive)
        {
            ApplyRevivedEnable();
        }
    }

    void CancelReviveStandup()
    {
        _reviveStandupActive = false;
        _reviveStandupTimer = 0f;
    }

    /// <summary>
    /// Cho Animation Event gọi khi anim đứng dậy (sau revive) kết thúc.
    /// </summary>
    public void NotifyReviveStandupAnimFinished()
    {
        EndReviveStandup();
    }


    void ApplyDownedDisable()
    {
        if (!IsLocal) return;
      //  Debug.Log("[DownedCtrl] APPLYING DOWNED DISABLE");

        // CHẶN INPUT GAMEPLAY nhưng vẫn cho nhìn
        InputBlockerSystem.Add(InputBlocker.Movement | InputBlocker.Combat | InputBlocker.Interaction);
        InputBlockerSystem.Remove(InputBlocker.CameraLook);

        // 0) Weapon camera
        if (weaponCamera)
        {
            weaponCamera.enabled = false;
        //    Debug.Log("[DownedCtrl] Weapon camera disabled");
        }

        // 1) Input maps (SP dùng PlayerInput trên player)
        if (playerInput)
        {
            if (!string.IsNullOrEmpty(reviveOnlyActionMap))
            {
                _prevActionMap = playerInput.currentActionMap != null ? playerInput.currentActionMap.name : "";
                playerInput.SwitchCurrentActionMap(reviveOnlyActionMap);
            }
            else if (disableAllInputMaps)
            {
                _wasInputEnabled = playerInput.enabled;
                playerInput.enabled = false;
            }

            // 1b) Disable các action cụ thể
            _disabledActions.Clear();
            if (playerInput.actions != null && actionsToDisable != null)
            {
                foreach (var name in actionsToDisable)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    var act = playerInput.actions.FindAction(name, throwIfNotFound: false);
                    if (act != null && act.enabled)
                    {
                        act.Disable();
                        _disabledActions.Add(act);
                    }
                }
            }
        }

        // 2) Disable behaviours (bắn/nhặt/ADS/switch nếu là component khác)
        foreach (var b in disableWhileDowned)
            if (b) b.enabled = false;
    }


    void ApplyRevivedEnable()
    {
        if (!IsLocal) return;
    //    Debug.Log("[DownedCtrl] APPLYING REVIVED ENABLE");

        InputBlockerSystem.Clear();

        // 0) Weapon camera
        if (weaponCamera)
        {
            weaponCamera.enabled = true;
          //  Debug.Log("[DownedCtrl] Weapon camera enabled");
        }

        // 1) Trả input
        if (playerInput)
        {
            if (!string.IsNullOrEmpty(reviveOnlyActionMap))
            {
                if (!string.IsNullOrEmpty(_prevActionMap))
                    playerInput.SwitchCurrentActionMap(_prevActionMap);
            }
            else if (disableAllInputMaps)
            {
                playerInput.enabled = _wasInputEnabled || true;
            }

            // 1b) Re-enable các action cụ thể
            foreach (var act in _disabledActions)
                if (act != null && !act.enabled) act.Enable();
            _disabledActions.Clear();
        }

        // 2) Enable behaviours
        foreach (var b in disableWhileDowned)
            if (b) b.enabled = true;
    }

    void SetDownedOverlay(bool active)
    {
        if (!IsLocal) return;

        var overlay = DownedOverlayUI.Instance;
        if (overlay != null)
        {
            overlay.SetVisible(active);
        }
    }

    void SetDeadOverlay(bool active)
    {
        if (!IsLocal) return;

        var overlay = DeadOverlayUI.Instance;
        if (overlay != null)
        {
            overlay.SetVisible(active);
        }
    }

}
