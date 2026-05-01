using System.Collections.Generic;
using UnityEngine;

#if FUSION_WEAVER
using Fusion;
#endif

/// <summary>
/// Local-only:
/// - Khi player Downed (MP) -> bật spectator cam (third person),
/// - Sau delay cho phép cycle giữa các player bằng chuột trái/phải.
/// </summary>
[DisallowMultipleComponent]
public class PlayerSpectatorController : MonoBehaviour
{
    [Header("Refs")]
    public PlayerLifeController life;
    public PlayerRefs playerRefs;

    [Header("Settings")]
    [Tooltip("Delay sau khi Downed trước khi cho phép cycle (giây).")]
    public float spectateDelay = 2.0f;

    private float _downedEnterTime;
    private bool _spectateActive;
    private bool _waitingDelay;
    private PlayerRefs _currentTarget;

    [Header("Orbit Camera")]
    private Transform orbitRoot;

    private Transform _currentAnchor;
    private float _yaw;
    private float _pitch;

    [Header("Spectator Input")]
    public float cycleCooldown = 0.3f; // Thêm cooldown
    private float _lastCycleTime;


#if FUSION_WEAVER
    private FusionNetBridge _bridge;
#endif

    // ===== Helpers =====

    private bool IsLocal
    {
        get
        {
#if FUSION_WEAVER
            if (_bridge == null)
                _bridge = GetComponentInParent<FusionNetBridge>();

            // SP hoặc prefab test không có bridge => coi như local
            if (_bridge == null)
                return true;

            return _bridge.IsLocalOwner;
#else
            return true;
#endif
        }
    }

    void Reset()
    {
        life = GetComponent<PlayerLifeController>();
        playerRefs = GetComponent<PlayerRefs>();
    }

    void Awake()
    {
        if (!life) life = GetComponent<PlayerLifeController>();
        if (!playerRefs) playerRefs = GetComponent<PlayerRefs>();

#if FUSION_WEAVER
        _bridge = GetComponentInParent<FusionNetBridge>();
#endif
    }

    void OnEnable()
    {
        if (life != null)
        {
            life.OnDowned += OnDowned;
            life.OnRevived += OnRevivedOrRespawned;
            life.OnRespawned += OnRevivedOrRespawned;
            life.OnDead += OnDead;
        }

        GameOverManager.OnGameOver += OnGameOver;
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

        GameOverManager.OnGameOver -= OnGameOver;

        if (IsLocal)
            DeactivateSpectatorCam();
    }

    void OnGameOver()
    {
        if (!IsLocal) return;
        DeactivateSpectatorCam();
    }

    // ===== Life events =====

    void OnDowned(PlayerLifeController who)
    {
        if (!IsLocal) return;

        // Chỉ spec trong mode có Downed (MP)
        if (!life.enableDownedInThisMode) return;

        EnsureOrbitRoot();          // 👈 THÊM
        if (!orbitRoot) return;     // không có thì thôi, khỏi spec

        _downedEnterTime = Time.time;
        _waitingDelay = true;

        _currentTarget = playerRefs;
        _currentAnchor = GetSpectatorAnchor(_currentTarget);

        // Lấy hướng cam hiện tại làm góc bắt đầu cho orbit
        var cam = Camera.main;
        if (cam)
        {
            var e = cam.transform.rotation.eulerAngles;
            _yaw = e.y;
            _pitch = e.x;
        }

        ActivateSpectatorCam();
    }

    void OnRevivedOrRespawned(PlayerLifeController who)
    {
        if (!IsLocal) return;
        DeactivateSpectatorCam();
    }

    void OnDead(PlayerLifeController who)
    {
        if (!IsLocal) return;
        DeactivateSpectatorCam();
    }

    // ===== Update spectate =====

