using UnityEngine;

[DefaultExecutionOrder(8050)] // chạy trước WeaponSwayBob (8100)
public class SwayMovementBridge : MonoBehaviour
{
    [Header("Targets")]
    public WeaponSwayBob sway;                    // Sway rig trong scene
    public PlayerMovementController movement;     // Player movement (spawn)

    [Header("Tuning")]
    [Tooltip("Nhân velocity trước khi feed vào sway (phóng đại / giảm).")]
    public float velocityScale = 1f;

    /// <summary>Cho CameraBinder gọi để bind movement từ player local.</summary>
    public void BindPlayer(PlayerMovementController ctrl)
    {
        movement = ctrl;
    }

    void LateUpdate()
    {
        if (sway == null || movement == null)
            return;

        // Chỉ cần planar velocity cho sway/bob
        Vector3 v = movement.WorldVelocity;
        v *= velocityScale;

        sway.SetExternalVelocity(v);

        // Nếu bạn muốn sway biết player đang sprint hay không:
        try
        {
            bool isSprint = movement.state == PlayerMovementController.MoveState.Sprinting;
            sway.SetIsSprinting(isSprint);
        }
        catch
        {
            // nếu enum khác thì bỏ qua
        }
    }
}
