// SingleWeaponDriver.cs
// SINGLEPLAYER-ONLY
// Đọc InputHub.Snapshot và điều khiển vũ khí + bridge/pickup.
// - Fire/ADS/Reload: như trước
// - Thêm: đổi slot (Prev/Next/Slot1/Slot2) + Pickup/Replace/Drop

using UnityEngine;
using TT;

[DefaultExecutionOrder(5000)]
[DisallowMultipleComponent]
public sealed class SingleWeaponDriver : MonoBehaviour
{
    [Header("Bindings")]
    [Tooltip("WeaponController gắn cố định trên Player (def sẽ != null khi đã equip).")]
    [SerializeField] private WeaponController weapon;

    [Tooltip("Optional: Movement core để báo trạng thái ADS (chặn sprint).")]
    [SerializeField] private PlayerMovementController movement;

    [Tooltip("InputHub trên Player (local only).")]
    [SerializeField] private InputHub inputHub;

    [Tooltip("Cầu nối đổi/active slot vũ khí.")]
    [SerializeField] private PlayerWeaponBridge bridge;

    [Tooltip("Logic nhặt / replace / drop vũ khí.")]
    [SerializeField] private PlayerPickup pickup;

    [Header("Options")]
    [Tooltip("ADS dạng giữ (true) hay gạt (false).")]
    [SerializeField] private bool aimHold = true;

    [Tooltip("Tự chọn style bắn theo WeaponDef.fireMode (Semi=edge, Auto/Burst=hold).")]
    [SerializeField] private bool autoChooseFireStyleByDef = true;

    // Runtime
    bool _ads;                 // trạng thái ADS hiện tại (toggle)
    bool _semiPulseArmed;      // Semi bắn 1 frame theo edge
    bool _prevAimHeld;         // để tính edge cho toggle

    void Awake()
    {
        if (!weapon) weapon = GetComponentInChildren<WeaponController>(true);
        if (!movement) movement = GetComponentInParent<PlayerMovementController>();
        if (!inputHub) inputHub = FindFirstObjectByType<InputHub>(FindObjectsInactive.Exclude);
        if (!bridge) bridge = GetComponentInChildren<PlayerWeaponBridge>(true);
        if (!pickup) pickup = GetComponentInChildren<PlayerPickup>(true);

    }

    void Update()
    {
        if (!inputHub) return;
        var snap = inputHub.Current;

        // ===== ADS =====
        if (weapon && weapon.def != null)
        {
            bool aimHeld = snap.ADS;
            bool aimDown = aimHeld && !_prevAimHeld;

            _ads = aimHold ? aimHeld : (aimDown ? !_ads : _ads);

            weapon.SetADS(_ads);
            if (movement) movement.SetADSExternal(_ads);

            _prevAimHeld = aimHeld;
        }
        else
        {
            if (_ads)
            {
                _ads = false;
                if (movement) movement.SetADSExternal(false);
            }
            _prevAimHeld = false;
        }

        // ===== Reload =====
        if (weapon && weapon.def != null && snap.ReloadDown)
        {
            weapon.TryReload();
        }

        // ===== Fire =====
        if (weapon && weapon.def != null)
        {
            bool wantFirePulse = false;
            bool wantFireHeld = false;

            if (autoChooseFireStyleByDef)
            {
                switch (weapon.def.fireMode)
                {
                    case WeaponDef.FireMode.Semi: wantFirePulse = snap.FireDown; break;
                    case WeaponDef.FireMode.Auto:
                    case WeaponDef.FireMode.Burst:
                    default: wantFireHeld = snap.Fire; break;
                }
            }
            else
            {
                wantFireHeld = snap.Fire;
            }

            if (wantFirePulse) _semiPulseArmed = true;

            if (_semiPulseArmed)
            {        // bắn đúng 1 frame
                weapon.Tick(true);
                _semiPulseArmed = false;
            }
            else
            {
                weapon.Tick(wantFireHeld); // auto/burst
            }
        }

        // ===== Đổi slot (Prev/Next/1/2) =====
        if (bridge != null)
        {
            if (snap.Slot1Down) bridge.SelectSlot(0);
            if (snap.Slot2Down) bridge.SelectSlot(1);

            if (snap.PrevDown) bridge.SelectPrevSlot(); // wrapper bạn vừa thêm ở Bridge
            if (snap.NextDown) bridge.SelectNextSlot();
        }

        // ===== Pickup / Replace / Drop =====
        // ===== Pickup / Replace / Drop =====
        // ===== Pickup / Buy / Replace / Drop =====
        if (pickup != null)
        {
            // BUY = PaP -> RandomBox -> Shop
            if (snap.BuyDown)
            {
                if (!pickup.TryInteractPackAPunch() &&
                    !pickup.TryInteractRandomBox() &&
                    !pickup.TryInteractShop())
                {
                    // không làm gì nếu không có gì để buy
                }
            }

            // INTERACT = nhặt súng rơi, revive, v.v. (tuỳ bạn)
            if (snap.InteractDown)
            {
                pickup.TryPickupLooked();
            }

            if (snap.ReplaceDown) pickup.ConfirmReplaceIfPending();
            if (snap.DropDown) pickup.DropActiveWeaponPublic();
        }
    }

    /// <summary>Cho bridge gọi khi đổi slot/equip: bind lại WC (nếu cần).</summary>
    public void BindWeapon(WeaponController newWeapon)
    {
        weapon = newWeapon;
        _semiPulseArmed = false;
        if (!weapon || weapon.def == null)
        {
            _ads = false;
            if (movement) movement.SetADSExternal(false);
            _prevAimHeld = false;
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!movement) movement = GetComponentInParent<PlayerMovementController>();
    }
#endif
}
