using UnityEngine;

/// <summary>
/// Điểm mua súng/mua đạn kiểu CoD (wall weapon).
/// Gắn lên object treo súng trên tường.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class WeaponShopSpot : MonoBehaviour
{
    [Header("Weapon")]
    public WeaponDef weaponDef;

    [Tooltip("Giá mua súng lần đầu (khi chưa có khẩu này).")]
    public int weaponCost = 1000;

    [Tooltip("Giá mua đạn, refill full reserve khi đã có khẩu này.")]
    public int ammoCost = 500;

    [Header("Usage")]
    [Tooltip("Nếu false, shop chỉ dùng được một số lần rồi tắt.")]
    public bool infiniteUse = true;

    [Min(1)]
    public int maxUses = 1;

    [Tooltip("Khoảng cách tối đa để cho phép tương tác.")]
    public float interactRange = 2.0f;

    [Header("UI / Hint (optional)")]
    [Tooltip("Nếu để trống sẽ dùng weaponDef.weaponName.")]
    public string overrideDisplayName;

    [Tooltip("World-space anchor để vẽ popup (TextMeshPro, icon...) nếu muốn.")]
    public Transform promptAnchor;

    int _timesUsed;

    void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true; // cho dễ xài raycast + trigger

        interactRange = 2.0f;
    }

    /// <summary>Tên hiển thị trên HUD/popup.</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(overrideDisplayName))
                return overrideDisplayName;
            if (weaponDef)
                return weaponDef.weaponName;
            return name;
        }
    }

    public string WeaponId => weaponDef ? weaponDef.weaponId : string.Empty;

    public bool CanUse()
    {
        if (infiniteUse) return true;
        return _timesUsed < maxUses;
    }

    public void NotifyUsed()
    {
        if (!infiniteUse)
            _timesUsed = Mathf.Min(_timesUsed + 1, maxUses);
    }
}
