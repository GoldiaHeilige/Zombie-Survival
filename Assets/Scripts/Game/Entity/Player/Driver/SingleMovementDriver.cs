using UnityEngine;

[DisallowMultipleComponent]
public class SingleMovementDriver : MonoBehaviour
{
    [AutoBindInParent][SerializeField] private PlayerMovementController movement;

    void OnEnable()
    {
        if (movement) movement.drivenExternally = true;
        SimTime.onTick += TickUpdate;
    }

    void OnDisable()
    {
        SimTime.onTick -= TickUpdate;
        if (movement) movement.drivenExternally = false;
    }

    void TickUpdate()
    {
        if (!movement) return;

        // SP input lấy từ InputHub
        var bus = InputHub.Instance;
        if (!bus) return;

        var snap = bus.Current;

        movement.SetInput(
            snap.Move,
            snap.Sprint,
            snap.Crouch,
            snap.JumpDown,
            snap.ViewYaw
        );

        // SimTime.Delta = 1/64s
        movement.Simulate(SimTime.Delta);
    }
}
