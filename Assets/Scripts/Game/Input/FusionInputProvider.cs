using UnityEngine;
using UnityEngine.InputSystem;
using NIX.Core.DesignPatterns;
using UnityEngine.InputSystem.EnhancedTouch;

[RequireComponent(typeof(PlayerInput))]
public class FusionInputProvider : SingletonBehaviour<FusionInputProvider>
{
    PlayerInput _pi;

    protected override void Awake()
    {
        base.Awake();
        _pi = GetComponent<PlayerInput>();
        _pi.actions.Enable();

        if (_pi == null || _pi.actions == null)
        {
            Debug.LogError("[FusionInputProvider] Missing PlayerInput or Actions. Disabling.");
            enabled = false;
            return;
        }
    }

    public bool IsValid
    {
        get
        {
            if (_pi == null) return false;
            var asset = _pi.actions;
            if (asset == null) return false;
            return _pi.enabled && asset.enabled;
        }
    }

    public PlayerInputData GetInputData()
    {
        if (!_pi || _pi.actions == null)
            return default;

        bool blockMove = InputBlockerSystem.Has(InputBlocker.Movement) || InputBlockerSystem.Has(InputBlocker.Full);
        bool blockLook = InputBlockerSystem.Has(InputBlocker.CameraLook) || InputBlockerSystem.Has(InputBlocker.Full);
        bool blockCombat = InputBlockerSystem.Has(InputBlocker.Combat) || InputBlockerSystem.Has(InputBlocker.Full);
        bool blockInteract = InputBlockerSystem.Has(InputBlocker.Interaction) || InputBlockerSystem.Has(InputBlocker.Full);
        bool blockSlot = InputBlockerSystem.Has(InputBlocker.SlotSwap) || InputBlockerSystem.Has(InputBlocker.Full);

        bool adsHeld = !blockCombat && ADS;
        bool sprintHeld = !blockMove && Sprint;

        // ADS override sprint (giống SP)
        if (adsHeld) sprintHeld = false;

        return new PlayerInputData
        {
            move = blockMove ? Vector2.zero : Move,
            look = blockLook ? Vector2.zero : Look,

            sprint = !blockMove && sprintHeld,
            crouch = !blockMove && Crouch,
            jump = !blockMove && JumpDown,

            fire = !blockCombat && Fire,
            ads = !blockCombat && adsHeld,
            reload = !blockCombat && ReloadDown,

            interact = !blockInteract && InteractDown,
            buy = !blockInteract && BuyDown,

            replace = !blockCombat && ReplaceDown,
            drop = !blockCombat && DropDown,

            prev = !blockSlot && PrevDown,
            next = !blockSlot && NextDown,
            slot1 = !blockSlot && Slot1Down,
            slot2 = !blockSlot && Slot2Down,

            reviveHeld = ReviveHeld && !blockInteract,

            pause = OnPausePressed,

            viewYaw = ViewYaw,
            viewPitch = ViewPitch,

            spectatePrev = SpectatePrevDown,
            spectateNext = SpectateNextDown
        };
    }

    // ===== PROPERTIES =====

    public Vector2 Move =>
        _pi && _pi.actions != null ? _pi.actions["Move"]?.ReadValue<Vector2>() ?? Vector2.zero : Vector2.zero;

    public Vector2 Look =>
        _pi && _pi.actions != null ? _pi.actions["Look"]?.ReadValue<Vector2>() ?? Vector2.zero : Vector2.zero;

    public bool Sprint =>
        _pi && _pi.actions != null && _pi.actions["Sprint"]?.IsPressed() == true;

    public bool Fire =>
        _pi && _pi.actions != null && _pi.actions["Fire"]?.IsPressed() == true;

    public bool ADS =>
        _pi && _pi.actions != null && _pi.actions["ADS"]?.IsPressed() == true;

    public bool JumpDown =>
        _pi && _pi.actions != null && _pi.actions["Jump"]?.WasPressedThisFrame() == true;

    public bool ReloadDown =>
        _pi && _pi.actions != null && _pi.actions["Reload"]?.WasPressedThisFrame() == true;

    public bool InteractDown =>
        _pi && _pi.actions != null && _pi.actions["Interact"]?.WasPressedThisFrame() == true;

    public bool BuyDown =>
        _pi && _pi.actions != null && _pi.actions["Buy"]?.WasPressedThisFrame() == true;

    public bool PrevDown =>
        _pi && _pi.actions != null && _pi.actions["Previous"]?.WasPressedThisFrame() == true;

    public bool NextDown =>
        _pi && _pi.actions != null && _pi.actions["Next"]?.WasPressedThisFrame() == true;

    public bool Slot1Down =>
        _pi && _pi.actions != null && _pi.actions["Slot1"]?.WasPressedThisFrame() == true;

    public bool Slot2Down =>
        _pi && _pi.actions != null && _pi.actions["Slot2"]?.WasPressedThisFrame() == true;

    public bool ReplaceDown =>
        _pi && _pi.actions != null && _pi.actions["ReplaceWeapon"]?.WasPressedThisFrame() == true;

    public bool DropDown =>
        _pi && _pi.actions != null && _pi.actions["Drop"]?.WasPressedThisFrame() == true;

    public bool ReviveHeld =>
        _pi && _pi.actions != null && _pi.actions["Revive"]?.IsPressed() == true;

    public bool OnPausePressed =>
        _pi && _pi.actions != null && _pi.actions["PauseMenu"]?.WasPressedThisFrame() == true;

    public bool SpectateNextDown =>
        Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

    public bool SpectatePrevDown =>
        Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;

    public bool Crouch =>
    _pi && _pi.actions != null && _pi.actions["Crouch"]?.IsPressed() == true;

    public float ViewYaw
    {
        get
        {
            if (_pi != null && _pi.camera != null)
                return _pi.camera.transform.eulerAngles.y;

            var cam = Camera.main;
            return cam ? cam.transform.eulerAngles.y : 0f;
        }
    }

    public float ViewPitch
    {
        get
        {
            if (_pi != null && _pi.camera != null)
                return _pi.camera.transform.eulerAngles.x;

            var cam = Camera.main;
            return cam ? cam.transform.eulerAngles.x : 0f;
        }
    }

}