    void Update()
    {
        if (!IsLocal) return;
        if (!_spectateActive) return;
        if (!life || life.state != LifeState.Downed) return;

        EnsureOrbitRoot();
        if (!orbitRoot) return;

        // Luôn bám vị trí anchor
        if (orbitRoot && _currentAnchor)
        {
            orbitRoot.position = _currentAnchor.position;
        }

        // Delay 2s trước khi cho phép cycle
        if (_waitingDelay)
        {
            if (Time.time - _downedEnterTime < spectateDelay)
                return;
            _waitingDelay = false;
        }

#if FUSION_WEAVER
        var provider = FusionInputProvider.Instance;
        if (provider == null) return;

        // ✅ Nếu đang GameOver / bị block look -> không cho xoay spectator
        if (InputBlockerSystem.Has(InputBlocker.Full) || InputBlockerSystem.Has(InputBlocker.CameraLook))
            return;

        // ✅ Lấy input đã tôn trọng InputBlockerSystem
        Vector2 look = provider.GetInputData().look;

        if (orbitRoot)
            orbitRoot.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

        // 🔥 THÊM COOLDOWN cho spectator cycle
        if (Time.time - _lastCycleTime < cycleCooldown)
            return;

        // Chuột trái = next, chuột phải = prev
        bool next = provider.SpectateNextDown;
        bool prev = provider.SpectatePrevDown;

        if (next || prev)
        {
            _lastCycleTime = Time.time; // 🔥 Reset cooldown

            int dir = next ? +1 : -1;
            CycleTarget(dir);

            Debug.Log($"[Spectator] Cycle to {(next ? "next" : "prev")} target");
        }
#endif
    }


    // ===== Core spectate logic =====

    void ActivateSpectatorCam()
    {
        var binder = CameraBinder.Instance;
        if (!binder)
        {
            Debug.LogWarning("[Spectator] No CameraBinder in scene.");
            return;
        }

        EnsureOrbitRoot();
        if (!orbitRoot)
        {
            Debug.LogWarning("[Spectator] No orbitRoot (spectatorOrbitRoot not set on CameraBinder).");
            return;
        }

        binder.SetSpectatorTarget(orbitRoot);
        binder.SetSpectatorActive(true);

        if (_currentAnchor)
            orbitRoot.position = _currentAnchor.position;

        _spectateActive = true;
        Debug.Log("[Spectator] Activated for local downed player.");
    }

    void DeactivateSpectatorCam()
    {
        if (!_spectateActive) return;

        var binder = CameraBinder.Instance;
        if (binder != null)
        {
            binder.SetSpectatorActive(false);
            // Không cần đổi Follow/LookAt; lần sau Activate sẽ set lại
        }

        _spectateActive = false;
        _waitingDelay = false;
        _currentTarget = null;
        _currentAnchor = null;

        Debug.Log("[Spectator] Deactivated (revived/dead/disabled).");
    }


    Transform GetSpectatorAnchor(PlayerRefs refs)
    {
        if (refs == null) return transform;

        // Nếu bạn đã thêm field spectatorAnchor trong PlayerRefs thì dùng nó,
        // nếu chưa thì fallback về camFollowTarget rồi tới transform.
        if (refs.spectatorAnchor != null)
            return refs.spectatorAnchor;
        if (refs.camFollowTarget != null)
            return refs.camFollowTarget;
        return refs.transform;
    }

    void CycleTarget(int dir)
    {
        var all = PlayerRegistry.GetAllValidPlayers();
        if (all == null || all.Count == 0) return;

        // Lọc: chỉ spectate player chưa Dead (Alive hoặc Downed)
        var candidates = new List<PlayerRefs>(all.Count);
        foreach (var p in all)
        {
            if (!p) continue;
            var life = p.GetComponent<PlayerLifeController>();
            if (life != null && life.state == LifeState.Dead) continue;
            candidates.Add(p);
        }

        if (candidates.Count == 0) return;

        // Tìm index current target
        int idx = 0;
        if (_currentTarget != null)
        {
            int found = candidates.IndexOf(_currentTarget);
            if (found >= 0) idx = found;
        }

        idx = (idx + dir + candidates.Count) % candidates.Count;
        _currentTarget = candidates[idx];
        _currentAnchor = GetSpectatorAnchor(_currentTarget);

        var binder = CameraBinder.Instance;
        if (!binder) return;

        // orbitRoot sẽ bám theo anchor trong Update()
        EnsureOrbitRoot();
        if (_currentAnchor && orbitRoot)
            orbitRoot.position = _currentAnchor.position;

        Debug.Log($"[Spectator] Now spectating: {_currentTarget.gameObject.name}");
    }

    void EnsureOrbitRoot()
    {
        if (orbitRoot) return;

        var binder = CameraBinder.Instance;
        if (binder && binder.spectatorOrbitRoot)
        {
            orbitRoot = binder.spectatorOrbitRoot;
        }
    }

}
