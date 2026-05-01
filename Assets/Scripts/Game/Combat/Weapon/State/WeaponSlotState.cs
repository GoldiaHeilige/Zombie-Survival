using Fusion;

/// <summary>
/// Dữ liệu sync gọn cho mỗi slot vũ khí.
/// Dùng int weaponKey (hash) để network-friendly.
/// </summary>
public struct WeaponSlotState : INetworkStruct
{
    public int weaponKey;    // 0 = empty
    public ushort mag;
    public ushort reserve;

    public bool IsEmpty => weaponKey == 0;

    public static WeaponSlotState Empty => new WeaponSlotState
    {
        weaponKey = 0,
        mag = 0,
        reserve = 0
    };
}
