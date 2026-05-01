using UnityEngine;

public interface ILoadoutState
{
    int ActiveSlot { get; }
    int SlotCount { get; }

    WeaponSlotState GetSlot(int index);

    // >>> THÊM (dùng chung SP/MP):
    /// <summary>Trừ 1 viên ở slot đang active nếu có thể.</summary>
    bool TryConsumeOneOnActive(out WeaponDef def);

    /// <summary>Bắt đầu reload ở slot đang active nếu hợp lệ.</summary>
    bool TryStartReloadOnActive(out WeaponDef def);

    /// <summary>Hoàn tất reload ở slot đang active (đổ đạn vào băng).</summary>
    bool CompleteReloadOnActive();

    /// <summary>Đang reload?</summary>
    bool IsReloading { get; }

    // ===== có sẵn:
    bool TryPickup(WorldWeapon ww);
    bool TryReplace(WorldWeapon ww);
    bool TryDropActive();
    void SelectActiveSlot(int index);
    /// <summary>Refill FULL mag + FULL reserve cho tất cả slot có vũ khí.</summary>
    bool TryFillMaxAmmoAll();


    event System.Action<int> OnSlotChanged;
    event System.Action<int> OnActiveSlotChanged;
}

