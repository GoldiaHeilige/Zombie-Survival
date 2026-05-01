using System;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    [Header("Slots")]
    [Tooltip("Số slot vũ khí hiện có (mặc định 2; có thể tăng lên 3 khi thêm dao).")]
    public int slotCount = 2;

    [Tooltip("Danh sách vũ khí theo slot (chỉ giữ WeaponDef, runtime ammo nằm nơi khác).")]
    public WeaponDef[] slots;

    /// <summary>
    /// Thông báo slot thay đổi (index).
    /// </summary>
    public event Action<int> OnSlotChanged;

    void Awake()
    {
        if (slotCount <= 0) slotCount = 2;
        if (slots == null || slots.Length != slotCount)
            slots = new WeaponDef[slotCount];
    }

    /// <summary>
    /// API cũ: Thêm vũ khí vào inventory.
    /// 1) Nếu có slot trống → đặt vào đó.
    /// 2) Nếu full → thay slot 0 (policy cũ để tương thích).
    /// </summary>
    public bool AddToSlot(WeaponDef def, out int slotIndex)
    {
        slotIndex = -1;
        if (def == null) return false;

        int empty = FindEmptySlot();
        if (empty >= 0)
        {
            slots[empty] = def;
            slotIndex = empty;
            OnSlotChanged?.Invoke(empty);
            return true;
        }

        // Policy cũ: thay slot 0 nếu full (giữ nguyên hành vi trước đây)
        slots[0] = def;
        slotIndex = 0;
        OnSlotChanged?.Invoke(0);
        return true;
    }

    /// <summary>
    /// Thêm vào slot trống, nếu full thì trả false (để caller tự quyết swap).
    /// </summary>
    public bool TryAddToEmpty(WeaponDef def, out int slotIndex)
    {
        slotIndex = -1;
        if (def == null) return false;

        int empty = FindEmptySlot();
        if (empty < 0) return false;

        slots[empty] = def;
        slotIndex = empty;
        OnSlotChanged?.Invoke(empty);
        return true;
    }

    /// <summary>
    /// Đặt WeaponDef vào slot index (có thể là null để xoá).
    /// </summary>
    public void SetSlot(int index, WeaponDef def)
    {
        if (!IsValidIndex(index)) return;
        slots[index] = def;
        OnSlotChanged?.Invoke(index);
    }

    /// <summary>
    /// Xoá vũ khí khỏi slot.
    /// </summary>
    public void ClearSlot(int index)
    {
        if (!IsValidIndex(index)) return;
        if (slots[index] != null)
        {
            slots[index] = null;
            OnSlotChanged?.Invoke(index);
        }
    }

    /// <summary>
    /// Lấy WeaponDef ở slot index.
    /// </summary>
    public WeaponDef GetSlot(int index)
    {
        if (!IsValidIndex(index)) return null;
        return slots[index];
    }

    /// <summary>
    /// Tìm slot trống, trả về -1 nếu không có.
    /// </summary>
    public int FindEmptySlot()
    {
        if (slots == null) return -1;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i] == null) return i;
        return -1;
    }

    /// <summary>
    /// Có slot trống không?
    /// </summary>
    public bool HasEmptySlot() => FindEmptySlot() >= 0;

    /// <summary>
    /// Tìm slot có weaponId (cùng loại vũ khí). Trả -1 nếu không có.
    /// </summary>
    public int FindSlotByWeaponId(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId) || slots == null) return -1;
        for (int i = 0; i < slots.Length; i++)
        {
            var def = slots[i];
            if (def != null && def.weaponId == weaponId)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Đảm bảo số lượng slot (ví dụ sau này tăng lên 3 để thêm dao).
    /// Giữ nguyên nội dung cũ, thêm slot trống nếu mở rộng.
    /// </summary>
    public void EnsureSlotCount(int desiredCount)
    {
        if (desiredCount <= 0) desiredCount = 1;
        if (slots != null && slots.Length == desiredCount)
        {
            slotCount = desiredCount;
            return;
        }

        var old = slots;
        slots = new WeaponDef[desiredCount];
        slotCount = desiredCount;

        if (old != null)
        {
            int copy = Mathf.Min(old.Length, slots.Length);
            for (int i = 0; i < copy; i++)
                slots[i] = old[i];
        }

        // phát tín hiệu toàn bộ slot có thể đã đổi
        for (int i = 0; i < slots.Length; i++)
            OnSlotChanged?.Invoke(i);
    }

    /// <summary>
    /// Số slot hiện có (helper).
    /// </summary>
    public int GetSlotCount() => slots != null ? slots.Length : 0;

    bool IsValidIndex(int index) => index >= 0 && slots != null && index < slots.Length;
}
