using UnityEngine;
#if FUSION_WEAVER
using Fusion;
#endif

/// <summary>
/// Bật/tắt QuickOutline khi local player đi vào/ra trigger range.
/// Dùng cho shop/door/zone unlock - kiểu "đi vào tầm là hiện", không cần nhìn.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class ProximityOutlineTrigger : MonoBehaviour
{
    [Header("Outline Targets")]
    [Tooltip("Nếu để trống, script sẽ tự tìm Outline trong parent/root (includeChildren).")]
    [SerializeField] private Outline[] outlines;

    [Tooltip("Nếu outlines để trống, sẽ auto-find Outline từ transform này trở lên.")]
    [SerializeField] private bool includeChildren = true;

    [Header("Range Auto-Setup (optional)")]
    [Tooltip("Nếu true và có SphereCollider trigger, sẽ set radius = interactRange từ Spot (WeaponShopSpot/PaP/Box).")]
    [SerializeField] private bool autoSetSphereRadiusFromSpot = true;

    [Tooltip("Ưu tiên lấy interactRange từ component Spot này. Nếu để trống, sẽ tự tìm trong parent.")]
    [SerializeField] private Component spotOverride;

    // local player overlap counter (chống multiple colliders)
    private int _localOverlapCount;

    private Collider _trigger;

    void Awake()
    {
        _trigger = GetComponent<Collider>();
        if (_trigger) _trigger.isTrigger = true;

        // Auto grab outlines if not assigned
        if (outlines == null || outlines.Length == 0)
        {
            outlines = includeChildren
                ? GetComponentsInParent<Outline>(true)
                : GetComponentsInParent<Outline>(false);

            // Nếu bạn gắn script lên child trigger, có thể nó sẽ tìm Outline cả trên trigger object.
            // Không sao, thường trigger object không có Outline.
        }

        // Default: tắt hết outline lúc start
        SetAll(false);

        // Auto radius from Spot interactRange (SphereCollider only)
        if (autoSetSphereRadiusFromSpot)
            TryAutoSetSphereRadius();
    }

    void OnEnable()
    {
        // đảm bảo không bị “kẹt outline” khi enable/disable object
        _localOverlapCount = 0;
        SetAll(false);
    }

    void OnDisable()
    {
        _localOverlapCount = 0;
        SetAll(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[ProximityOutlineTrigger] Enter: {other.name}  root:{other.transform.root.name}", other);

        var pickup = other.GetComponentInParent<PlayerPickup>();
        Debug.Log($"  Has PlayerPickup in parent? {(pickup != null)}", other);

        if (!IsLocalPlayerCollider(other)) return;

        _localOverlapCount++;
        if (_localOverlapCount == 1) SetAll(true);
    }


    private void OnTriggerExit(Collider other)
    {
        if (!IsLocalPlayerCollider(other)) return;

        _localOverlapCount = Mathf.Max(0, _localOverlapCount - 1);
        if (_localOverlapCount == 0)
            SetAll(false);
    }

    private void SetAll(bool on)
    {
        if (outlines == null) return;
        for (int i = 0; i < outlines.Length; i++)
        {
            if (outlines[i])
                outlines[i].enabled = on;
        }
    }

    private bool IsLocalPlayerCollider(Collider other)
    {
        var root = other.transform.root;

        // PlayerPickup nằm ở con -> phải dùng GetComponentInChildren
        var pickup = root.GetComponentInChildren<PlayerPickup>(true);
        if (!pickup) return false;

#if FUSION_WEAVER
        var net = root.GetComponentInChildren<FusionNetBridge>(true);
        if (net != null && net.Object != null && !net.Object.HasInputAuthority)
            return false;
#endif

        return true;
    }


    private void TryAutoSetSphereRadius()
    {
        var sc = _trigger as SphereCollider;
        if (!sc) return;

        float range = -1f;

        // 1) lấy từ override nếu có
        if (spotOverride != null)
        {
            range = ReadInteractRangeFromSpot(spotOverride);
        }

        // 2) nếu chưa có, tự tìm trong parent
        if (range <= 0f)
        {
            // ưu tiên: WeaponShopSpot / PackAPunchSpot / RandomWeaponBoxSpot
            var shop = GetComponentInParent<WeaponShopSpot>(true);
            if (shop) range = shop.interactRange;

            if (range <= 0f)
            {
                var pap = GetComponentInParent<PackAPunchSpot>(true);
                if (pap) range = pap.interactRange;
            }

            if (range <= 0f)
            {
                var box = GetComponentInParent<RandomWeaponBoxSpot>(true);
                if (box) range = box.interactRange;
            }
        }

        if (range > 0f)
            sc.radius = range;
    }

    private float ReadInteractRangeFromSpot(Component spot)
    {
        // đọc interactRange từ 3 loại spot bạn đang có
        if (spot is WeaponShopSpot s1) return s1.interactRange;
        if (spot is PackAPunchSpot s2) return s2.interactRange;
        if (spot is RandomWeaponBoxSpot s3) return s3.interactRange;
        return -1f;
    }
}
