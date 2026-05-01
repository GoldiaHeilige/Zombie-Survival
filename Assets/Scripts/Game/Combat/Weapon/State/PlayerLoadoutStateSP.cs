using UnityEngine;
using TT;

/// <summary>
/// Loadout data-driven cho Singleplayer / offline.
/// Không cần NetworkObject.
/// </summary>
public class PlayerLoadoutStateSP : MonoBehaviour, ILoadoutState
{
    [SerializeField] int _slotCount = 2;
    [SerializeField] int _activeSlot = 0;
    private readonly WeaponSlotState[] _slots = new WeaponSlotState[4];

    [SerializeField] private bool giveStartingWeapon = true;
    [SerializeField] private WeaponDef startingWeapon; // ví dụ: pistol

    public int ActiveSlot => _activeSlot;
    public int SlotCount => _slotCount;

    bool _reloading;
    float _reloadEndTime;

    public event System.Action<int> OnSlotChanged;
    public event System.Action<int> OnActiveSlotChanged;

    public bool IsReloading => _reloading;

    void Update()
    {
        if (_reloading && Time.time >= _reloadEndTime)
        {
            CompleteReloadOnActive(); // có Notify + OnSlotChanged bạn đã viết sẵn
        }
    }

    private void Start()
    {
        if (!giveStartingWeapon || startingWeapon == null) return;

        // Nếu slot 0 đã có gì đó thì thôi
        if (!_slots[0].IsEmpty) return;

        int key = WeaponIdRegistry.GetKey(startingWeapon.weaponId);
        if (key == 0)
        {
            Debug.LogWarning("[SP Loadout] startingWeapon has no registry key (weaponId not registered?)");
            return;
        }

        _slots[0] = new WeaponSlotState
        {
            weaponKey = key,
            mag = (ushort)startingWeapon.magSize,
            reserve = (ushort)startingWeapon.startReserve
        };

        _activeSlot = 0;

        OnSlotChanged?.Invoke(0);
        OnActiveSlotChanged?.Invoke(0);

        // (optional) bắn event feed giống flow khác
        TT.Observer.Instance?.NotifyWithData(
            TT.WeaponTopics.Picked,
            (gameObject, startingWeapon, 0, _slots[0].mag, _slots[0].reserve)
        );
    }


    public WeaponSlotState GetSlot(int index)
    {
        if (index < 0 || index >= _slots.Length) return WeaponSlotState.Empty;
        return _slots[index];
    }

    public bool TryConsumeOneOnActive(out WeaponDef def)
    {
        def = null;

        if (_activeSlot < 0 || _activeSlot >= _slotCount) return false;
        ref var s = ref _slots[_activeSlot];
        if (s.IsEmpty || s.mag <= 0) return false;

        def = WeaponIdRegistry.GetDef(s.weaponKey);
        if (def == null) return false;

        s.mag = (ushort)Mathf.Max(0, s.mag - 1);

        TT.Observer.Instance?.NotifyWithData(
            "weapon.fired",  // hoặc TT.WeaponTopics.Fired nếu const này = "weapon.fired"
            (gameObject, _activeSlot, s.mag, s.reserve)
        );

        // Nếu bạn có event OnSlotChanged thì Raise ở đây để HUD cập nhật:
        OnSlotChanged?.Invoke(_activeSlot);

        if (!_reloading && s.mag <= 0 && s.reserve > 0)
        {
            TryStartReloadOnActive(out _);
        }

        return true;
    }

        
    public bool TryStartReloadOnActive(out WeaponDef def)
    {
        def = null;
        if (_reloading) return false;
        if (_activeSlot < 0 || _activeSlot >= _slotCount) return false;
        var s = _slots[_activeSlot];
        if (s.IsEmpty) return false;

        def = WeaponIdRegistry.GetDef(s.weaponKey);
        if (def == null) return false;

        if (s.mag >= def.magSize) return false;
        if (s.reserve <= 0) return false;

        float mult = 1f;
        var perk = GetComponentInParent<TT.PerkManager>(true);
        if (perk != null) mult = perk.GetReloadDurationMultiplier();

        float seconds = Mathf.Max(0.01f, def.reloadTime * mult);

        _reloading = true;
        _reloadEndTime = Time.time + seconds;

        // DEBUG (xong rồi xoá)
     //   Debug.Log($"[SpeedCola][SP] Reload start | weapon={def.weaponName} base={def.reloadTime:0.00}s mult={mult:0.00} dur={seconds:0.00}s");


        TT.Observer.Instance?.NotifyWithData(
            TT.WeaponTopics.ReloadStarted,
            (gameObject, _activeSlot)
        );
           
        return true;
    }

