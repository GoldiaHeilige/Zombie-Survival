// FusionWeaponDriver.cs
// MONO (không NetworkBehaviour). Được FusionNetBridge gọi mỗi tick.
// - Chỉ StateAuthority mới thực thi gameplay thật (NetBridge đảm nhiệm điều này).

using UnityEngine;
using TT;

[DisallowMultipleComponent]
public class FusionWeaponDriver : MonoBehaviour
{
    [AutoBindInParent][SerializeField] private WeaponController weapon;
    [AutoBindInParent][SerializeField] private PlayerWeaponBridge bridge; // <-- THÊM
    [AutoBindInParent][SerializeField] private PlayerPickup pickup;

    // Guard: 1 tick chỉ xử lý 1 lần
    private int _lastTick = int.MinValue;

    [AutoBindInParent] private FusionNetBridge _net;
    bool IsAuth => _net != null && _net.HasStateAuth;
    bool IsLocal => _net != null && _net.IsLocalOwner;

    float _localFxCooldown;

    bool _pendingSemi;
    float _pendingSemiTTL;


#if FUSION_WEAVER
    // NetBridge sẽ gọi hàm này mỗi tick
    public void NetworkTick(Fusion.NetworkRunner runner, int tick, float dt, PlayerInputData inp, bool fireEdge)
#else
    public void NetworkTick(int tick, float dt, PlayerInputData inp, bool fireEdge)
#endif
    {
        if (_lastTick == tick) return;
        _lastTick = tick;

        if (!weapon) weapon = GetComponentInChildren<WeaponController>(true);
        if (!bridge) bridge = GetComponentInChildren<PlayerWeaponBridge>(true); // <-- THÊM
        if (!pickup) pickup = GetComponentInChildren<PlayerPickup>(true);       // <-- THÊM
        if (!weapon) return;

        // ===== ADS =====
        // Cho phép client local set ADS view (cosmetic). An toàn ở cả IsAuth & IsLocal.
        if (weapon.def != null)
            weapon.SetADS(inp.ads);

        // ===== Reload =====
        // Server (StateAuthority) xử lý reload thật,
        // nhưng mọi máy local (host + client) đều nghe FP reload.
        if (weapon.def != null && inp.reload)
        {
            if (IsAuth)
                weapon.TryReload();
        }

        // ===== Fire =====
/*        if (weapon.def != null && IsAuth)
        {
            bool wantPulse = false, wantHeld = false;
            switch (weapon.def.fireMode)
            {
                case WeaponDef.FireMode.Semi: wantPulse = fireEdge; break;
                default: wantHeld = inp.fire; break;
            }
            if (wantPulse) weapon.Tick(true);
            else weapon.Tick(wantHeld);
        }*/

        // ===== Slot switching =====
        if (bridge != null)
        {
            bool blockSwitch = pickup != null && pickup.IsDroppingNow;
            if (!blockSwitch && inp.slot1) bridge.SelectSlot(0);
            if (!blockSwitch && inp.slot2) bridge.SelectSlot(1);
            if (!blockSwitch && inp.prev) bridge.SelectPrevSlot();
            if (!blockSwitch && inp.next) bridge.SelectNextSlot();
        }

        // ===== Pickup / Replace / Drop =====
        // ✅ CHO PHÉP CLIENT GỌI DROP (sẽ gửi RPC)
        if (pickup != null)
        {
            if (inp.replace) pickup.ConfirmReplaceIfPending();
            if (inp.drop)
            {
                Debug.Log($"[WeaponDriver] Drop input detected - IsAuth: {IsAuth}, IsLocal: {IsLocal}");
                pickup.DropActiveWeaponPublic();
            }
        }

        if (IsLocal && weapon != null && weapon.def != null)
        {
            // 1) Tick state machine, recoil, sway, cadence y như SP
            bool wantPulse = false, wantHeld = false;
            switch (weapon.def.fireMode)
            {
                case WeaponDef.FireMode.Semi:
                    wantPulse = fireEdge;
                    break;
                default:
                    wantHeld = inp.fire;
                    break;
            }

            if (inp.sprint)
            {
                // vẫn giảm TTL để pending tự hết
                if (weapon.def.fireMode == WeaponDef.FireMode.Semi && _pendingSemi)
                {
                    _pendingSemiTTL -= dt;
                    if (_pendingSemiTTL <= 0f) _pendingSemi = false;
                }
                return;
            }


            // ✅ Buffer click cho Semi (đặc biệt pump shotgun)
            // fireEdge chỉ tồn tại 1 tick -> nếu tick đó bị lock (Action/Reload/CD) thì sẽ "mất click"
            if (weapon.def.fireMode == WeaponDef.FireMode.Semi)
            {
                if (wantPulse)
                {
                    _pendingSemi = true;
                    _pendingSemiTTL = 0.18f; // 180ms đủ qua 1-2 tick/lag nhỏ
                }
                else
                {
                    _pendingSemiTTL -= dt;
                    if (_pendingSemiTTL <= 0f) _pendingSemi = false;
                }
            }

            bool fireRequested =
                (weapon.def.fireMode == WeaponDef.FireMode.Semi) ? _pendingSemi : wantHeld;


            // Chạy Tick() để update FSM + recoil predict
            //      weapon.Tick(fireRequested);

            // 2) Trừ FX cooldown
            _localFxCooldown -= dt;

            // 3) Chỉ gửi RPC khi local biết còn đạn
            bool shouldRequest =
                fireRequested
                && HasAmmoLocal()
                && !weapon.IsReloadingNow
                && _localFxCooldown <= 0f;      // ✅ THÊM: chỉ request khi local cadence ready


            if (shouldRequest && _net != null)
            {
                // tính interval trước để set cooldown/commit
                float fxInterval = (weapon.def.rpm > 0f) ? (60f / weapon.def.rpm) : 0.1f;

                Vector3 origin, dir;
                weapon.GetFireRay(out origin, out dir);

                _net.RPC_RequestFire(origin, dir, weapon.IsADS());

                // ✅ COMMIT local shot
                weapon.PlayLocalShotFXImmediate();
                _localFxCooldown = fxInterval;

                // ✅ chỉ clear pending khi đã commit local shot
                if (weapon.def.fireMode == WeaponDef.FireMode.Semi)
                    _pendingSemi = false;
            }


            return; // không cho client chạm code auth phía dưới
        }
    }

    public void BindWeapon(WeaponController newWeapon) => weapon = newWeapon;

    ILoadoutState GetLoadout()
    {
        var prov = GetComponentInParent<PlayerStateProvider>(true);
        return prov ? prov.Loadout : null;
    }

    bool HasAmmoLocal()
    {
        var load = GetLoadout();
        if (load == null) return true; // fallback: cho qua nếu không có loadout

        int slot = load.ActiveSlot;
        if (slot < 0 || slot >= load.SlotCount) return true;

        var s = load.GetSlot(slot);
        return s.mag > 0;
    }

}
