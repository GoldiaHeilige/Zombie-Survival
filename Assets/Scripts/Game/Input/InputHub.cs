using UnityEngine;
using UnityEngine.InputSystem;
using NIX.Core.DesignPatterns;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerInput))]
public class InputHub : SingletonBehaviour<InputHub>
{
    private Snapshot _lastSnapshot;

    public struct Snapshot
    {
        public Vector2 Move;
        public Vector2 Look;
        public bool Sprint;

        public bool Fire;
        public bool FireDown;
        public bool FireUp;

        public bool ADS;
        public bool Jump;
        public bool JumpDown;
        public bool JumpUp;

        public bool ReloadDown;
        public bool InteractDown;
        public bool ReviveHeld;
        public bool BuyDown;

        public bool PrevDown;
        public bool NextDown;
        public bool Slot1Down;
        public bool Slot2Down;

        public bool ReplaceDown;
        public bool DropDown;

        public bool Crouch;
        public bool CrouchDown;

        public bool OnPausePressed;

        public float ViewYaw;
    }

    PlayerInput _pi;
    InputAction _move, _look, _sprint, _fire, _ads, _jump, _reload, _interact, _buy, _prev, _next, _slot1, _slot2, _replace, _drop, _crouch, _revive, _pause;

    bool _firePrev, _jumpPrev;

    public Snapshot Current { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        _pi = GetComponent<PlayerInput>();
        var a = _pi.actions;

        _move = a["Move"];
        _look = a["Look"];
        _sprint = a["Sprint"];
        _crouch = a["Crouch"];
        _fire = a["Fire"];
        _ads = a["ADS"];
        _jump = a["Jump"];
        _reload = a["Reload"];
        _interact = a["Interact"];
        _buy = a["Buy"];
        _prev = a["Previous"];
        _next = a["Next"];
        _slot1 = a["Slot1"];
        _slot2 = a["Slot2"];
        _replace = a["ReplaceWeapon"];
        _drop = a["Drop"];
        _revive = a["Revive"];
        _pause = a["PauseMenu"];
    }

    void Update()
    {
        bool blockMove = InputBlockerSystem.Has(InputBlocker.Movement) || InputBlockerSystem.Has(InputBlocker.Full);
        bool blockLook = InputBlockerSystem.Has(InputBlocker.CameraLook) || InputBlockerSystem.Has(InputBlocker.Full);
        bool blockCombat = InputBlockerSystem.Has(InputBlocker.Combat) || InputBlockerSystem.Has(InputBlocker.Full);
        bool blockInteract = InputBlockerSystem.Has(InputBlocker.Interaction) || InputBlockerSystem.Has(InputBlocker.Full);
        bool blockSlot = InputBlockerSystem.Has(InputBlocker.SlotSwap) || InputBlockerSystem.Has(InputBlocker.Full);

        var snap = new Snapshot();

        snap.Move = blockMove ? Vector2.zero : _move.ReadValue<Vector2>();
        snap.Look = blockLook ? Vector2.zero : _look.ReadValue<Vector2>();

        snap.Sprint = !blockMove && _sprint.IsPressed();

        bool fireNow = !blockCombat && (_fire?.IsPressed() ?? false);
        snap.Fire = fireNow;
        snap.FireDown = !blockCombat && (_fire?.WasPressedThisFrame() ?? false);
        snap.FireUp = (_firePrev && !fireNow);
        _firePrev = fireNow;

        bool jumpNow = !blockMove && (_jump?.IsPressed() ?? false);
        snap.Jump = jumpNow;
        snap.JumpDown = !blockMove && (_jump?.WasPressedThisFrame() ?? false);
        snap.JumpUp = _jumpPrev && !jumpNow;
        _jumpPrev = jumpNow;

        snap.ADS = !blockCombat && (_ads?.IsPressed() ?? false);
        snap.ReloadDown = !blockCombat && (_reload?.WasPressedThisFrame() ?? false);

        snap.InteractDown = !blockInteract && (_interact?.WasPressedThisFrame() ?? false);
        snap.BuyDown = !blockInteract && (_buy?.WasPressedThisFrame() ?? false);

        snap.PrevDown = !blockSlot && (_prev?.WasPressedThisFrame() ?? false);
        snap.NextDown = !blockSlot && (_next?.WasPressedThisFrame() ?? false);
        snap.Slot1Down = !blockSlot && (_slot1?.WasPressedThisFrame() ?? false);
        snap.Slot2Down = !blockSlot && (_slot2?.WasPressedThisFrame() ?? false);

        snap.ReplaceDown = !blockCombat && (_replace?.WasPressedThisFrame() ?? false);
        snap.DropDown = !blockCombat && (_drop?.WasPressedThisFrame() ?? false);

        snap.Crouch = !blockMove && (_crouch?.IsPressed() ?? false);
        snap.CrouchDown = !blockMove && (_crouch?.WasPressedThisFrame() ?? false);

        snap.OnPausePressed = _pause?.WasPressedThisFrame() ?? false;

        snap.ViewYaw = _pi.camera != null
            ? _pi.camera.transform.eulerAngles.y
            : (Camera.main ? Camera.main.transform.eulerAngles.y : 0f);

        Current = snap;
        _lastSnapshot = snap;
    }

    public Snapshot GetSnapshotForTick() => _lastSnapshot;
}