    public bool CompleteReloadOnActive()
    {
        if (!_reloading) return false;
        if (_activeSlot < 0 || _activeSlot >= _slotCount) { _reloading = false; return false; }
        ref var s = ref _slots[_activeSlot];
        if (s.IsEmpty) { _reloading = false; return false; }

        var def = WeaponIdRegistry.GetDef(s.weaponKey);
        if (def == null) { _reloading = false; return false; }

        int need = def.magSize - s.mag;
        if (need <= 0 || s.reserve <= 0) { _reloading = false; return false; }

        int take = Mathf.Min(need, s.reserve);
        s.mag = (ushort)(s.mag + take);
        s.reserve = (ushort)(s.reserve - take);

        _reloading = false;
        TT.Observer.Instance?.NotifyWithData(
            TT.WeaponTopics.ReloadFinished,
            (gameObject, _activeSlot, s.mag, s.reserve)
        );

        OnSlotChanged?.Invoke(_activeSlot);
        OnActiveSlotChanged?.Invoke(_activeSlot);
        return true;
    }

    public bool TryPickup(WorldWeapon ww)
    {
        if (!ww || !ww.weaponDef) return false;
        var def = ww.weaponDef;
        int key = WeaponIdRegistry.GetKey(def.weaponId);

        int same = FindSlotByKey(key);
        if (same >= 0)
        {
            var s = _slots[same];
            int maxReserve = Mathf.Max(def.maxReserve, def.startReserve);
            int canTake = Mathf.Min(maxReserve - s.reserve, Mathf.Max(0, ww.reserveOnGround));
            if (canTake <= 0) return false;
            s.reserve = (ushort)(s.reserve + canTake);
            ww.reserveOnGround -= canTake;
            _slots[same] = s;
            OnSlotChanged?.Invoke(same);
            return true;
        }

        int empty = FindEmptySlot();
        if (empty >= 0)
        {
            var s = new WeaponSlotState
            {
                weaponKey = key,
                mag = (ushort)Mathf.Clamp(ww.magOnGround, 0, def.magSize),
                reserve = (ushort)Mathf.Clamp(ww.reserveOnGround, 0, def.maxReserve)
            };
            _slots[empty] = s;
            _activeSlot = empty;
            // Cập nhật slot UI
            OnSlotChanged?.Invoke(empty);

            return true;
        }

        return false;
    }

    public bool TryReplace(WorldWeapon ww)
    {
        if (!ww || !ww.weaponDef) return false;
        var def = ww.weaponDef;
        int key = WeaponIdRegistry.GetKey(def.weaponId);

        var s = new WeaponSlotState
        {
            weaponKey = key,
            mag = (ushort)Mathf.Clamp(ww.magOnGround, 0, def.magSize),
            reserve = (ushort)Mathf.Clamp(ww.reserveOnGround, 0, def.maxReserve)
        };
        _slots[_activeSlot] = s;
        OnSlotChanged?.Invoke(_activeSlot);
        return true;
    }

    public bool TryDropActive()
    {
        var s = _slots[_activeSlot];
        if (s.IsEmpty) return false;
        _slots[_activeSlot] = WeaponSlotState.Empty;
        OnSlotChanged?.Invoke(_activeSlot);
        return true;
    }

    public void SelectActiveSlot(int index)
    {
        int clamped = Mathf.Clamp(index, 0, Mathf.Max(1, _slotCount) - 1);

        // Nếu slot không đổi thì thôi, khỏi làm gì
        if (clamped == _activeSlot)
            return;

        if (_reloading)
        {
            _reloading = false;
            // Có thể thêm event nếu sau này cần:
            TT.Observer.Instance?.NotifyWithData("weapon.reload.cancelled", (gameObject, _activeSlot));
        }

        _activeSlot = Mathf.Clamp(index, 0, Mathf.Max(1, _slotCount) - 1);
        OnActiveSlotChanged?.Invoke(_activeSlot);
    }

    public int FindEmptySlot()
    {
        for (int i = 0; i < _slotCount; i++) if (_slots[i].IsEmpty) return i;
        return -1;
    }

    public int FindSlotByKey(int key)
    {
        for (int i = 0; i < _slotCount; i++) if (_slots[i].weaponKey == key) return i;
        return -1;
    }

