using UnityEngine;
using Unity.Cinemachine;

public class CinemachineInputBlocker : MonoBehaviour
{
    CinemachineInputAxisController controller;

    private void Awake()
    {
        controller = GetComponent<CinemachineInputAxisController>();

        if (controller != null)
        {
            controller.ReadControlValueOverride = BlockedReader;
        }
    }

    private float BlockedReader(
        UnityEngine.InputSystem.InputAction action,
        IInputAxisOwner.AxisDescriptor.Hints hint,
        Object context,
        CinemachineInputAxisController.Reader.ControlValueReader defaultReader)
    {
        // Nếu block look → trả 0
        if (InputBlockerSystem.Has(InputBlocker.CameraLook) ||
            InputBlockerSystem.Has(InputBlocker.Full))
        {
            return 0f;
        }

        // Ngược lại đọc input bình thường
        return defaultReader(action, hint, context, null);
    }
}
