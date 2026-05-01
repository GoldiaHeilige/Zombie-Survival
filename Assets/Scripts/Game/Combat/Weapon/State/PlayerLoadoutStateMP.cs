#if FUSION_WEAVER
using UnityEngine;
using Fusion;
using TT;

/// <summary>
/// Loadout data-driven cho Multiplayer.
/// ĐẶT TRÊN PLAYER CHA (cùng GameObject có NetworkObject).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerLoadoutStateMP : NetworkBehaviour, ILoadoutState
{
    [Networked] public int ActiveSlot { get; set; }
    [Networked] public int SlotCount { get; set; }

    [Networked] public WeaponSlotState Slot0 { get; set; }
    [Networked] public WeaponSlotState Slot1 { get; set; }

    [Networked] public TickTimer ReloadTimer { get; set; }
    [Networked] public NetworkBool NetIsReloading { get; set; }

    [SerializeField] private bool giveStartingWeapon = true;
    [SerializeField] private WeaponDef startingWeapon;

    private int _shopRpcCounter = 0;
    public bool IsReloading
    {
        get
        {
            // Guard: object chưa Spawned hoặc đã bị despawn thì 
            // tuyệt đối không đụng vào Networked property.
            if (!Object || !Object.IsValid || Runner == null)
                return false;

            return NetIsReloading;
        }
    }

    public event System.Action<int> OnSlotChanged;
    public event System.Action<int> OnActiveSlotChanged;

    private WeaponSlotState _p0, _p1;
    private int _pActive = -1, _pCount = -1;
    private bool _pReload;

    public WeaponSlotState GetSlot(int index) => index switch
    {
        0 => Slot0,
        1 => Slot1,
        _ => WeaponSlotState.Empty
    };
    private void SetSlot(int index, WeaponSlotState value)
    {
        switch (index)
        {
            case 0: Slot0 = value; break;
            case 1: Slot1 = value; break;
        }
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            var count = SlotCount <= 0 ? 2 : SlotCount;
            SlotCount = Mathf.Clamp(count, 1, 4);
            ActiveSlot = Mathf.Clamp(ActiveSlot, 0, SlotCount - 1);

            // ===== GIVE STARTING LOADOUT (HOST ONLY) =====
            if (giveStartingWeapon && startingWeapon != null && Slot0.IsEmpty)
            {
                int key = WeaponIdRegistry.GetKey(startingWeapon.weaponId);
                if (key == 0)
                {
                    Debug.LogWarning("[MP Loadout] startingWeapon has no registry key (weaponId not registered?)");
                    return;
                }

                var s = new WeaponSlotState
                {
                    weaponKey = key,
                    mag = (ushort)startingWeapon.magSize,
                    reserve = (ushort)startingWeapon.startReserve
                };

                Slot0 = s;
                ActiveSlot = 0;

                // Tạo GUID và sync cho local owner (y như flow pickup/drop của bạn)
                string guid = System.Guid.NewGuid().ToString();

                var hostBridge = GetComponentInChildren<PlayerWeaponBridge>(true);
                if (hostBridge != null) hostBridge.SetRuntimeGuid(0, guid); // giống cách bạn làm khi pickup【:contentReference[oaicite:2]{index=2}】

                var net = GetComponent<FusionNetBridge>();
                if (net != null)
                {
                    // sync guid về InputAuthority【:contentReference[oaicite:3]{index=3}】
                    net.RPC_SetSlotRuntimeGuid(0, guid);

                    // equip visual tức thì cho owner (đỡ 1-2 frame tay không)【:contentReference[oaicite:4]{index=4}
                    net.RPC_ClientEquipAfterPickup(0, key, s.mag, s.reserve, guid);
                }
            }
        }
    }

    // ===== Server-side gameplay API (ILoadoutState) =====

    public bool TryConsumeOneOnActive(out WeaponDef def)
    {
        def = null;
        if (!HasStateAuthority) return false;
        if (ActiveSlot < 0 || ActiveSlot >= SlotCount) return false;

        var s = GetSlot(ActiveSlot);
        if (s.IsEmpty || s.mag <= 0) return false;

        def = WeaponIdRegistry.GetDef(s.weaponKey);
        if (def == null) return false;

        s.mag = (ushort)(s.mag - 1);
        SetSlot(ActiveSlot, s);
        OnSlotChanged?.Invoke(ActiveSlot);

        if (!NetIsReloading && s.mag <= 0 && s.reserve > 0)
        {
            TryStartReloadOnActive(out _);
        }

        return true;
    }

    public bool TryStartReloadOnActive(out WeaponDef def)
    {
        def = null;
        if (!HasStateAuthority) return false;
        if (NetIsReloading) return false;
        if (ActiveSlot < 0 || ActiveSlot >= SlotCount) return false;

        var s = GetSlot(ActiveSlot);
        if (s.IsEmpty) return false;

        def = WeaponIdRegistry.GetDef(s.weaponKey);
        if (def == null) return false;

        if (s.mag >= def.magSize) return false;
        if (s.reserve <= 0) return false;

        NetIsReloading = true;

        float reloadMult = 1f;
        var perk = GetComponentInParent<TT.PerkManager>(true);
        if (perk != null) reloadMult = perk.GetReloadDurationMultiplier();

        float reloadSeconds = Mathf.Max(0.01f, def.reloadTime * reloadMult);
        ReloadTimer = TickTimer.CreateFromSeconds(Runner, reloadSeconds);

        return true;
    }

    public bool CompleteReloadOnActive()
    {
        if (!HasStateAuthority) return false;
        if (!NetIsReloading) return false;
        if (ActiveSlot < 0 || ActiveSlot >= SlotCount) { NetIsReloading = false; return false; }

        var s = GetSlot(ActiveSlot);
        if (s.IsEmpty) { NetIsReloading = false; return false; }

        var def = WeaponIdRegistry.GetDef(s.weaponKey);
        if (def == null) { NetIsReloading = false; return false; }

        int need = def.magSize - s.mag;
        if (need > 0 && s.reserve > 0)
        {
            int take = Mathf.Min(need, s.reserve);
            s.mag = (ushort)(s.mag + take);
            s.reserve = (ushort)(s.reserve - take);
            SetSlot(ActiveSlot, s);
            OnSlotChanged?.Invoke(ActiveSlot);
        }

        NetIsReloading = false;
        return true;
    }

    public bool TryPickup(WorldWeapon ww)
    {
        if (!HasStateAuthority) return false;
        if (!ww || !ww.weaponDef) return false;

        var def = ww.weaponDef;
        int key = WeaponIdRegistry.GetKey(def.weaponId);

        // merge?
        int same = FindSlotByKey(key);
        if (same >= 0)
        {
            var s = GetSlot(same);
            int maxReserve = Mathf.Max(def.maxReserve, def.startReserve);
            int canTake = Mathf.Min(maxReserve - s.reserve, Mathf.Max(0, ww.reserveOnGround));
            if (canTake <= 0) return false;
            s.reserve = (ushort)(s.reserve + canTake);
            ww.reserveOnGround -= canTake;
            SetSlot(same, s);
            return true;
        }

        // empty?
        int empty = FindEmptySlot();
        if (empty >= 0)
        {
            var s = new WeaponSlotState
            {
                weaponKey = key,
                mag = (ushort)Mathf.Clamp(ww.magOnGround, 0, def.magSize),
                reserve = (ushort)Mathf.Clamp(ww.reserveOnGround, 0, def.maxReserve)
            };
            SetSlot(empty, s);
            ActiveSlot = empty;
            return true;
        }

        return false; // full
    }

    public bool TryReplace(WorldWeapon ww)
    {
        if (!HasStateAuthority) return false;
        if (!ww || !ww.weaponDef) return false;

        var def = ww.weaponDef;
        int key = WeaponIdRegistry.GetKey(def.weaponId);

        var s = new WeaponSlotState
        {
            weaponKey = key,
            mag = (ushort)Mathf.Clamp(ww.magOnGround, 0, def.magSize),
            reserve = (ushort)Mathf.Clamp(ww.reserveOnGround, 0, def.maxReserve)
        };
        SetSlot(ActiveSlot, s);
        return true;
    }

    public bool TryDropActive()
    {
        // Chỉ StateAuthority mới được phép sửa state
        if (!HasStateAuthority) return false;

        // Lấy slot đang active và clear nó
        int idx = ActiveSlot;
        var s = GetSlot(idx);
        if (s.weaponKey == 0) return false; // đang tay không rồi

        // Xoá vũ khí (slot rỗng)
        s.weaponKey = 0;
        s.mag = 0;
        s.reserve = 0;
        SetSlot(idx, s);

        // Không nhất thiết phải đổi ActiveSlot ở đây.
        // Bridge phía client đã xử lý "nếu slot active bị clear thì unequip view".
        // (Giữ nguyên ActiveSlot để UI còn biết tay đang rỗng tại index đó.)
        return true;
    }


    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestDropActive(RpcInfo info = default)
    {
        Debug.Log($"[RPC_RequestDropActive] Received from client {info.Source}, HasStateAuth: {HasStateAuthority}, Object: {Object != null}");

        if (!HasStateAuthority)
        {
            Debug.LogError("[RPC_RequestDropActive] No State Authority!");
            return;
        }

        // gọi drop qua PlayerPickup ở cùng player
        var pickup = GetComponentInChildren<PlayerPickup>();
        if (pickup != null)
        {
            Debug.Log("[RPC_RequestDropActive] Calling ServerSpawnDropActive");
            pickup.ServerSpawnDropActive();
        }
        else
        {
            Debug.LogError("[RPC_RequestDropActive] Pickup component not found!");
        }
    }


    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestSetActiveSlot(int slot)
    {
        if (!HasStateAuthority) return;

        int clamped = Mathf.Clamp(slot, 0, Mathf.Max(1, SlotCount) - 1);
        var s = GetSlot(clamped);
        if (s.weaponKey == 0) return;

        SelectActiveSlot(clamped);
        /*        ActiveSlot = clamped; */  // StateAuthority cập nhật
                                            // Render() sẽ bắn OnActiveSlotChanged
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_RequestBuyFromShop(NetworkObject shopNo, RpcInfo info = default)
    {
        _shopRpcCounter++;
        int seq = _shopRpcCounter;

        Debug.Log($"[SHOP][HOST] RPC_RequestBuyFromShop #{seq} FROM={info.Source} " +
                  $"HasStateAuth={HasStateAuthority} ObjectValid={Object != null}");

        if (!HasStateAuthority)
        {
            Debug.LogError($"[SHOP][HOST] #{seq} FAIL: No StateAuthority!");
            return;
        }

        if (!shopNo)
        {
            Debug.LogWarning($"[SHOP][HOST] #{seq} FAIL: shop NetworkObject is null.");
            return;
        }

        var shop = shopNo.GetComponent<WeaponShopSpot>();
        if (!shop || !shop.weaponDef)
        {
            Debug.LogWarning($"[SHOP][HOST] #{seq} FAIL: WeaponShopSpot missing/invalid. shopNull={shop == null} " +
                             $"weaponDefNull={shop?.weaponDef == null}");
            return;
        }

        var points = GetComponentInChildren<PlayerPoints>();
        var pickup = GetComponentInChildren<PlayerPickup>();

        if (!points)
        {
            Debug.LogError($"[SHOP][HOST] #{seq} FAIL: PlayerPoints not found on player.");
            return;
        }

        // Check khoảng cách server-side
        Vector3 playerPos = transform.position;
        Vector3 shopPos = shop.transform.position;
        float dist = Vector3.Distance(playerPos, shopPos);

        Debug.Log($"[SHOP][HOST] #{seq} PRECHECK: shop='{shop.name}' dist={dist:F2} " +
                  $"interactRange={shop.interactRange}+0.5 canUse={shop.CanUse()} " +
                  $"pickupNull={(pickup == null)}");

        if (dist > shop.interactRange + 0.5f)
        {
            Debug.Log($"[SHOP][HOST] #{seq} FAIL: Out of range (dist={dist:F2}).");
            return;
        }

        bool ok = TryBuyFromShop(shop, points, pickup, gameObject);
        Debug.Log($"[SHOP][HOST] #{seq} TryBuyFromShop result = {ok}");

        if (ok)
        {
            var net = GetComponentInParent<FusionNetBridge>();
            if (net != null)
                net.RPC_BuySucceeded();
        }
    }


    public void SelectActiveSlot(int index)
    {
        if (!HasStateAuthority) return;

        if (NetIsReloading)
        {
            NetIsReloading = false;
            ReloadTimer = TickTimer.None;
            // Có thể bắn event nếu cần:
            TT.Observer.Instance?.NotifyWithData("weapon.reload.cancelled", (gameObject, ActiveSlot));
        }

        ActiveSlot = Mathf.Clamp(index, 0, Mathf.Max(1, SlotCount) - 1);
    }

    public int FindEmptySlot()
    {
        for (int i = 0; i < SlotCount; i++) if (GetSlot(i).IsEmpty) return i;
        return -1;
    }

    public int FindSlotByKey(int key)
    {
        for (int i = 0; i < SlotCount; i++) if (GetSlot(i).weaponKey == key) return i;
        return -1;
    }

    private void CheckSlot(int idx, WeaponSlotState curr, ref WeaponSlotState prev)
    {
        // nếu khác key/mag/reserve -> cập nhật
        if (curr.weaponKey != prev.weaponKey || curr.mag != prev.mag || curr.reserve != prev.reserve)
        {
            // ⬇️ nếu cùng weaponKey và mag giảm -> coi như bắn 1 viên
            if (curr.weaponKey != 0 && prev.weaponKey == curr.weaponKey && curr.mag < prev.mag)
            {
                var def = WeaponIdRegistry.GetDef(curr.weaponKey);
                TT.Observer.Instance?.NotifyWithData(
                    TT.WeaponTopics.Fired,
                    (gameObject, idx, def != null ? def.weaponId : null)
                );
            }

            prev = curr;
            OnSlotChanged?.Invoke(idx);
        }
    }


    public override void Render()
    {
        // Active slot
        if (ActiveSlot != _pActive)
        {
            _pActive = ActiveSlot;
            OnActiveSlotChanged?.Invoke(_pActive);
        }

        // Slot count (cho UI refresh toàn bộ)
        if (SlotCount != _pCount)
        {
            _pCount = SlotCount;
            OnSlotChanged?.Invoke(-1); // -1 = refresh all
        }

        // Detect reload state transition (start/finish) cho mọi máy
        if (_pReload != NetIsReloading)
        {
            bool was = _pReload;
            _pReload = NetIsReloading;

            if (_pReload)
            {
                // started
                var s = GetSlot(ActiveSlot);
                var def = WeaponIdRegistry.GetDef(s.weaponKey);
                TT.Observer.Instance?.NotifyWithData(
                    TT.WeaponTopics.ReloadStarted,
                    (gameObject, ActiveSlot, def != null ? def.weaponId : null)
                );
            }
            else
            {
                // finished
                TT.Observer.Instance?.NotifyWithData(
                    TT.WeaponTopics.ReloadFinished,
                    (gameObject, ActiveSlot)
                );
            }
        }


        // Từng slot
        CheckSlot(0, Slot0, ref _p0);
        CheckSlot(1, Slot1, ref _p1);
    }

    /// <summary>
    /// Logic mua súng/mua đạn từ shop trên HOST (StateAuthority).
    /// </summary>
    public bool TryBuyFromShop(WeaponShopSpot shop, PlayerPoints points, PlayerPickup pickup, GameObject owner)
    {
        if (!HasStateAuthority)
        {
            Debug.LogWarning("[MP Loadout] TryBuyFromShop called without StateAuthority.");
            return false;
        }

        if (!shop || !shop.weaponDef || points == null)
        {
            Debug.LogWarning($"[MP Loadout] TryBuyFromShop FAIL: bad args. " +
                             $"shopNull={shop == null} weaponDefNull={shop?.weaponDef == null} pointsNull={points == null}");
            return false;
        }

        if (!shop.CanUse())
        {
            Debug.Log("[MP Loadout] TryBuyFromShop FAIL: shop.CanUse() == false.");
            return false;
        }

        var def = shop.weaponDef;
        int key = WeaponIdRegistry.GetKey(def.weaponId);
        if (key == 0)
        {
            Debug.LogWarning("[MP Loadout] TryBuyFromShop: weaponKey = 0, weaponId=" + def.weaponId);
            return false;
        }

        Debug.Log($"[MP Loadout] TryBuyFromShop START: weapon='{def.weaponName}' key={key}");

        // Đã có khẩu này? → mua ammo
        int haveSlot = FindSlotByKey(key);
        if (haveSlot >= 0)
        {
            var s = GetSlot(haveSlot);
            int maxReserve = Mathf.Max(def.maxReserve, def.startReserve);

            Debug.Log($"[MP Loadout] Buy AMMO path: slot={haveSlot} reserve={s.reserve} maxReserve={maxReserve} " +
                      $"ammoCost={shop.ammoCost}");

            if (s.reserve >= maxReserve)
            {
                Debug.Log("[MP Loadout] Ammo already full, skip buy.");
                return false;
            }

            if (!points.TrySpend(shop.ammoCost, PointReason.Purchase, shop.gameObject))
            {
                Debug.Log("[MP Loadout] Not enough points for ammo.");
                return false;
            }

            s.reserve = (ushort)maxReserve;
            SetSlot(haveSlot, s);
            OnSlotChanged?.Invoke(haveSlot);
            shop.NotifyUsed();

            TT.Observer.Instance?.NotifyWithData(
                TT.WeaponTopics.ReloadFinished, // hoặc topic riêng nếu muốn
                (owner, haveSlot)
            );

            Debug.Log($"[MP Loadout] Buy AMMO SUCCESS: slot={haveSlot} newReserve={s.reserve}");
            return true;
        }

        // Chưa có khẩu này → mua súng
        Debug.Log($"[MP Loadout] Buy WEAPON path: weaponCost={shop.weaponCost}");

        if (!points.TrySpend(shop.weaponCost, PointReason.Purchase, shop.gameObject))
        {
            Debug.Log("[MP Loadout] Not enough points for weapon.");
            return false;
        }

        int targetSlot = FindEmptySlot();

        // Full slot → drop active bằng Pickup, rồi tìm lại
        if (targetSlot < 0)
        {
            Debug.Log("[MP Loadout] No empty slot, will drop active via pickup.");

            if (pickup != null)
            {
                Debug.Log("[MP Loadout] Calling pickup.ServerSpawnDropActive() before buy.");
                pickup.ServerSpawnDropActive();
            }
            else
            {
                Debug.LogWarning("[MP Loadout] pickup is null, cannot drop active weapon.");
            }

            targetSlot = FindEmptySlot();

            if (targetSlot < 0)
            {
                targetSlot = Mathf.Clamp(ActiveSlot, 0, Mathf.Max(1, SlotCount) - 1);
                Debug.Log($"[MP Loadout] Still no empty slot, fallback targetSlot={targetSlot} (ActiveSlot).");
            }
            else
            {
                Debug.Log($"[MP Loadout] After drop, found empty slot={targetSlot}.");
            }
        }

        var newState = new WeaponSlotState
        {
            weaponKey = key,
            mag = (ushort)def.magSize,
            reserve = (ushort)def.startReserve
        };

        SetSlot(targetSlot, newState);
        ActiveSlot = targetSlot;

        OnSlotChanged?.Invoke(targetSlot);
        OnActiveSlotChanged?.Invoke(targetSlot);
        shop.NotifyUsed();

        TT.Observer.Instance?.NotifyWithData(
            TT.WeaponTopics.Picked,
            (owner, def, targetSlot, newState.mag, newState.reserve)
        );

        Debug.Log($"[MP Loadout] Buy WEAPON SUCCESS: slot={targetSlot} mag={newState.mag} reserve={newState.reserve}");

        return true;
    }

    public bool TryFillMaxAmmoAll()
    {
        if (!HasStateAuthority) return false;

        bool changed = false;

        // Cancel reload
        if (NetIsReloading)
        {
            NetIsReloading = false;
            ReloadTimer = TickTimer.None;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            var s = GetSlot(i);
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
                SetSlot(i, s);
                changed = true;

                // UI/visual update
                OnSlotChanged?.Invoke(i);
            }
        }

        if (changed)
            OnActiveSlotChanged?.Invoke(ActiveSlot);

        return changed;
    }

}
#endif