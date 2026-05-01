using UnityEngine;
#if FUSION_WEAVER
using Fusion;
#endif
using TT; // để gọi AudioEvents

/// <summary>
/// Hòm random vũ khí kiểu mystery box.
/// Không equip trực tiếp, chỉ spawn WorldWeapon để player nhặt.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class RandomWeaponBoxSpot : MonoBehaviour
{
    public enum BoxState
    {
        ClosedIdle,     // Hòm đóng, rảnh, có thể dùng
        Opening,        // Đang mở nắp
        Raising,        // Súng bay từ trong hòm lên trên
        Offering,       // Súng đứng im ở trên, có thể nhặt
        Returning,      // Không ai nhặt → súng bay về lại trong hòm
        Closing         // Đang đóng nắp
    }

    [Header("Random Box")]
    [Tooltip("Giá quay random 1 vũ khí.")]
    public int cost = 950;

    [Tooltip("Danh sách vũ khí có thể random ra.")]
    public WeaponDef[] candidates;

    [Tooltip("Điểm spawn WorldWeapon (top point). Nếu null sẽ dùng chính transform của box.")]
    public Transform spawnPoint;

    [Header("Usage")]
    [Tooltip("Nếu false thì box chỉ dùng được maxUses lần.")]
    public bool infiniteUse = true;

    [Min(1)]
    public int maxUses = 3;

    [Tooltip("Khoảng cách tối đa để cho phép tương tác.")]
    public float interactRange = 2.0f;

    [Header("Pending Weapon")]
    [Tooltip("Thời gian tối đa cho súng nằm trên hòm trước khi tự biến mất (giây). Fallback an toàn.")]
    public float pendingLifetime = 60f;

    [SerializeField] private WorldWeapon pendingWeapon;
    private float _pendingSpawnTime;
    private int _uses;

    [Header("Motion (code điều khiển súng bay lên/xuống)")]
    [Tooltip("Điểm bắt đầu (trong hòm). Nếu null sẽ dùng spawnPoint hoặc transform.")]
    public Transform weaponStartPoint;

    [Tooltip("Điểm kết thúc (bên trên hòm, nơi player nhặt). Nếu null sẽ dùng spawnPoint hoặc transform.")]
    public Transform weaponTopPoint;

    // --- Visual random ---
    [Header("Visual Random Weapon (Model Cloning)")]
    public Transform visualRoot; // đặt 1 empty object trong box để chứa visual model
    private GameObject _visualInstance;

    [Header("Light / VFX")]
    [Tooltip("Ánh sáng vàng bật khi hòm đang mở (có thể là GameObject chứa Light hoặc VFX).")]
    public GameObject goldenLightObject;

    [Tooltip("Thời gian súng bay lên.")]
    public float raiseDuration = 1.2f;

    [Tooltip("Thời gian súng đứng ở vị trí trên, cho phép nhặt.")]
    public float offerDuration = 8f;

    [Tooltip("Thời gian súng bay xuống lại hòm nếu không ai nhặt.")]
    public float returnDuration = 1.2f;

    [Header("Anim")]
    [Tooltip("Animator điều khiển nắp hòm.")]
    public Animator boxAnimator;

    [Tooltip("Tên trigger để mở nắp.")]
    public string openTrigger = "Open";

    [Tooltip("Tên trigger để đóng nắp.")]
    public string closeTrigger = "Close";

    [Tooltip("Thời lượng anim mở nắp (để sync sang Raising).")]
    public float openAnimDuration = 0.7f;

    [Tooltip("Thời lượng anim đóng nắp (để biết lúc nào quay về Idle).")]
    public float closeAnimDuration = 0.7f;

    [Header("Audio (IDS từ AudioEventSO)")]
    [Tooltip("SFX khi hòm mở (3D, 1-shot).")]
    public int sfxOpenEventId;

    [Tooltip("Nhạc random box (3D, 1-shot, không loop).")]
    public int musicEventId;

    [Tooltip("SFX khi hòm đóng.")]
    public int sfxCloseEventId;

    [Header("Raising Randomization")]
    [Tooltip("Có random đổi mẫu súng trong lúc đang bay lên không (kiểu COD).")]
    public bool randomizeDuringRaise = true;

    [Tooltip("Khoảng thời gian giữa các lần random model trong lúc Raising.")]
    public float raiseRandomInterval = 0.12f;

    // --- State machine runtime ---
    [SerializeField, Tooltip("Debug xem state hiện tại")]
    private BoxState _state = BoxState.ClosedIdle;
    private float _stateStartTime;

    [Tooltip("Network animator cho hòm (sync SP/MP).")]
    [SerializeField] private RandomBoxNetworkAnimator netAnimator;

    // Dữ liệu roll cho lần mở hiện tại
    private PlayerPickup _rollingPlayer;
    private WeaponDef _finalDef;
    private int _finalMag;
    private int _finalReserve;
    private string _finalRuntimeGuid;
    private float _nextRaiseRandomTime;

    private bool _announcedReveal;

    // Cache vị trí chuyển động
    private Vector3 _raiseStartPos;
    private Vector3 _raiseEndPos;

    private Vector3 _returnStartPos;
    private Vector3 _returnEndPos;

#if FUSION_WEAVER
    private NetworkObject _netObj;

    bool HasBoxAuthority()
    {
        // Không có NetworkObject hoặc chưa có Runner → coi như SP/offline, cho phép chạy logic
        if (_netObj == null || _netObj.Runner == null)
            return true;

        return _netObj.HasStateAuthority;
    }
#endif

    void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    void Awake()
    {
#if FUSION_WEAVER
        _netObj = GetComponent<NetworkObject>();
#endif
        if (!netAnimator)
            netAnimator = GetComponent<RandomBoxNetworkAnimator>();

        // Đảm bảo lúc mới vào game hòm đang đóng thì ánh sáng tắt
        SetGoldenLight(false);
    }

    void SetGoldenLight(bool enabled)
    {
        if (goldenLightObject != null)
            goldenLightObject.SetActive(enabled);
    }

    void Update()
    {
#if FUSION_WEAVER
        if (HasBoxAuthority())
        {
            // Host / SP: full logic
            UpdateStateMachine();
            UpdatePendingLifetimeFallback();
        }
        else
        {
            // Client: chỉ chạy bản proxy (visual-only)
            UpdateStateMachineProxy();
        }
#else
    // Build không Fusion: coi như SP
    UpdateStateMachine();
    UpdatePendingLifetimeFallback();
#endif
    }

    void UpdateStateMachineProxy()
    {
        switch (_state)
        {
            case BoxState.Opening:
                UpdateOpeningProxy();
                break;

            case BoxState.Raising:
                UpdateRaisingProxy();
                break;

                // Các state khác (Offering / Returning / Closing) 
                // hiện tại không cần làm gì cho client (real weapon đã sync qua NetworkTransform)
        }
    }

    void UpdateOpeningProxy()
    {
        if (Time.time >= _stateStartTime + Mathf.Max(0.01f, openAnimDuration))
        {
            EnterRaisingProxy();
        }
    }

    void EnterRaisingProxy()
    {
        _state = BoxState.Raising;
        _stateStartTime = Time.time;

        // Điểm bắt đầu/điểm top giống host
        Transform startAnchor = GetStartAnchor();
        if (startAnchor == null)
            startAnchor = GetSpawnAnchor();

        Transform topAnchor = GetTopAnchor();
        if (topAnchor == null)
            topAnchor = GetSpawnAnchor();

        _raiseStartPos = startAnchor.position;
        _raiseEndPos = topAnchor.position;

        if (visualRoot != null)
            visualRoot.position = _raiseStartPos;

        if (randomizeDuringRaise)
        {
            _nextRaiseRandomTime = Time.time;
            RebuildVisualForDef(GetRandomWeapon());
        }
        else
        {
            // Không cần finalDef trên client, chỉ cần 1 mẫu bất kỳ để bay
            RebuildVisualForDef(GetRandomWeapon());
        }
    }

    void UpdateRaisingProxy()
    {
        float t = Mathf.Clamp01((Time.time - _stateStartTime) / Mathf.Max(raiseDuration, 0.01f));

        if (_visualInstance != null)
        {
            _visualInstance.transform.position = Vector3.Lerp(_raiseStartPos, _raiseEndPos, t);
        }

        // Random model local cho đẹp, không cần giống hệt host
        if (randomizeDuringRaise && Time.time >= _nextRaiseRandomTime && t < 1f)
        {
            _nextRaiseRandomTime = Time.time + raiseRandomInterval;

            var rand = GetRandomWeapon();
            if (rand != null)
            {
                RebuildVisualForDef(rand);
            }
        }

        if (t >= 1f)
        {
            if (_visualInstance != null)
            {
                Destroy(_visualInstance);
                _visualInstance = null;
            }

            // Sau khi visual bay xong, real weapon đã được spawn bởi host
            // Client chỉ cần chuyển state sang Offering để không lặp lại nữa
            _state = BoxState.Offering;
            _stateStartTime = Time.time;
        }
    }


    void UpdateStateMachine()
    {
        switch (_state)
        {
            case BoxState.Opening:
                UpdateOpening();
                break;
            case BoxState.Raising:
                UpdateRaising();
                break;
            case BoxState.Offering:
                UpdateOffering();
                break;
            case BoxState.Returning:
                UpdateReturning();
                break;
            case BoxState.Closing:
                UpdateClosing();
                break;
        }
    }

    void UpdateOpening()
    {
        if (Time.time >= _stateStartTime + Mathf.Max(0.01f, openAnimDuration))
        {
            EnterRaising();
        }
    }

    void EnterRaising()
    {
        _state = BoxState.Raising;
        _stateStartTime = Time.time;

        if (_rollingPlayer == null || _finalDef == null)
        {
            Debug.LogWarning("[RandomBox] Missing _rollingPlayer/_finalDef in EnterRaising. Closing box.");
            StartClosing();
            return;
        }

        // Điểm bắt đầu visual (trong hòm)
        Transform startAnchor = GetStartAnchor();
        if (startAnchor == null)
            startAnchor = GetSpawnAnchor();

        // Điểm top nơi súng thật sẽ đứng để nhặt
        Transform topAnchor = GetTopAnchor();
        if (topAnchor == null)
            topAnchor = GetSpawnAnchor();

        _raiseStartPos = startAnchor.position;
        _raiseEndPos = topAnchor.position;

        // Visual clone bắt đầu ở start
        if (visualRoot != null)
            visualRoot.position = _raiseStartPos;

        if (randomizeDuringRaise)
        {
            _nextRaiseRandomTime = Time.time;
            RebuildVisualForDef(GetRandomWeapon());
        }
        else
        {
            // Không random trong Raising thì cho visual luôn là final weapon
            RebuildVisualForDef(_finalDef);
        }

        // Tăng số lần dùng box
        NotifyUsed();
    }


    void UpdateRaising()
    {
        float t = Mathf.Clamp01((Time.time - _stateStartTime) / Mathf.Max(raiseDuration, 0.01f));

        // Di chuyển visual clone trong Raising
        if (_visualInstance != null)
        {
            _visualInstance.transform.position = Vector3.Lerp(_raiseStartPos, _raiseEndPos, t);
        }

        // Random model
        if (randomizeDuringRaise && Time.time >= _nextRaiseRandomTime && t < 1f)
        {
            _nextRaiseRandomTime = Time.time + raiseRandomInterval;

            var rand = GetRandomWeapon();
            if (rand != null)
            {
                RebuildVisualForDef(rand);
            }
        }

        // Đến top point → chốt súng + spawn world weapon
        if (t >= 1f)
        {
            // Xoá visual
            if (_visualInstance != null)
            {
                Destroy(_visualInstance);
                _visualInstance = null;
            }

            // Nếu chưa spawn weapon thật thì spawn bây giờ
            if (pendingWeapon == null)
            {
                Transform topAnchor = GetTopAnchor();
                if (topAnchor == null)
                    topAnchor = GetSpawnAnchor();

                var ww = _rollingPlayer.SpawnRandomBoxWeapon(
                    _finalDef, _finalMag, _finalReserve, _finalRuntimeGuid, topAnchor);

                if (ww == null)
                {
                    Debug.LogWarning("[RandomBox] Failed to spawn weapon at end of Raising. Closing box.");
                    StartClosing();
                    return;
                }

                RegisterPendingWeapon(ww);

                if (!_announcedReveal)
                {
                    _announcedReveal = true;
                    _rollingPlayer?.AnnounceRandomBoxResultDeferred(_finalDef != null ? _finalDef.weaponName : null);
                }
            }

            // Cho nhặt luôn (không block)
            pendingWeapon.BlockPickupFor(0f);

            _state = BoxState.Offering;
            _stateStartTime = Time.time;
        }
    }


    void UpdateOffering()
    {
        // Nếu weapon đã được nhặt (pendingWeapon null) → đóng hòm
        RefreshPendingStatus();
        if (pendingWeapon == null)
        {
            StartClosing();
            return;
        }

        // Nếu hết thời gian cho phép nhặt → bắt đầu bay xuống lại
        if (offerDuration > 0f && Time.time >= _stateStartTime + offerDuration)
        {
            EnterReturning();
        }
    }

    void EnterReturning()
    {
        _state = BoxState.Returning;
        _stateStartTime = Time.time;

        if (pendingWeapon != null)
        {
            Transform topAnchor = GetTopAnchor();
            _returnStartPos = pendingWeapon.transform.position;
            _returnEndPos = GetStartAnchor() != null
                ? GetStartAnchor().position
                : (spawnPoint != null ? spawnPoint.position : transform.position);

            // Không cho nhặt trong lúc trả về
            pendingWeapon.BlockPickupFor(returnDuration);
        }
        else
        {
            StartClosing();
        }
    }

    void UpdateReturning()
    {
        if (pendingWeapon == null)
        {
            StartClosing();
            return;
        }

        float t = Mathf.Clamp01((Time.time - _stateStartTime) / Mathf.Max(returnDuration, 0.01f));
        pendingWeapon.transform.position = Vector3.Lerp(_returnStartPos, _returnEndPos, t);

        if (t >= 1f)
        {
            // Về lại trong hòm mà vẫn không ai nhặt → despawn weapon
#if FUSION_WEAVER
            var wNo = pendingWeapon.GetComponent<NetworkObject>();
            if (wNo != null && wNo.Runner != null)
                wNo.Runner.Despawn(wNo);
            else
                Destroy(pendingWeapon.gameObject);
#else
            Destroy(pendingWeapon.gameObject);
#endif
            pendingWeapon = null;
            StartClosing();
        }
    }

    void UpdateClosing()
    {
        if (Time.time >= _stateStartTime + Mathf.Max(0.01f, closeAnimDuration))
        {
            // Quay lại Idle
            _state = BoxState.ClosedIdle;
            _rollingPlayer = null;
            _finalDef = null;
            _finalRuntimeGuid = null;
            _finalMag = 0;
            _finalReserve = 0;

            SetGoldenLight(false);
        }
    }

    void StartClosing()
    {
        _announcedReveal = false;

        if (_state == BoxState.Closing)
            return;

        if (_visualInstance != null)
        {
            Destroy(_visualInstance);
            _visualInstance = null;
        }


        _state = BoxState.Closing;
        _stateStartTime = Time.time;

        if (netAnimator != null)
        {
            netAnimator.PlayClose();
        }
        else if (boxAnimator && !string.IsNullOrEmpty(closeTrigger))
        {
            boxAnimator.SetTrigger(closeTrigger);
        }
        PlayCloseAudio();
    }

    void UpdatePendingLifetimeFallback()
    {
        if (pendingWeapon != null && pendingLifetime > 0f)
        {
            if (Time.time >= _pendingSpawnTime + pendingLifetime)
            {
                Debug.Log($"[RandomBox] Pending weapon timed out after {pendingLifetime} seconds, despawning.");

#if FUSION_WEAVER
                var wNo = pendingWeapon.GetComponent<NetworkObject>();
                if (wNo != null && wNo.Runner != null)
                {
                    wNo.Runner.Despawn(wNo);
                }
                else
                {
                    Destroy(pendingWeapon.gameObject);
                }
#else
                Destroy(pendingWeapon.gameObject);
#endif

                pendingWeapon = null;

                if (_state == BoxState.Offering || _state == BoxState.Raising || _state == BoxState.Returning)
                {
                    StartClosing();
                }
            }
        }
    }

    // ------------- Public API cũ (PlayerPickup đang dùng) -------------

    public bool CanUse()
    {
        if (!infiniteUse && _uses >= maxUses)
            return false;

        // Hòm đang bận thì không cho dùng
        if (_state != BoxState.ClosedIdle)
            return false;

        // Đang có pending weapon thì cũng không cho mua mới
        if (HasPendingWeapon)
            return false;

        return true;
    }

    public WeaponDef GetRandomWeapon()
    {
        if (candidates == null || candidates.Length == 0)
            return null;
        int idx = Random.Range(0, candidates.Length);
        return candidates[idx];
    }

    public Transform GetSpawnAnchor()
    {
        return spawnPoint ? spawnPoint : transform;
    }

    Transform GetStartAnchor()
    {
        if (weaponStartPoint != null)
            return weaponStartPoint;
        // fallback: cho start = transform (trong hòm)
        return transform;
    }

    Transform GetTopAnchor()
    {
        if (weaponTopPoint != null)
            return weaponTopPoint;
        // fallback: dùng spawnPoint / transform
        return spawnPoint != null ? spawnPoint : transform;
    }

    /// <summary>
    /// Đang có vũ khí nằm sẵn trên hòm chưa được nhặt?
    /// </summary>
    public bool HasPendingWeapon => pendingWeapon != null;

    public void NotifyUsed()
    {
        if (!infiniteUse)
            _uses = Mathf.Min(_uses + 1, maxUses);
    }

    public WorldWeapon GetPendingWeapon() => pendingWeapon;

    public void RegisterPendingWeapon(WorldWeapon ww)
    {
        pendingWeapon = ww;
        _pendingSpawnTime = Time.time;
    }

    /// <summary>
    /// Gọi trước khi check HasPendingWeapon để clear reference nếu WorldWeapon đã bị nhặt/destroy.
    /// </summary>
    public void RefreshPendingStatus()
    {
        if (pendingWeapon == null)
            pendingWeapon = null; // chỉ để Unity refresh reference
    }

    /// <summary>
    /// Host / SP gọi khi player mua hòm thành công. 
    /// Khởi động flow Opening → Raising → Offering → Returning / Closing.
    /// </summary>
    public bool BeginRoll(PlayerPickup requester, WeaponDef def, int mag, int reserve, string runtimeGuid)
    {

        if (!CanUse())
        {
            Debug.Log("[RandomBox] BeginRoll called but box cannot use now.");
            return false;
        }

        _announcedReveal = false;

        _rollingPlayer = requester;
        _finalDef = def;
        _finalMag = mag;
        _finalReserve = reserve;
        _finalRuntimeGuid = runtimeGuid;

        _state = BoxState.Opening;
        _stateStartTime = Time.time;

        // Anim mở nắp
        if (netAnimator != null)
        {
            netAnimator.PlayOpen();
        }
        else if (boxAnimator && !string.IsNullOrEmpty(openTrigger))
        {
            // Fallback nếu quên gán netAnimator
            boxAnimator.SetTrigger(openTrigger);
        }

        // Audio mở hòm + nhạc random (hiện tại vẫn local trên host/SP)
        PlayOpenAudio();

        SetGoldenLight(true);

        return true;
    }


    /// <summary>
    /// Đổi visual của pendingWeapon sang WeaponDef chỉ định (giữ nguyên runtimeGuid + ammo).
    /// Dùng để random model trong lúc Raising và chốt súng ở cuối.
    /// </summary>

    GameObject RebuildVisualForDef(WeaponDef def)
    {
        if (visualRoot == null || def == null || def.worldPrefab == null)
            return null;

        // Xoá bản cũ nếu có
        if (_visualInstance != null)
        {
            Destroy(_visualInstance);
            _visualInstance = null;
        }

        // Clone world prefab
        GameObject clone = Instantiate(def.worldPrefab, visualRoot);

        // Xoá thành phần không cần (NetworkObject, WorldWeapon, Collider…)
        foreach (var w in clone.GetComponentsInChildren<WorldWeapon>(true))
            Destroy(w);

        foreach (var c in clone.GetComponentsInChildren<Collider>(true))
            Destroy(c);

        foreach (var r in clone.GetComponentsInChildren<Rigidbody>(true))
            Destroy(r);

        // Reset transform local
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;

        _visualInstance = clone;
        return clone;
    }

    void PlayOpenAudio()
    {
#if FUSION_WEAVER
        if (_netObj != null && _netObj.Runner != null)
        {
            if (sfxOpenEventId != 0)
                AudioEvents.PlayWorld3D(sfxOpenEventId, transform.position);

            if (musicEventId != 0)
                AudioEvents.PlayWorld3D(musicEventId, transform.position);
            return;
        }
#endif
        // Fallback SP / không có Runner
        if (sfxOpenEventId != 0)
            AudioEvents.PlayWorld3DAttached(sfxOpenEventId, transform);
        if (musicEventId != 0)
            AudioEvents.PlayWorld3DAttached(musicEventId, transform);
    }

    void PlayCloseAudio()
    {
#if FUSION_WEAVER
        if (_netObj != null && _netObj.Runner != null)
        {
            if (sfxCloseEventId != 0)
                AudioEvents.PlayWorld3D(sfxCloseEventId, transform.position);
            return;
        }
#endif
        if (sfxCloseEventId != 0)
            AudioEvents.PlayWorld3DAttached(sfxCloseEventId, transform);
    }

    public void ForceLight(bool enabled)
    {
        SetGoldenLight(enabled);
    }

    public void OnRemoteOpen()
    {
#if FUSION_WEAVER
        // Host / SP đã set state trong BeginRoll rồi → bỏ qua
        if (HasBoxAuthority())
            return;
#endif
        // Client proxy: bắt đầu Opening từ bây giờ
        _state = BoxState.Opening;
        _stateStartTime = Time.time;

        PlayOpenAudio();
    }

    public void OnRemoteClose()
    {
#if FUSION_WEAVER
        if (HasBoxAuthority())
            return;
#endif
        PlayCloseAudio();

        _state = BoxState.ClosedIdle;
        _stateStartTime = Time.time;

        _rollingPlayer = null;
        _finalDef = null;
        _finalRuntimeGuid = null;
        _finalMag = 0;
        _finalReserve = 0;

        if (_visualInstance != null)
        {
            Destroy(_visualInstance);
            _visualInstance = null;
        }
    }

}
