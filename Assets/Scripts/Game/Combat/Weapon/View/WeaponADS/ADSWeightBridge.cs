using UnityEngine;
using System.Reflection;

/// <summary>
/// Cầu nối lấy ADS weight (0..1) từ một component khác (VD: WeaponViewController)
/// mà KHÔNG cần sửa code của nó. Nếu không tìm thấy, dùng giá trị manual.
/// </summary>
[DisallowMultipleComponent]
public class ADSWeightBridge : MonoBehaviour
{
    [Header("Targets")]
    public WeaponSwayBob sway;                // Kéo WeaponSwayBob vào đây

    [Header("Provider (tuỳ chọn)")]
    [Tooltip("Component có property/method float ADSWeight (0..1). Ví dụ: WeaponViewController.")]
    public MonoBehaviour provider;            // để trống thì dùng manual

    [Tooltip("Tên property hoặc method (trả float) để lấy ADS weight. Mặc định: ADSWeight")]
    public string adsMemberName = "ADSWeight";

    [Header("Manual fallback")]
    [Range(0f, 1f)] public float manualADSWeight = 0f;

    [Header("Animator (optional)")]
    [Tooltip("Animator của world model player (để set IsADS). Nếu trống sẽ tự tìm ở parent.")]
    public Animator worldAnimator;

    [Tooltip("Tên tham số bool điều khiển ADS trong Animator.")]
    public string isADSParam = "IsADS";

    [Tooltip("Ngưỡng bật ADS (0..1). Ví dụ 0.6 nghĩa là ADS khi ADSWeight > 0.6.")]
    [Range(0f, 1f)] public float adsOnThreshold = 0.6f;

    [Tooltip("Ngưỡng tắt ADS (0..1). Nên nhỏ hơn adsOnThreshold để tránh nháy.")]
    [Range(0f, 1f)] public float adsOffThreshold = 0.4f;

    private int _isADSHash;
    private bool _isADS;


    object _cachedObj;
    MemberInfo _cachedMember;
    bool _triedCache;

    void Awake()
    {
        if (!worldAnimator)
            worldAnimator = GetComponentInParent<Animator>();

        if (!string.IsNullOrEmpty(isADSParam))
            _isADSHash = Animator.StringToHash(isADSParam);
    }


    void LateUpdate()
    {
        if (!sway) return;

        float w = manualADSWeight;

        // Nếu có provider, thử lấy ADSWeight qua reflection an toàn
        if (provider)
        {
            if (!_triedCache)
            {
                _cachedObj = provider;
                var t = provider.GetType();
                // Ưu tiên property
                var prop = t.GetProperty(adsMemberName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.PropertyType == typeof(float))
                {
                    _cachedMember = prop;
                }
                else
                {
                    // Thử method không tham số trả float
                    var m = t.GetMethod(adsMemberName, BindingFlags.Public | BindingFlags.Instance, null, System.Type.EmptyTypes, null);
                    if (m != null && m.ReturnType == typeof(float)) _cachedMember = m;
                }
                _triedCache = true;
            }

            if (_cachedMember != null && _cachedObj != null)
            {
                try
                {
                    if (_cachedMember is PropertyInfo pi)
                        w = (float)pi.GetValue(_cachedObj);
                    else if (_cachedMember is MethodInfo mi)
                        w = (float)mi.Invoke(_cachedObj, null);
                }
                catch { /* dùng manual nếu lỗi */ }
            }
        }

        // Clamp và feed cho FP sway
        w = Mathf.Clamp01(w);
        sway.SetADSWeight(w);

        // ---- Feed sang Animator (IsADS) ----
        if (worldAnimator && _isADSHash != 0)
        {
            // Hysteresis nhỏ để tránh nháy khi w lắc quanh ngưỡng
            bool targetADS;
            if (_isADS)
            {
                // Đang ADS -> chỉ tắt khi w tụt xuống dưới ngưỡng off
                targetADS = w > adsOffThreshold;
            }
            else
            {
                // Đang hip -> chỉ bật khi w vượt ngưỡng on
                targetADS = w > adsOnThreshold;
            }

            if (targetADS != _isADS)
            {
                _isADS = targetADS;
                worldAnimator.SetBool(_isADSHash, _isADS);
            }
        }
    }

    public void SetWorldAnimator(Animator newAnimator)
    {
        worldAnimator = newAnimator;

        if (!string.IsNullOrEmpty(isADSParam) && worldAnimator != null)
        {
            _isADSHash = Animator.StringToHash(isADSParam);
            // Đọc trạng thái hiện tại của Animator để sync _isADS
            _isADS = worldAnimator.GetBool(_isADSHash);
        }
        else
        {
            _isADSHash = 0;
            _isADS = false;
        }
    }
}
