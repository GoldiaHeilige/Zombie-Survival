using UnityEngine;
using System;

public class WeaponViewController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Where the weapon view prefab will be parented under (1P rig root).")]
    [SerializeField] private Transform weaponViewRoot;

    [Tooltip("Optional GunFX component to drive muzzle flash & light. If null, will auto-search under root or spawned view.")]
    [SerializeField] private GunFX gunFX;

    private Transform _casingEjectPoint;

    [Header("Options")]
    [Tooltip("Try to match spawned view's layer to the root's layer (recursively). Useful for first-person rendering layers.")]
    [SerializeField] private bool inheritLayerFromRoot = true;

    [Tooltip("Candidate transform names to look for when WeaponView.muzzle is not present.")]
    [SerializeField] private string[] muzzleNameCandidates = new[] { "Muzzle", "MuzzleFX", "MuzzleFlash" };

    // --- Reload visual ---
    [SerializeField] private float reloadDropDistance = 0.25f;   // súng tụt xuống bao nhiêu (Y-)

    [Tooltip("Tỉ lệ thời gian reload dành cho pha hạ súng (0–0.5).")]
    [SerializeField, Range(0.01f, 0.5f)]
    private float reloadEnterFraction = 0.15f;

    [Tooltip("Tỉ lệ thời gian reload dành cho pha nâng súng lên lại (0–0.5).")]
    [SerializeField, Range(0.01f, 0.5f)]
    private float reloadExitFraction = 0.20f;

    [Tooltip("Nếu bật, súng sẽ teleport xuống dưới trong suốt thời gian reload, không lerp.")]
    [SerializeField]
    private bool instantReloadDrop = false;

    private float _reloadProgress;  // t chuẩn hoá 0–1 từ WeaponController

    [Header("ADS (View)")]
    [Tooltip("Trọng số ADS (0=Hip, 1=ADS). Đọc-only đối với module khác.")]
    [Range(0f, 1f)] public float ADSWeight { get; private set; } = 0f;

    private WeaponDef _def;
    private Vector3 _baseRootLocalPos;
    private bool _basePosCached;

    public GameObject CurrentInstance { get; private set; }
    public Transform Muzzle { get; private set; }
    public void SetADSDesired(bool on) { _adsDesired = on; }

    public void SetReloadProgress(float t)
    {
        // Không SmoothDamp nữa, dùng t trực tiếp cho reload curve
        _reloadProgress = Mathf.Clamp01(t);
    }

    public void ResetReloadVisualImmediate()
    {
        _reloadProgress = 0f;

        if (weaponViewRoot != null && _basePosCached)
        {
            weaponViewRoot.localPosition = _baseRootLocalPos;
        }
    }

    public event Action<Transform> OnViewReady;

    bool _adsDesired;
    float _adsVel;

    private FusionNetBridge _net;
    private bool IsLocalOwner =>
    _net == null ||
    _net.Object == null ||
    !_net.Object.IsValid ||
    _net.IsLocalOwner;

    void Awake() 
    {
        _net = GetComponentInParent<FusionNetBridge>(true);
    }

    public void Apply(WeaponDef def, Transform overrideRoot = null)
    {
        if (!IsLocalOwner)
        {
            return;
        }

        _def = def;

        var root = overrideRoot != null ? overrideRoot : weaponViewRoot;
        Clear(root);

        if (def == null || def.viewPrefab == null || root == null) return;

        CurrentInstance = Instantiate(def.viewPrefab, root, false);

        if (inheritLayerFromRoot && root != null)
            SetLayerRecursively(CurrentInstance, root.gameObject.layer);

        Muzzle = ResolveMuzzle(CurrentInstance);
        _casingEjectPoint = ResolveCasingEjectPoint(CurrentInstance, def);

        var fx = gunFX ? gunFX : (root != null ? root.GetComponentInChildren<GunFX>(true) : null);
        if (fx == null && CurrentInstance != null)
            fx = CurrentInstance.GetComponentInChildren<GunFX>(true);

        if (fx != null)
        {
            gunFX = fx;
            fx.Configure(Muzzle, def.muzzleFlashPrefab);
        }

     //   Debug.Log($"[APPLY] {Time.frameCount} local={IsLocalOwner} APPLY weapon {def?.name}");

        OnViewReady?.Invoke(Muzzle);
    }

    public void Clear(Transform overrideRoot = null)
    {
        if (!IsLocalOwner) return;

        if (CurrentInstance != null)
        {
            CurrentInstance.SetActive(false);
            Destroy(CurrentInstance);
            CurrentInstance = null;
        }

        CurrentInstance = null;
        Muzzle = null;

        // NEW: reset reload offset về base pos khi clear view
        ResetReloadVisualImmediate();
    }

    public void PlayMuzzle()
    {
        if (gunFX != null)
            gunFX.PlayMuzzle();

        TryEjectCasingFP();
    }

    private Transform ResolveMuzzle(GameObject viewInstance)
    {
        if (viewInstance == null) return null;

        var view = viewInstance.GetComponentInChildren<WeaponView>(true);
        if (view != null && view.muzzle != null)
            return view.muzzle;

        foreach (var t in viewInstance.GetComponentsInChildren<Transform>(true))
        {
            foreach (var name in muzzleNameCandidates)
            {
                if (t.name.Equals(name, StringComparison.Ordinal))
                    return t;
            }
        }

        return null;
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null) return;
        go.layer = layer;
        var trs = go.GetComponentsInChildren<Transform>(true);
        foreach (var t in trs) t.gameObject.layer = layer;
    }

    void LateUpdate()
    {
        // 1) Tween ADSWeight về trạng thái mong muốn (mượt bằng SmoothDamp)
        float target = _adsDesired ? 1f : 0f;
        float inT = _def != null ? _def.adsInTime : 0.10f;
        float outT = _def != null ? _def.adsOutTime : 0.12f;
        float smoothT = Mathf.Max(0.0001f, _adsDesired ? inT : outT);
        ADSWeight = Mathf.SmoothDamp(ADSWeight, target, ref _adsVel, smoothT);

        // 2) Áp pose ADS (nếu bạn đã có Transform ADSAlign)
        // ví dụ:
        // adsAlign.localPosition = Vector3.Lerp(hipPos, adsPos, ADSWeight);
        // adsAlign.localRotation = Quaternion.Slerp(hipRot, adsRot, ADSWeight);

        // 3) (không bắt buộc) có thể phát event cho UI/scope nếu cần

        if (weaponViewRoot != null)
        {
            if (!_basePosCached && _reloadProgress <= 0f)
            {
                _baseRootLocalPos = weaponViewRoot.localPosition;
                _basePosCached = true;
            }

            float t = _reloadProgress; // 0–1 từ WeaponController
            float weight = 0f;         // 0 = hip, 1 = hạ tối đa

            if (t > 0f && t < 1f)
            {
                if (instantReloadDrop)
                {
                    // OPTION 2: Teleport – súng luôn nằm dưới trong suốt thời gian reload
                    weight = 1f;
                }
                else
                {
                    // OPTION 1: 3 phase – xuống nhanh, giữ dưới, lên nhanh
                    float enterT = reloadEnterFraction;
                    float exitT = reloadExitFraction;

                    // Đảm bảo tổng không vượt 1
                    float sum = enterT + exitT;
                    if (sum > 1f)
                    {
                        float s = 1f / sum;
                        enterT *= s;
                        exitT *= s;
                    }

                    if (t <= enterT)
                    {
                        // Pha hạ súng: 0 → enterT
                        float nt = t / Mathf.Max(enterT, 0.0001f);
                        weight = Mathf.SmoothStep(0f, 1f, nt); // ease-out nhẹ
                    }
                    else if (t >= 1f - exitT)
                    {
                        // Pha nâng lên: (1-exitT) → 1
                        float nt = (t - (1f - exitT)) / Mathf.Max(exitT, 0.0001f);
                        weight = Mathf.SmoothStep(1f, 0f, nt); // ease-in nhẹ
                    }
                    else
                    {
                        // Pha giữ dưới: giữa reload
                        weight = 1f;
                    }
                }
            }

            float yOffset = -reloadDropDistance * weight;

            Vector3 pos = _baseRootLocalPos;
            pos.y += yOffset;
            weaponViewRoot.localPosition = pos;
        }

    }

    public void SetADSWeight(float w)
    {
        ADSWeight = Mathf.Clamp01(w);
    }

    public void ResetADS()
    {
        ADSWeight = 0f;
    }

    // (an toàn hơn)
    void OnDisable()
    {
        ADSWeight = 0f;
        ResetReloadVisualImmediate();
    }

    public void RecacheBaseRootLocalPos()
    {
        if (weaponViewRoot == null) return;
        _baseRootLocalPos = weaponViewRoot.localPosition;
        _basePosCached = true;
    }

    private Transform ResolveCasingEjectPoint(GameObject viewInstance, WeaponDef def)
    {
        if (viewInstance == null || def == null) return null;

        var names = def.casingEjectNameCandidates;
        if (names == null || names.Length == 0) return null;

        foreach (var t in viewInstance.GetComponentsInChildren<Transform>(true))
        {
            foreach (var n in names)
            {
                if (!string.IsNullOrEmpty(n) && t.name == n)
                    return t;
            }
        }
        return null;
    }

    void TryEjectCasingFP()
    {
        if (!IsLocalOwner) return;
        if (_def == null) return;
        if (_def.casingPrefab == null) return;

        // Không có eject point thì fallback bằng muzzle (hoặc root)
        Transform p = _casingEjectPoint != null ? _casingEjectPoint : (Muzzle != null ? Muzzle : weaponViewRoot);
        if (p == null) return;
        var pool = CasingPool.Instance;
        PooledCasing pc = null;

        int fpLayer = LayerMask.NameToLayer("Casing");
        if (fpLayer < 0) fpLayer = 0;

        if (pool != null)
        {
            pc = pool.Rent(_def.casingPrefab, p.position, p.rotation, fpLayer);
        }
        else
        {
            // fallback nếu quên đặt pool trong scene
            var casingGO = Instantiate(_def.casingPrefab, p.position, p.rotation);
            foreach (var t in casingGO.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = fpLayer;

            // physics (giữ y như cũ)
            var rb0 = casingGO.GetComponent<Rigidbody>();
            if (rb0 != null)
            {
                float f0 = UnityEngine.Random.Range(_def.casingEjectForce.x, _def.casingEjectForce.y);
                Vector3 dir0 = (p.right + p.up * 0.25f).normalized;
                rb0.AddForce(dir0 * f0, ForceMode.Impulse);

                float tq0 = UnityEngine.Random.Range(_def.casingTorque.x, _def.casingTorque.y);
                rb0.AddTorque(UnityEngine.Random.onUnitSphere * tq0, ForceMode.Impulse);
            }

            if (_def.casingImpactAudio != null)
            {
                var impact0 = casingGO.GetComponent<CasingImpactSfx>();
                if (impact0 == null) impact0 = casingGO.AddComponent<CasingImpactSfx>();
                impact0.Init(_def.casingImpactAudio);
            }

            Destroy(casingGO, Mathf.Max(0.25f, _def.casingLifetime));
            return;
        }

        // ✅ từ đây trở xuống là path pooling
        var rb = pc.GetComponent<Rigidbody>();
        if (rb != null)
        {
            float f = UnityEngine.Random.Range(_def.casingEjectForce.x, _def.casingEjectForce.y);
            Vector3 dir = (p.right + p.up * 0.25f).normalized;
            rb.AddForce(dir * f, ForceMode.Impulse);

            float tq = UnityEngine.Random.Range(_def.casingTorque.x, _def.casingTorque.y);
            rb.AddTorque(UnityEngine.Random.onUnitSphere * tq, ForceMode.Impulse);
        }

        // FP impact sound (1 lần)
        if (_def.casingImpactAudio != null)
        {
            var impact = pc.GetComponent<CasingImpactSfx>();
            if (impact == null) impact = pc.gameObject.AddComponent<CasingImpactSfx>();
            impact.Init(_def.casingImpactAudio);
        }

        pc.ScheduleReturn(Mathf.Max(0.25f, _def.casingLifetime));

    }

}