    /// <summary>
    /// Mua súng / mua đạn từ WeaponShopSpot (singleplayer/offline).
    /// - Nếu đã có khẩu đó → mua đạn: reserve full = def.maxReserve, mag giữ nguyên.
    /// - Nếu chưa có khẩu đó:
    ///     + Nếu còn slot trống → cho vào slot trống.
    ///     + Nếu full slot → drop khẩu đang cầm rồi gán khẩu mới vào.
    /// </summary>
    public bool TryBuyFromShop(WeaponShopSpot shop, PlayerPoints points, PlayerPickup pickup, GameObject owner)
    {
        if (!shop || !shop.weaponDef || points == null)
            return false;
        if (!shop.CanUse())
            return false;

        var def = shop.weaponDef;
        int key = WeaponIdRegistry.GetKey(def.weaponId);
        if (key == 0)
        {
            Debug.LogWarning("[SP Loadout] TryBuyFromShop: weaponKey = 0, weaponId=" + def.weaponId);
            return false;
        }

        // Đã có khẩu này? → mua ammo
        int haveSlot = FindSlotByKey(key);
        if (haveSlot >= 0)
        {
            ref var s = ref _slots[haveSlot];
            int maxReserve = Mathf.Max(def.maxReserve, def.startReserve);

            // Nếu đã full rồi thì thôi
            if (s.reserve >= maxReserve)
            {
                Debug.Log("[SP Loadout] Ammo already full, skip buy.");
                return false;
            }

            // Trừ điểm
            if (!points.TrySpend(shop.ammoCost, PointReason.Purchase, shop.gameObject))
            {
                Debug.Log("[SP Loadout] Not enough points for ammo.");
                return false;
            }

            // Fill full reserve, giữ nguyên mag
            s.reserve = (ushort)maxReserve;
            OnSlotChanged?.Invoke(haveSlot);
            shop.NotifyUsed();

            // (Optional) bắn Observer để HUD/FX nghe
            TT.Observer.Instance?.NotifyWithData(
                TT.WeaponTopics.ReloadFinished, // hoặc tạo topic riêng kiểu "weapon.ammo.bought"
                (owner, haveSlot)
            );

            return true;
        }

        // Chưa có khẩu này → mua súng
        if (!points.TrySpend(shop.weaponCost, PointReason.Purchase, shop.gameObject))
        {
            Debug.Log("[SP Loadout] Not enough points for weapon.");
            return false;
        }

        int targetSlot = FindEmptySlot();

        // Full slot → drop active rồi tìm lại slot trống
        if (targetSlot < 0)
        {
            if (pickup != null)
            {
                Debug.Log("[SP Loadout] Full slots → dropping active weapon before buy.");
                pickup.ServerSpawnDropActive();
            }

            targetSlot = FindEmptySlot();
            if (targetSlot < 0)
            {
                // Fallback: nếu vì lý do gì đó vẫn không rỗng thì overwrite active
                targetSlot = Mathf.Clamp(_activeSlot, 0, _slotCount - 1);
            }
        }

        var newState = new WeaponSlotState
        {
            weaponKey = key,
            mag = (ushort)def.magSize,          // băng full
            reserve = (ushort)def.startReserve  // reserve theo startReserve (CoD-style)
        };

        _slots[targetSlot] = newState;
        _activeSlot = targetSlot;

        OnSlotChanged?.Invoke(targetSlot);
        OnActiveSlotChanged?.Invoke(targetSlot);
        shop.NotifyUsed();

        TT.Observer.Instance?.NotifyWithData(
            TT.WeaponTopics.Picked,
            (owner, def, targetSlot, newState.mag, newState.reserve)
        );

        return true;
    }

    public void ClearSlot(int index)
    {
        _slots[index].weaponKey = 0;
        _slots[index].mag = 0;
        _slots[index].reserve = 0;
        OnSlotChanged?.Invoke(index);
    }

    public bool TryFillMaxAmmoAll()
    {
        bool changed = false;

        // Cancel reload nếu đang reload (giống CoD: max ammo thì không cần reload state lằng nhằng)
        if (_reloading)
        {
            _reloading = false;
            _reloadEndTime = 0f;
        }

        for (int i = 0; i < _slotCount; i++)
        {
            ref var s = ref _slots[i];
            if (s.IsEmpty) continue;

            var def = WeaponIdRegistry.GetDef(s.weaponKey);
            if (def == null) continue;

            int maxReserve = Mathf.Max(def.maxReserve, def.startReserve);

            ushort newMag = (ushort)Mathf.Clamp(def.magSize, 0, ushort.MaxValue);
            ushort newReserve = (ushort)Mathf.Clamp(maxReserve, 0, ushort.MaxValue);

            if (s.mag != newMag || s.reserve != newReserve)
            {
                s.mag = newMag;
                s.reserve = newReserve;
                changed = true;

                OnSlotChanged?.Invoke(i);
            }
        }

        if (changed)
            OnActiveSlotChanged?.Invoke(_activeSlot);

        return changed;
    }

}
