using System.Collections;
using UnityEngine;
#if FUSION_WEAVER
using Fusion;
#endif

/// <summary>
/// Spawn & quản lý model súng 3rd-person dựa trên ILoadoutState (SP/MP).
/// - Mỗi slot có thể có 1 instance thirdPersonPrefab (nếu được set trong WeaponDef).
/// - Chỉ slot đang ActiveSlot mới được SetActive(true) (gắn ở tay).
/// - Sau này có thể mở rộng holster (primary/secondary) nếu muốn.
/// </summary>
[DisallowMultipleComponent]
public class PlayerWeaponWorldView : MonoBehaviour
{
    [Header("References")]
    [AutoBindInParent, SerializeField]
    private PlayerStateProvider stateProvider;

    [Tooltip("Socket trên model 3D dùng để gắn súng đang cầm (thường là WeaponSocket_Hand_R).")]
    [SerializeField] private Transform activeWeaponRoot;

    [Tooltip("Optional: socket để holster súng chính khi không cầm.")]
    [SerializeField] private Transform primaryHolsterRoot;

    [Tooltip("Optional: socket để holster súng phụ khi không cầm.")]
    [SerializeField] private Transform secondaryHolsterRoot;

    [Tooltip("Optional: nếu có, sẽ được cập nhật cờ HasWeapon (dùng cho anim).")]
    [SerializeField] private PlayerNetworkAnimator networkAnimator;

    [Tooltip("Animator world model để đọc IsADS cho blend offset.")]
    [SerializeField] private Animator worldAnimator;

    [Header("ADS offset blend")]
    [SerializeField] private string adsBoolParam = "IsADS";

    [Tooltip("Tốc độ blend offset giữa hip và ADS.")]
    [SerializeField] private float adsOffsetLerpSpeed = 10f;

    [Header("Local visibility")]
    [SerializeField] private bool hideWorldWeaponForLocal = true;

#if FUSION_WEAVER
    [SerializeField] private FusionNetBridge _netBridge;
#endif

    Transform[] _worldCasingEjectPoints;

    // Runtime
    private ILoadoutState _loadout;
    private bool _bound;

    private GameObject[] _instances;
    private WeaponDef[] _defs;
    private int _cachedSlotCount = -1;

    private int _adsBoolHash;
    private float _adsBlend; // 0 = hip, 1 = ADS
    private int _currentActiveSlot = -1;

    GameObject[] _worldFxRoots;
    ParticleSystem[][] _worldFxParticles;
    Coroutine[] _worldFxAutoOff;
    float[] _worldFxLife;


#if FUSION_WEAVER
    private NetworkBehaviour _loadoutNetBehaviour;
#endif

    void Awake()
    {
        if (!activeWeaponRoot)
            activeWeaponRoot = transform;

        if (!stateProvider)
            stateProvider = GetComponentInParent<PlayerStateProvider>(true);

        if (!networkAnimator)
            networkAnimator = GetComponentInParent<PlayerNetworkAnimator>(true);

        if (!worldAnimator)
            worldAnimator = GetComponentInParent<Animator>(true);

#if FUSION_WEAVER
        if (_netBridge == null)
            _netBridge = GetComponentInParent<FusionNetBridge>(true);
#endif

        if (!string.IsNullOrEmpty(adsBoolParam) && worldAnimator != null)
            _adsBoolHash = Animator.StringToHash(adsBoolParam);
    }

    void OnEnable()
    {
        StartCoroutine(Co_BindAndPrime());
    }

    void OnDisable()
    {
        Unbind();
        ClearInstances();
    }

    void Update()
    {
        UpdateADSOffset();
    }

