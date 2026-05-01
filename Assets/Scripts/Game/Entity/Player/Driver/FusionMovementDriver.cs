using UnityEngine;
using static Unity.Collections.Unicode;

[DisallowMultipleComponent]
public class FusionMovementDriver : MonoBehaviour
{
    [AutoBindInParent][SerializeField] private PlayerMovementController movement;

    // Guard: 1 tick chỉ simulate đúng 1 lần
    private int _lastTick = int.MinValue;


#if FUSION_WEAVER
    public void NetworkTick(Fusion.NetworkRunner runner, int tick, float dt, PlayerInputData inp)
    {
        if (!movement) return;

        // Chỉ chống double-call ở normal sim. Resim phải cho chạy lại.
        if (!runner.IsResimulation)
        {
            if (_lastTick == tick) return;
            _lastTick = tick;
        }

        movement.SetInput(inp.move, inp.sprint, inp.crouch, inp.jump, inp.viewYaw);
        movement.Simulate(dt);
    }
#endif

}
