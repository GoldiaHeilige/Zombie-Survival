using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(6000)]
[DisallowMultipleComponent]
public class SwayLookDeltaBridge : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("Component WeaponSwayBob cần nhận look delta.")]
    public WeaponSwayBob sway;

    [Header("Input")]
    [Tooltip("Action Vector2 (ví dụ: 'CM Default/Look') đang bound tới /Mouse/delta hoặc gamepad right stick.")]
    public InputActionReference look; // Vector2

    [Header("Tuning")]
    [Tooltip("Hệ số nhân cho look delta trước khi truyền vào sway (tuỳ DPI/feel).")]
    public float sensitivity = 1.0f;

    [Tooltip("Nhân với deltaTime (hữu ích nếu action trả giá trị 'per second'). Đa số để OFF với /Mouse/delta.")]
    public bool multiplyByDeltaTime = false;

    void OnEnable()
    {
        if (look != null) look.action.Enable();
    }

    void OnDisable()
    {
        if (look != null) look.action.Disable();
    }

    void LateUpdate()
    {
        if (sway == null) return;

        Vector2 v;

        if (GameSession.Mode == AppPlayMode.Single)
            v = InputHub.Instance?.Current.Look ?? Vector2.zero;
        else
            v = FusionInputProvider.Instance?.Look ?? Vector2.zero;

        v *= sensitivity;
        if (multiplyByDeltaTime) v *= Time.deltaTime;

        sway.SetLookDelta(v);
    }
}