    void UpdateADSOffset()
    {
        if (_instances == null || _defs == null)
            return;

        if (_currentActiveSlot < 0 || _currentActiveSlot >= _instances.Length)
            return;

        var inst = _instances[_currentActiveSlot];
        var def = _defs[_currentActiveSlot];
        if (!inst || def == null)
            return;

        // Đọc IsADS từ Animator (đã được sync qua PlayerNetworkAnimator)
        bool isADS = false;

        // Ưu tiên đọc ADS từ input đã replicate (remote/proxy cũng có)
#if FUSION_WEAVER
        if (_netBridge != null && _netBridge.Object != null && _netBridge.Object.IsValid)
        {
            isADS = _netBridge.LastInput.ads;
        }
        else
#endif
        {
            // Fallback (giữ hành vi cũ): đọc từ animator bool nếu không có netbridge
            if (worldAnimator != null && _adsBoolHash != 0)
                isADS = worldAnimator.GetBool(_adsBoolHash);
        }


        float target = isADS ? 1f : 0f;
        _adsBlend = Mathf.MoveTowards(_adsBlend, target, adsOffsetLerpSpeed * Time.deltaTime);

        // Hips offsets
        Vector3 hipPos = def.thirdPersonPositionOffset;
        Quaternion hipRot = Quaternion.Euler(def.thirdPersonRotationOffsetEuler);
        Vector3 hipScale = def.thirdPersonScale;
        if (hipScale == Vector3.zero)
            hipScale = Vector3.one;

        // ADS offsets (nếu không dùng ADS riêng thì = hip)
        Vector3 adsPos = hipPos;
        Quaternion adsRot = hipRot;
        Vector3 adsScale = hipScale;

        if (def.useADSOffsetsForWorldModel)
        {
            adsPos = def.thirdPersonADSPositionOffset;
            adsRot = Quaternion.Euler(def.thirdPersonADSRotationOffsetEuler);

            if (def.thirdPersonADSScale != Vector3.zero)
                adsScale = def.thirdPersonADSScale;
        }

        // Lerp giữa hip & ADS
        inst.transform.localPosition = Vector3.Lerp(hipPos, adsPos, _adsBlend);
        inst.transform.localRotation = Quaternion.Slerp(hipRot, adsRot, _adsBlend);
        inst.transform.localScale = Vector3.Lerp(hipScale, adsScale, _adsBlend);
    }

    IEnumerator Co_BindAndPrime()
    {
        if (!_bound)
        {
            if (stateProvider != null)
                _loadout = stateProvider.Loadout;

            if (_loadout != null)
            {
                _loadout.OnSlotChanged += HandleSlotChanged;
                _loadout.OnActiveSlotChanged += HandleActiveSlotChanged;
                _bound = true;
            }

#if FUSION_WEAVER
            _loadoutNetBehaviour = _loadout as NetworkBehaviour;
#endif
        }

        if (_loadout == null)
            yield break;

#if FUSION_WEAVER
        // Nếu là networked loadout, chờ Spawned xong
        if (_loadoutNetBehaviour != null)
        {
            yield return new WaitUntil(() =>
                _loadoutNetBehaviour.Object != null &&
                _loadoutNetBehaviour.Object.IsValid);
        }
#endif

        // Cho thêm 1–2 frame để state kịp prime
        yield return null;
        yield return null;

        PrimeFromState();

        // Local: vẫn spawn để có muzzle + FX, nhưng ẩn mesh của súng 3rd
        if (hideWorldWeaponForLocal && IsLocalPlayer())
        {
            // Sau PrimeFromState() thì _instances đã có
            if (_instances != null)
            {
                for (int i = 0; i < _instances.Length; i++)
                {
                    if (_instances[i])
                        SetWorldWeaponMeshesVisible(_instances[i], false);
                }
            }
        }
    }

    void Unbind()
    {
        if (_bound && _loadout != null)
        {
            _loadout.OnSlotChanged -= HandleSlotChanged;
            _loadout.OnActiveSlotChanged -= HandleActiveSlotChanged;
        }

        _loadout = null;
        _bound = false;
    }

    void ClearInstances()
    {
        if (_instances != null)
        {
            for (int i = 0; i < _instances.Length; i++)
            {
                if (_instances[i])
                    Destroy(_instances[i]);
            }
        }

        _instances = null;
        _defs = null;
        _cachedSlotCount = -1;

        // NEW
        ClearWorldFx();
    }

    void EnsureArrays()
    {
        if (_loadout == null)
        {
            ClearInstances();
            return;
        }

        int slotCount = Mathf.Max(0, _loadout.SlotCount);
        if (slotCount == _cachedSlotCount && _instances != null && _defs != null)
            return;

        GameObject[] oldInst = _instances;
        WeaponDef[] oldDefs = _defs;
        int oldCount = oldInst != null ? oldInst.Length : 0;

        _instances = slotCount > 0 ? new GameObject[slotCount] : null;
        _defs = slotCount > 0 ? new WeaponDef[slotCount] : null;

        _worldFxRoots = slotCount > 0 ? new GameObject[slotCount] : null;
        _worldFxParticles = slotCount > 0 ? new ParticleSystem[slotCount][] : null;
        _worldFxAutoOff = slotCount > 0 ? new Coroutine[slotCount] : null;
        _worldFxLife = slotCount > 0 ? new float[slotCount] : null;
        _worldCasingEjectPoints = slotCount > 0 ? new Transform[slotCount] : null;

        if (oldInst != null && oldDefs != null)
        {
            int copy = Mathf.Min(oldCount, slotCount);
            for (int i = 0; i < copy; i++)
            {
                _instances[i] = oldInst[i];
                _defs[i] = oldDefs[i];
            }

            for (int i = slotCount; i < oldCount; i++)
            {
                if (oldInst[i])
                    Destroy(oldInst[i]);
            }
        }

        _cachedSlotCount = slotCount;
    }

