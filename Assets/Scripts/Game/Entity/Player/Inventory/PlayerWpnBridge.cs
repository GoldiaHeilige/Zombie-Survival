using System;
using System.Collections;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerInventory))]
public class PlayerWeaponBridge : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private WeaponController weaponController;

    [Header("State")]
    [SerializeField] private int activeSlot = 0;

    // Runtime-by-slot
    private AmmoModule[] runtimes;
    private string[] runtimeGuids;

    // Chặn auto-select khi inventory đang thay đổi (tránh init bằng startReserve)
    private bool suppressAutoEquip = false;

    public event Action<int> OnActiveSlotChanged; // -1 nếu tay không
    public int ActiveSlot => activeSlot;

    private PlayerStateProvider _stateProv;
    private ILoadoutState _loadout;
    private bool _bound;

    private FusionNetBridge _net;
    private bool IsLocalOwner =>
    _net == null ||
    _net.Object == null ||
    !_net.Object.IsValid ||
    _net.IsLocalOwner;

    // ===== Public accessors =====
    public WeaponController GetCurrentWeapon() => weaponController;
    public int GetActiveSlotIndex() => activeSlot;
    public int GetSlotCount() => inventory != null && inventory.slots != null ? inventory.slots.Length : 0;
    public WeaponDef GetSlotDef(int index) => inventory != null ? inventory.GetSlot(index) : null;

    public AmmoModule GetRuntime(int slotIndex)
        => (runtimes != null && slotIndex >= 0 && slotIndex < runtimes.Length) ? runtimes[slotIndex] : null;

    public string GetRuntimeGuid(int slotIndex)
        => (runtimeGuids != null && slotIndex >= 0 && slotIndex < runtimeGuids.Length) ? runtimeGuids[slotIndex] : null;

    public void SetRuntimeGuid(int slotIndex, string guid)
    {
        EnsureArrays();
        if (slotIndex >= 0 && slotIndex < runtimeGuids.Length)
        {
            runtimeGuids[slotIndex] = guid;

            // Nếu đang cầm slot này thì cập nhật identity ngay
            if (slotIndex == activeSlot && weaponController != null)
                weaponController.SetRuntimeIdentity(guid, slotIndex);
        }
    }


    // Dùng tạm tắt auto-equip trong lúc thêm/điền slot
    public void BeginInventoryTransaction() => suppressAutoEquip = true;
    public void EndInventoryTransaction() => suppressAutoEquip = false;

    void EnsureArrays()
    {
        if (inventory == null || inventory.slots == null) return;
        if (runtimes == null || runtimes.Length != inventory.slots.Length)
            runtimes = new AmmoModule[inventory.slots.Length];
        if (runtimeGuids == null || runtimeGuids.Length != inventory.slots.Length)
            runtimeGuids = new string[inventory.slots.Length];
    }

    void Reset()
    {
        inventory = GetComponent<PlayerInventory>();
        if (weaponController == null) weaponController = GetComponentInChildren<WeaponController>();
    }

    void Awake()
    {
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (weaponController == null) weaponController = GetComponentInChildren<WeaponController>();

        _net = GetComponentInParent<FusionNetBridge>(true);
        EnsureArrays();
//
 //       Debug.Log($"[PlayerWeaponBridge] NetBridge: {_net != null}, IsLocalOwner: {(_net?.IsLocalOwner ?? false)}");

        StartCoroutine(Co_BindStateAndPrime());

        IEnumerator Co_BindStateAndPrime()
        {
            // 1) Chờ PlayerStateProvider tồn tại
            while (_stateProv == null)
            {
                _stateProv = GetComponentInParent<PlayerStateProvider>(true);
                yield return null;
            }

            // 2) Chờ Loadout được set (tránh race Awake order)
            while (_loadout == null)
            {
                _loadout = _stateProv.Loadout;
                yield return null;
            }

            // 3) Bind đúng 1 lần
            if (!_bound)
            {
                _loadout.OnSlotChanged += HandleSlotChanged_FromState;
                _loadout.OnActiveSlotChanged += HandleActiveSlotChanged_FromState;
                _bound = true;
            }

            // 4) Nếu MP: chờ NetworkObject valid rồi mới prime
            var mp = _loadout as NetworkBehaviour;
            if (mp != null)
                yield return new WaitUntil(() => mp.Object && mp.Runner != null && mp.Object.IsValid);

            yield return null;
            yield return null;

            // 5) Prime lần đầu
            TryPrimeFromState();
        }

    }

    private void OnDisable()
    {
        if (_bound && _loadout != null)
        {
            _loadout.OnSlotChanged -= HandleSlotChanged_FromState;
            _loadout.OnActiveSlotChanged -= HandleActiveSlotChanged_FromState;
        }
        _bound = false;

/*        if (IsLocalOwner && weaponController != null)
            weaponController.EquipFromDef(null);*/
    }


    public void SelectSlot(int index)
    {
        if (inventory == null || weaponController == null || inventory.slots == null) return;
        if (index < 0 || index >= inventory.slots.Length) return;

        if (index == activeSlot)
            return;

        var def = inventory.GetSlot(index);
        activeSlot = index;

        // === MP branch: dùng StateAuthority làm "nguồn sự thật" ===
        var nb = _loadout as NetworkBehaviour;
        bool isMP = nb != null && nb.Runner != null && nb.Object != null && nb.Object.IsValid;

        if (isMP)
        {
            var mpState = _loadout as PlayerLoadoutStateMP;
            if (mpState != null)
            {
                if (_net != null && _net.HasStateAuth)
                {
                    // HOST: đổi slot trực tiếp trên state (không equip cục bộ ở đây)
                    mpState.SelectActiveSlot(index);
                    // Cập nhật index local để UI biết ngay; view sẽ equip qua HandleSlotChanged_FromState()
                    OnActiveSlotChanged?.Invoke(activeSlot);
                    return;
                }
                else
                {
                    // CLIENT: gửi yêu cầu cho host; không equip cục bộ
                    mpState.RPC_RequestSetActiveSlot(index);
                    OnActiveSlotChanged?.Invoke(activeSlot);
                    return;
                }
            }
        }

        // ==== SP (hoặc fallback nếu không có state MP) tiếp tục flow local ====

        if (_loadout != null)
        {
            _loadout.SelectActiveSlot(index);
        }

        if (def == null)
        {
            if (IsLocalOwner && weaponController != null)
                weaponController.EquipFromDef(null);
            OnActiveSlotChanged?.Invoke(-1);
            return;
        }

        EnsureArrays();

        var rt = GetRuntime(activeSlot);
/*        if (rt != null && rt.magSize == def.magSize)
        {
            if (IsLocalOwner && weaponController != null)
            {
                weaponController.Equip(def, rt, initIfNew: false);
                weaponController.SetRuntimeIdentity(GetRuntimeGuid(activeSlot), activeSlot);
                weaponController.RaiseAmmoChanged(def.weaponId, GetRuntimeGuid(activeSlot), activeSlot, rt, def.magSize);
            }
            OnActiveSlotChanged?.Invoke(activeSlot);
        }*/

        bool hasStateRuntime = runtimes[activeSlot] != null && runtimes[activeSlot].magSize == def.magSize;
        if (!hasStateRuntime)
        {
            runtimes[activeSlot] = new AmmoModule();
            runtimes[activeSlot].ResetFromDef(def, fullMag: true, setReserve: def.startReserve);
            Debug.Log($"[Bridge] SelectSlot reset runtime default for slot {activeSlot} (no state runtime).");
        }

        bool isAuthoritative = _net == null || _net.HasStateAuth;
        if (string.IsNullOrEmpty(GetRuntimeGuid(activeSlot)) && isAuthoritative)
        {
            SetRuntimeGuid(activeSlot, System.Guid.NewGuid().ToString());
            Debug.Log($"[Bridge] Generated GUID locally for slot {activeSlot} (authoritative).");
        }

/*        if (IsLocalOwner && weaponController != null)
        {
            weaponController.Equip(def, runtimes[activeSlot], initIfNew: false);
            weaponController.SetRuntimeIdentity(GetRuntimeGuid(activeSlot), activeSlot);
            weaponController.RaiseAmmoChanged(def.weaponId, GetRuntimeGuid(activeSlot), activeSlot, runtimes[activeSlot], def.magSize);
        }
        OnActiveSlotChanged?.Invoke(activeSlot);*/

 //       Debug.Log($"[Bridge] Equip slot {activeSlot}: {def.name} (mag={runtimes[activeSlot].mag}, reserve={runtimes[activeSlot].reserve})");
    }


    // thêm trong class PlayerWeaponBridge
    public void SelectPrevSlot()
    {
        int count = GetSlotCount();          
        int next = Mathf.Clamp(activeSlot - 1, 0, count - 1);
        if (next != activeSlot) SelectSlot(next);
    }

    public void SelectNextSlot()
    {
        int count = GetSlotCount();
        int next = Mathf.Clamp(activeSlot + 1, 0, count - 1);
        if (next != activeSlot) SelectSlot(next);
    }

    public void EquipIntoSlot(int slotIndex, WeaponDef def, AmmoModule runtime, string runtimeGuid = null)
    {
        ForceResolveRefs();
        if (inventory == null || weaponController == null || def == null) return;
        if (inventory.slots == null || inventory.slots.Length == 0) return;

        EnsureArrays();
        slotIndex = Mathf.Clamp(slotIndex, 0, inventory.slots.Length - 1);

        bool prev = suppressAutoEquip;
        suppressAutoEquip = true;
        inventory.SetSlot(slotIndex, def);
        suppressAutoEquip = prev;

        // Gán runtime
        runtimes[slotIndex] = runtime ?? new AmmoModule { mag = def.magSize, reserve = def.startReserve, magSize = def.magSize };

        if (string.IsNullOrEmpty(runtimeGuid))
            runtimeGuid = System.Guid.NewGuid().ToString();
        SetRuntimeGuid(slotIndex, runtimeGuid);

        // 👉 Chỉ equip trực tiếp nếu bạn thật sự muốn chuyển sang slot này
        // (ví dụ khi vừa thêm vũ khí mới)
        if (IsLocalOwner && weaponController != null)
        {
            weaponController.Equip(def, runtimes[slotIndex], initIfNew: false);
            weaponController.SetRuntimeIdentity(GetRuntimeGuid(slotIndex), slotIndex);
            weaponController.RaiseAmmoChanged(def.weaponId, GetRuntimeGuid(slotIndex), slotIndex, runtimes[slotIndex], def.magSize);
        }
        OnActiveSlotChanged?.Invoke(activeSlot);

    }

    public int FindSlotByWeaponId(string weaponId)
    {
        if (inventory?.slots == null) return -1;
        for (int i = 0; i < inventory.slots.Length; i++)
        {
            var d = inventory.GetSlot(i);
            if (d != null && d.weaponId == weaponId) return i;
        }
        return -1;
    }

    public void ClearRuntimeForSlot(int slotIndex)
    {
        EnsureArrays();
        if (slotIndex < 0 || slotIndex >= (runtimes?.Length ?? 0)) return;
        runtimes[slotIndex] = null;
        if (runtimeGuids != null) runtimeGuids[slotIndex] = null;
    }
    public void NotifySlotAmmoChanged(int slotIndex)
    {
        EnsureArrays();
        var def = GetSlotDef(slotIndex);
        var rt = GetRuntime(slotIndex);
        if (weaponController == null || def == null || rt == null) return;

        var guid = GetRuntimeGuid(slotIndex);
        weaponController.RaiseAmmoChanged(def.weaponId, guid, slotIndex, rt, def.magSize);
    }

    private void TryPrimeFromState()
    {
        if (_loadout == null) return;

        int count;
        try { count = _loadout.SlotCount; }
        catch { return; } // MP chưa Spawned — phòng hờ

        if (inventory) inventory.EnsureSlotCount(count);
        EnsureArrays();

        for (int i = 0; i < count; i++)
            HandleSlotChanged_FromState(i);

        int active;
        try { active = _loadout.ActiveSlot; }
        catch { return; }
        HandleActiveSlotChanged_FromState(active);
    }


    private void HandleSlotChanged_FromState(int idx)
    {
        if (_loadout == null) return;

        // --- đọc từ state ---
        var slot = _loadout.GetSlot(idx);
        var def = WeaponIdRegistry.GetDef(slot.weaponKey);

        // cache def hiện đang hiển thị trong Inventory (trước khi set mới)
        var prevDef = inventory ? inventory.GetSlot(idx) : null;
        var prevId = prevDef ? prevDef.weaponId : null;
        var newId = def ? def.weaponId : null;
        bool weaponChanged = prevId != newId || slot.weaponKey == 0;

        // đồng bộ Inventory (view cache cho UI)
        if (inventory) inventory.SetSlot(idx, def);

        // slot rỗng -> xử lý cũ
        if (def == null || slot.weaponKey == 0)
        {
            if (inventory) inventory.ClearSlot(idx);

            if (idx == activeSlot && IsLocalOwner && weaponController != null)
            {
                weaponController.EquipFromDef(null);
                ClearRuntimeForSlot(idx);
             //   activeSlot = -1;
                OnActiveSlotChanged?.Invoke(-1);
            }
            return;
        }

        // cập nhật runtime cache theo state
        EnsureArrays();
        runtimes[idx] = new AmmoModule
        {
            mag = slot.mag,
            reserve = slot.reserve,
            magSize = def.magSize
        };

        // --- QUYẾT ĐỊNH EQUIP HAY CHỈ CẬP NHẬT ĐẠN ---
        bool isActiveInState = (_loadout.ActiveSlot == idx);

        if (isActiveInState)
        {
            // Nếu đổi hẳn vũ khí -> Equip (đúng hành vi cũ)
            if (weaponChanged || weaponController == null || weaponController.def != def)
            {
                if (IsLocalOwner && weaponController != null)
                {
                    weaponController.Equip(def, runtimes[idx], initIfNew: false);
                    weaponController.SetRuntimeIdentity(GetRuntimeGuid(idx), idx);
                    weaponController.RaiseAmmoChanged(def.weaponId, GetRuntimeGuid(idx), idx, runtimes[idx], def.magSize);
                }
                activeSlot = idx;
                OnActiveSlotChanged?.Invoke(activeSlot);
            }
            else
            {
                // KHÔNG đổi vũ khí -> KHÔNG Equip (tránh nháy Equipping)
                // chỉ bắn sự kiện đạn để HUD cập nhật
                if (IsLocalOwner && weaponController != null)
                {
                    weaponController.RaiseAmmoChanged(def.weaponId, GetRuntimeGuid(idx), idx, runtimes[idx], def.magSize);
                }
                // activeSlot giữ nguyên
            }
        }
    }


    private void HandleActiveSlotChanged_FromState(int active)
    {
        // 1) Cập nhật index + event
        activeSlot = active;
        OnActiveSlotChanged?.Invoke(activeSlot);

        if (weaponController != null)
        {
            weaponController._slotIndex = activeSlot;
        }

        if (_loadout == null || inventory == null) return;

        // 2) Lấy state thực tế của slot vừa active
        var slot = (activeSlot >= 0 && activeSlot < _loadout.SlotCount)
            ? _loadout.GetSlot(activeSlot)
            : WeaponSlotState.Empty;

        // 3) Tay không → clear model ngay
        if (slot.weaponKey == 0)
        {
            if (IsLocalOwner && weaponController != null)
                weaponController.EquipFromDef(null);
            return;
        }

        // 4) Có vũ khí → đảm bảo runtime rồi EQUIP NGAY (kể cả key không đổi)
        var def = WeaponIdRegistry.GetDef(slot.weaponKey);
        if (def == null)
        {
            if (IsLocalOwner && weaponController != null)
                weaponController.EquipFromDef(null);
            return;
        }

        EnsureArrays();
        var rt = new AmmoModule { mag = slot.mag, reserve = slot.reserve, magSize = def.magSize };
        runtimes[activeSlot] = rt;

        bool shouldReEquip = ShouldReEquipWeapon(def, activeSlot);

        if (IsLocalOwner && weaponController != null && shouldReEquip)
        {
            weaponController.Equip(def, rt, initIfNew: false);
            var guid = GetRuntimeGuid(activeSlot);
            if (!string.IsNullOrEmpty(guid))
                weaponController.SetRuntimeIdentity(guid, activeSlot);
            weaponController.RaiseAmmoChanged(def.weaponId, guid, activeSlot, rt, def.magSize);
        }
        else if (IsLocalOwner && weaponController != null && !shouldReEquip)
        {
            // Chỉ cập nhật đạn, không equip lại
            var guid = GetRuntimeGuid(activeSlot);
            weaponController.RaiseAmmoChanged(def.weaponId, guid, activeSlot, rt, def.magSize);

            // Cập nhật ammo module trong weapon controller
            if (weaponController.ammo != null)
            {
                weaponController.ammo.SetCounts(rt.mag, rt.reserve, rt.magSize);
            }
        }
    }

    private bool ShouldReEquipWeapon(WeaponDef newDef, int slotIndex)
    {
        // Nếu weapon controller không có súng nào → cần equip
        if (weaponController == null || weaponController.def == null)
            return true;

        // Nếu súng khác → cần equip
        if (weaponController.def.weaponId != newDef.weaponId)
            return true;

        // Nếu slot khác → cần equip (chuyển slot)
        if (weaponController != null && slotIndex != weaponController._slotIndex)
            return true;

        // Nếu đang reload → không cần equip lại (chỉ update ammo)
        if (_loadout != null && _loadout.IsReloading)
            return false;

        // Các trường hợp còn lại → không cần equip lại
        return false;
    }

    public void ForceResolveRefs()
    {
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (weaponController == null) weaponController = GetComponentInChildren<WeaponController>(true);
        EnsureArrays();
    }

}
