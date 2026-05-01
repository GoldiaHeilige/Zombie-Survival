using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(9000)] // chạy sau Cinemachine Brain (thường LateUpdate)
public class ViewPitchFromCamera : MonoBehaviour
{
    [Header("Sources")]
    [SerializeField] Transform cameraTransform;   // auto-fill hoặc gọi BindToCamera() lúc spawn
    [SerializeField] Transform yawReference;      // node giữ yaw (thường là CameraRoot)

    [Header("Pitch options")]
    [SerializeField] bool clampPitch = true;
    [SerializeField] Vector2 pitchLimits = new Vector2(-89f, 89f);

    [Header("Auto-bind MainCamera")]
    [SerializeField] bool autoBindMainCam = true;
    [SerializeField] float rebindInterval = 0.25f;     // lặp tìm khi Main chưa có / bị thay
    [SerializeField] bool rebindOnSceneChange = true;  // đổi scene sẽ tự bind lại

    Coroutine rebindLoop;

    void Awake()
    {
        // đoán mặc định yawReference = parent nếu chưa set
        if (!yawReference && transform.parent) yawReference = transform.parent;
    }

    void OnEnable()
    {
        if (autoBindMainCam && rebindLoop == null)
            rebindLoop = StartCoroutine(RebindMainCamLoop());

        if (rebindOnSceneChange)
            SceneManager.activeSceneChanged += OnSceneChanged;
    }

    void OnDisable()
    {
        if (rebindLoop != null) { StopCoroutine(rebindLoop); rebindLoop = null; }
        if (rebindOnSceneChange)
            SceneManager.activeSceneChanged -= OnSceneChanged;
    }

    void OnSceneChanged(Scene a, Scene b)
    {
        // force rebind khi đổi scene
        if (autoBindMainCam)
        {
            cameraTransform = null;
            if (rebindLoop != null) StopCoroutine(rebindLoop);
            rebindLoop = StartCoroutine(RebindMainCamLoop());
        }
    }

    IEnumerator RebindMainCamLoop()
    {
        var wait = new WaitForSeconds(rebindInterval);
        while (enabled && (cameraTransform == null))
        {
            var cam = Camera.main; // Unity 6: MainCamera tag
            if (cam) cameraTransform = cam.transform;
            yield return wait;
        }
        rebindLoop = null;
    }

    /// <summary>Cho phép bind thủ công từ code spawn (ví dụ từ CameraBinder/Netcode)</summary>
    public void BindToCamera(Camera cam)
    {
        cameraTransform = cam ? cam.transform : null;
    }
    /// <summary>Hoặc bind thủ công bằng Transform</summary>
    public void BindToCamera(Transform camTransform)
    {
        cameraTransform = camTransform;
    }
    /// <summary>Đặt yaw reference (nếu rig thay đổi runtime)</summary>
    public void SetYawReference(Transform yawRef)
    {
        yawReference = yawRef;
    }

    void LateUpdate()
    {
        if (!cameraTransform || !yawReference) return;

        // Camera forward trong không gian của yawReference
        Vector3 f = Quaternion.Inverse(yawReference.rotation) * cameraTransform.forward;

        // f.y = sin(pitch). Dùng atan2 để ổn định gần ±90°
        float pitch = Mathf.Atan2(-f.y, new Vector2(f.x, f.z).magnitude) * Mathf.Rad2Deg;

        if (clampPitch) pitch = Mathf.Clamp(pitch, pitchLimits.x, pitchLimits.y);

        transform.position = cameraTransform.position;

        // Chỉ set local X, giữ Y/Z = 0 để không chồng yaw/roll
        var local = transform.localEulerAngles;
        local.x = pitch;
        local.y = 0f;
        local.z = 0f;
        transform.localEulerAngles = local;
    }

}