    void PrimeFromState()
    {
        if (_loadout == null)
            return;

        EnsureArrays();

        for (int i = 0; i < _cachedSlotCount; i++)
        {
            HandleSlotChanged(i);
        }

        HandleActiveSlotChanged(_loadout.ActiveSlot);
    }

    void DestroyInstance(int slotIndex)
    {
        if (_instances == null)
            return;

        if (slotIndex < 0 || slotIndex >= _instances.Length)
            return;

        if (_instances[slotIndex])
            Destroy(_instances[slotIndex]);

        _instances[slotIndex] = null;
        if (_defs != null && slotIndex < _defs.Length)
            _defs[slotIndex] = null;

        if (_worldFxAutoOff != null && slotIndex < _worldFxAutoOff.Length && _worldFxAutoOff[slotIndex] != null)
            StopCoroutine(_worldFxAutoOff[slotIndex]);

        if (_worldFxRoots != null && slotIndex < _worldFxRoots.Length && _worldFxRoots[slotIndex] != null)
            Destroy(_worldFxRoots[slotIndex]);

        if (_worldFxRoots != null) _worldFxRoots[slotIndex] = null;
        if (_worldFxParticles != null) _worldFxParticles[slotIndex] = null;
        if (_worldFxLife != null) _worldFxLife[slotIndex] = 0f;
        if (_worldFxAutoOff != null) _worldFxAutoOff[slotIndex] = null;
        if (_worldCasingEjectPoints != null && slotIndex < _worldCasingEjectPoints.Length)
            _worldCasingEjectPoints[slotIndex] = null;
    }

    void EnsureWorldFx(int slotIndex, Transform muzzle, GameObject fxPrefab)
    {
        if (_worldFxRoots == null || slotIndex < 0 || slotIndex >= _worldFxRoots.Length)
            return;
        if (_worldFxRoots[slotIndex] != null)
            return; // đã tạo rồi

        if (muzzle == null || fxPrefab == null)
            return;

        var fxRoot = Instantiate(fxPrefab, muzzle.position, muzzle.rotation, muzzle);
        fxRoot.SetActive(false);

        // Layer theo muzzle (đảm bảo main cam render được)
        int layer = muzzle.gameObject.layer;
        foreach (var t in fxRoot.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;

        var particles = fxRoot.GetComponentsInChildren<ParticleSystem>(true);
        _worldFxParticles[slotIndex] = particles;

        float life = 0.06f;
        foreach (var ps in particles)
        {
            var main = ps.main;
            float approx = main.duration + main.startLifetimeMultiplier;
            if (approx > life) life = approx;
        }
        _worldFxLife[slotIndex] = life;

        _worldFxRoots[slotIndex] = fxRoot;
    }

    IEnumerator Co_AutoOffWorldFx(int slotIndex)
    {
        if (_worldFxLife == null || slotIndex < 0 || slotIndex >= _worldFxLife.Length)
            yield break;

        float life = Mathf.Max(0.01f, _worldFxLife[slotIndex]);
        yield return new WaitForSeconds(life);

        if (_worldFxRoots != null && slotIndex < _worldFxRoots.Length && _worldFxRoots[slotIndex])
            _worldFxRoots[slotIndex].SetActive(false);

        if (_worldFxAutoOff != null)
            _worldFxAutoOff[slotIndex] = null;
    }


    void HandleSlotChanged(int slotIndex)
    {
        if (_loadout == null)
            return;

        EnsureArrays();

        if (slotIndex < 0 || slotIndex >= _cachedSlotCount)
            return;

        var state = _loadout.GetSlot(slotIndex);
        if (state.IsEmpty)
        {
            DestroyInstance(slotIndex);
            UpdateHasWeaponFlag();
            return;
        }

        var def = WeaponIdRegistry.GetDef(state.weaponKey);
        if (!def)
        {
            DestroyInstance(slotIndex);
            UpdateHasWeaponFlag();
            return;
        }

        if (_defs[slotIndex] == def && _instances != null && _instances[slotIndex] != null)
        {
            if (_loadout.ActiveSlot != slotIndex)
                _instances[slotIndex].SetActive(false);

            UpdateHasWeaponFlag();
            return;
        }

        DestroyInstance(slotIndex);

        if (!def.thirdPersonPrefab || !activeWeaponRoot)
        {
            Debug.LogWarning($"[PlayerWeaponWorldView] WeaponDef {def.weaponId} chưa được set thirdPersonPrefab.");
            _defs[slotIndex] = def;
            UpdateHasWeaponFlag();
            return;
        }

        var inst = Instantiate(def.thirdPersonPrefab, activeWeaponRoot);

        // Áp offset từ WeaponDef
        inst.transform.localPosition = def.thirdPersonPositionOffset;
        inst.transform.localRotation = Quaternion.Euler(def.thirdPersonRotationOffsetEuler);

        // Nếu thirdPersonScale = (0,0,0) (do chưa set), fallback về (1,1,1)
        var scale = def.thirdPersonScale;
        if (scale == Vector3.zero)
            scale = Vector3.one;

        inst.transform.localScale = scale;


        _instances[slotIndex] = inst;
        _defs[slotIndex] = def;

        if (_loadout.ActiveSlot != slotIndex)
            inst.SetActive(false);

        if (hideWorldWeaponForLocal && IsLocalPlayer())
            SetWorldWeaponMeshesVisible(inst, false);

        UpdateHasWeaponFlag();

    }

    void HandleActiveSlotChanged(int newActive)
    {
        if (_instances == null || _instances.Length == 0)
            return;

        for (int i = 0; i < _instances.Length; i++)
        {
            var inst = _instances[i];
            if (!inst) continue;

            bool shouldBeActive = (i == newActive && newActive >= 0);
            if (inst.activeSelf != shouldBeActive)
                inst.SetActive(shouldBeActive);

            // Sau này nếu muốn holster: ở đây có thể re-parent theo i/WeaponType
        }

        UpdateHasWeaponFlag();

        _currentActiveSlot = newActive;
        // Khi đổi weapon, về mặc định coi như đang ở hip
        _adsBlend = 0f;
    }

    void UpdateHasWeaponFlag()
    {
        if (networkAnimator == null)
            return;

        bool hasWeapon = false;

        if (_loadout != null && _defs != null && _defs.Length > 0)
        {
            int active = _loadout.ActiveSlot;
            if (active >= 0 && active < _defs.Length)
                hasWeapon = _defs[active] != null;
        }

#if FUSION_WEAVER
        // Nếu là Network và player này không có StateAuthority thì không đụng vào network flag
        if (_loadoutNetBehaviour != null)
        {
            var obj = _loadoutNetBehaviour.Object;
            if (obj != null && obj.IsValid && !obj.HasStateAuthority)
                return;
        }
#endif

        networkAnimator.SetHasWeapon(hasWeapon);
    }

    public void PlayWorldMuzzleForWeaponKey(int weaponKey)
    {
        if (hideWorldWeaponForLocal && IsLocalPlayer())
            return; // local: tắt hẳn world muzzle VFX

        if (weaponKey == 0) return;

        if (stateProvider == null)
            stateProvider = GetComponentInParent<PlayerStateProvider>(true);

        var loadout = stateProvider != null ? stateProvider.Loadout : null;
        if (loadout == null) return;

        int slotIndex = -1;
        for (int i = 0; i < loadout.SlotCount; i++)
        {
            var slot = loadout.GetSlot(i);
            if (slot.weaponKey == weaponKey)
            {
                slotIndex = i;
                break;
            }
        }
        if (slotIndex < 0) return;

        var def = WeaponIdRegistry.GetDef(weaponKey);
        if (def == null) return;

        var fxPrefab = def.worldMuzzleFlashPrefab != null
            ? def.worldMuzzleFlashPrefab
            : def.muzzleFlashPrefab;

        if (fxPrefab == null) return;

        PlayWorldMuzzleInternal(slotIndex, fxPrefab, def);
    }


    // slotIndex đã tìm từ weaponKey, fxPrefab = def.worldMuzzleFlashPrefab ?? def.muzzleFlashPrefab
    void PlayWorldMuzzleInternal(int slotIndex, GameObject fxPrefab, WeaponDef def)
    {
        if (_instances == null)
            return;
        if (slotIndex < 0 || slotIndex >= _instances.Length)
            return;

        var inst = _instances[slotIndex];
        if (!inst)
            return;

        var view = inst.GetComponentInChildren<WeaponView>(true);
        if (view == null || view.muzzle == null)
            return;

        var muzzle = view.muzzle;

        // Tạo FX root nếu chưa có
        EnsureWorldFx(slotIndex, muzzle, fxPrefab);

        if (_worldFxRoots == null || slotIndex >= _worldFxRoots.Length)
            return;

        var fxRoot = _worldFxRoots[slotIndex];
        if (!fxRoot)
            return;

        // Dừng coroutine cũ nếu đang chạy
        if (_worldFxAutoOff != null && _worldFxAutoOff[slotIndex] != null)
        {
            StopCoroutine(_worldFxAutoOff[slotIndex]);
            _worldFxAutoOff[slotIndex] = null;
        }

        // Bật FX
        // Bật FX
        fxRoot.SetActive(true);

        bool localLightOnly = hideWorldWeaponForLocal && IsLocalPlayer();

        if (localLightOnly)
        {
            // Local: tắt render để không thấy muzzle VFX, nhưng vẫn cho particle chạy để lights module hoạt động
            SetWorldMuzzleFxLightOnly(fxRoot);
        }

        // vẫn play particle để light chạy
        foreach (var ps in _worldFxParticles[slotIndex])
        {
            if (!ps) continue;
            ps.Clear(true);
            ps.Play(true);
        }


        TryEjectCasingWorld(slotIndex, inst, muzzle, def);


        // Hẹn giờ tắt, nhưng KHÔNG Destroy
        if (_worldFxLife != null && _worldFxLife[slotIndex] > 0f)
            _worldFxAutoOff[slotIndex] = StartCoroutine(Co_AutoOffWorldFx(slotIndex));
    }


    void ClearWorldFx()
    {
        if (_worldFxRoots != null)
        {
            for (int i = 0; i < _worldFxRoots.Length; i++)
            {
                if (_worldFxRoots[i])
                    Destroy(_worldFxRoots[i]);
            }
        }

        _worldFxRoots = null;
        _worldFxParticles = null;
        _worldFxAutoOff = null;
        _worldFxLife = null;
    }

    /// <summary>
    /// Được PlayerAppearance gọi để cập nhật socket theo skin hiện tại.
    /// Truyền null nếu skin không có socket đó.
    /// </summary>
    public void SetSockets(Transform activeRoot, Transform primaryHolster, Transform secondaryHolster)
    {
        // Chỉ update nếu có transform hợp lệ, tránh ghi đè bằng null
        if (activeRoot != null)
            activeWeaponRoot = activeRoot;

        if (primaryHolster != null)
            primaryHolsterRoot = primaryHolster;

        if (secondaryHolster != null)
            secondaryHolsterRoot = secondaryHolster;

        // Re-parent lại các instance hiện có sang activeWeaponRoot mới
        if (activeWeaponRoot != null && _instances != null && _defs != null)
        {
            for (int i = 0; i < _instances.Length; i++)
            {
                var inst = _instances[i];
                var def = (i < _defs.Length) ? _defs[i] : null;
                if (!inst || def == null)
                    continue;

                // Set lại parent về socket tay
                inst.transform.SetParent(activeWeaponRoot, false);

                // Áp lại offset giống như khi vừa spawn
                Vector3 pos = def.thirdPersonPositionOffset;
                Quaternion rot = Quaternion.Euler(def.thirdPersonRotationOffsetEuler);
                Vector3 scale = def.thirdPersonScale;
                if (scale == Vector3.zero)
                    scale = Vector3.one;

                inst.transform.localPosition = pos;
                inst.transform.localRotation = rot;
                inst.transform.localScale = scale;
            }
        }
    }

    /// <summary>
    /// Cho phép PlayerAppearance cập nhật Animator world model khi skin thay đổi.
    /// </summary>
    public void SetWorldAnimator(Animator animator)
    {
        worldAnimator = animator;

        if (!string.IsNullOrEmpty(adsBoolParam) && worldAnimator != null)
            _adsBoolHash = Animator.StringToHash(adsBoolParam);
        else
            _adsBoolHash = 0;
    }

    Transform ResolveWorldCasingEjectPoint(GameObject inst, WeaponDef def, Transform fallbackMuzzle)
    {
        if (inst == null) return fallbackMuzzle;
        if (def == null) return fallbackMuzzle;

        var candidates = def.casingEjectNameCandidates;
        if (candidates != null && candidates.Length > 0)
        {
            var all = inst.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                foreach (var n in candidates)
                {
                    if (!string.IsNullOrEmpty(n) && t.name == n)
                        return t;
                }
            }
        }

        return fallbackMuzzle;
    }

    void TryEjectCasingWorld(int slotIndex, GameObject inst, Transform muzzle, WeaponDef def)
    {
        if (hideWorldWeaponForLocal && IsLocalPlayer())
            return;

        if (def == null) return;
        if (def.casingPrefab == null) return;

        if (_worldCasingEjectPoints == null || slotIndex < 0 || slotIndex >= _worldCasingEjectPoints.Length)
            return;

        var p = _worldCasingEjectPoints[slotIndex];
        if (p == null)
        {
            p = ResolveWorldCasingEjectPoint(inst, def, muzzle);
            _worldCasingEjectPoints[slotIndex] = p;
        }

        if (p == null) return;

        var pool = CasingPool.Instance;
        PooledCasing pc = null;

        int layer = LayerMask.NameToLayer("Casing");
        if (layer < 0) layer = 0;


        if (pool != null)
        {
            pc = pool.Rent(def.casingPrefab, p.position, p.rotation, layer);
        }
        else
        {
            // fallback
            var casingGO = Instantiate(def.casingPrefab, p.position, p.rotation);
            foreach (var t in casingGO.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;

            var rb0 = casingGO.GetComponent<Rigidbody>();
            if (rb0 != null)
            {
                float f0 = Random.Range(def.casingEjectForce.x, def.casingEjectForce.y);
                Vector3 dir0 = (p.right + p.up * 0.15f).normalized;
                rb0.AddForce(dir0 * f0, ForceMode.Impulse);

                float tq0 = Random.Range(def.casingTorque.x, def.casingTorque.y);
                rb0.AddTorque(Random.onUnitSphere * tq0, ForceMode.Impulse);
            }

            Destroy(casingGO, Mathf.Max(0.25f, def.casingLifetime));
            return;
        }

        // pooling path
        var rb = pc.GetComponent<Rigidbody>();
        if (rb != null)
        {
            float f = Random.Range(def.casingEjectForce.x, def.casingEjectForce.y);
            Vector3 dir = (p.right + p.up * 0.15f).normalized;
            rb.AddForce(dir * f, ForceMode.Impulse);

            float tq = Random.Range(def.casingTorque.x, def.casingTorque.y);
            rb.AddTorque(Random.onUnitSphere * tq, ForceMode.Impulse);
        }

        // ❌ world: không init impact SFX
        pc.ScheduleReturn(Mathf.Max(0.25f, def.casingLifetime));
    }

    bool IsLocalPlayer()
    {
        // SP: luôn là local
        if (GameSession.Mode == AppPlayMode.Single) return true;

#if FUSION_WEAVER
        // MP: local = có InputAuthority
        var no = GetComponentInParent<NetworkObject>(true);
        if (no != null && no.Runner != null && no.IsValid)
            return no.HasInputAuthority;
#endif

        return false;
    }

    void SetWorldWeaponMeshesVisible(GameObject inst, bool visible)
    {
        if (!inst) return;

        // Chỉ tắt renderer của MESH, không đụng ParticleSystemRenderer/TrailRenderer...
        foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (!r) continue;

            // Tắt mesh của súng 3rd thôi
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
                r.enabled = visible;
        }
    }

    private void SetWorldMuzzleFxLightOnly(GameObject fxRoot)
    {
        if (fxRoot == null) return;

        // 1) Ẩn phần render của particle, nhưng KHÔNG stop particle
        var psRenderers = fxRoot.GetComponentsInChildren<ParticleSystemRenderer>(true);
        foreach (var r in psRenderers)
        {
            if (!r) continue;
            r.enabled = false;
        }

        // Nếu effect có Trail/Line renderer thì tắt luôn
        var otherRenderers = fxRoot.GetComponentsInChildren<Renderer>(true);
        foreach (var r in otherRenderers)
        {
            if (!r) continue;
            if (r is ParticleSystemRenderer) continue;
            r.enabled = false;
        }

        // 2) Bật Light object nếu nó bị tắt sẵn (không bắt buộc nhưng an toàn)
        var lights = fxRoot.GetComponentsInChildren<Light>(true);
        foreach (var l in lights)
        {
            if (!l) continue;
            l.enabled = true;
        }
    }

}
