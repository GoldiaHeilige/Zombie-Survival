using UnityEngine;
using UnityEngine.Animations.Rigging;

public class UpperBodyAimRigDriver : MonoBehaviour
{
    [Header("Rig Targets")]
    [SerializeField] private Transform aimTarget;   // AimTarget trong UpperBodyAimRig
    [SerializeField] private Transform aimOrigin;   // Thường là Spine_02

    [Header("Look source override (local only)")]
    [SerializeField] private Transform lookSourceOverride;  // Camera hoặc CameraRoot nếu muốn override

    [Header("Auto-bind refs")]
    [AutoBindInParent, SerializeField] private PlayerRefs playerRefs;

#if FUSION_WEAVER
    [AutoBindInParent, SerializeField] private FusionNetBridge netBridge;
#endif

    [Header("Settings")]
    [SerializeField] private float distance = 2f;       // AimTarget cách ngực bao xa
    [SerializeField] private float smoothTime = 0.05f;  // Làm mượt di chuyển

    [Header("Rig weight (optional)")]
    [SerializeField] private Rig upperBodyRig;

    bool _aimEnabled = true;
    float _defaultRigWeight = 1f;

    private Vector3 _currentPos;
    private Vector3 _vel;
    private bool _initialized;

    /// <summary>
    /// Cho phép PlayerAppearance bind lại rig khi skin/model thay đổi.
    /// </summary>
    public void SetAimRig(Transform newOrigin, Transform newTarget)
    {
        aimOrigin = newOrigin;
        aimTarget = newTarget;
    }

    public void SetUpperBodyRig(Rig newRig)
    {
        upperBodyRig = newRig;

        if (upperBodyRig != null)
        {
            _defaultRigWeight = upperBodyRig.weight;
        }
    }

    void LateUpdate()
    {
        if (!_aimEnabled)
            return;

        if (!aimTarget || !aimOrigin)
            return;

#if FUSION_WEAVER
        if (netBridge && netBridge.Object && netBridge.Object.IsValid)
        {
            if (netBridge.Object.HasInputAuthority)
            {
                // Local player → dùng camera
                UpdateFromLocalCamera();
            }
            else
            {
                // Proxy → dùng viewYaw/viewPitch từ LastInput
                UpdateFromNetworkInput();
            }
        }
        else
        {
            // Không có Fusion (SP) → coi như local
            UpdateFromLocalCamera();
        }
#else
        // Build SP / no-Fusion → chỉ local
        UpdateFromLocalCamera();
#endif
    }

    // ====== LOCAL PLAYER: dùng camera ======

    void UpdateFromLocalCamera()
    {
        Transform src = lookSourceOverride;

        // Ưu tiên camera trong PlayerRefs rồi mới tới CameraBinder / Camera.main
        if (!src)
        {
            if (playerRefs && playerRefs.mainCam)
            {
                src = playerRefs.mainCam.transform;
            }
            else if (CameraBinder.Instance && CameraBinder.Instance.mainCam)
            {
                src = CameraBinder.Instance.mainCam.transform;
            }
            else if (Camera.main)
            {
                src = Camera.main.transform;
            }
        }

        if (!src)
            return;

        Vector3 dir = src.forward;
        MoveAimTarget(dir);
    }

#if FUSION_WEAVER
    // ====== PROXY: dùng NetViewYaw / NetViewPitch từ network ======
    void UpdateFromNetworkInput()
    {
        if (!netBridge)
            return;

        float yaw = netBridge.NetViewYaw;
        float pitch = netBridge.NetViewPitch;

        // Nếu muốn an toàn, có thể check thêm: nếu cả yaw/pitch = 0 mà player đang quay chỗ khác thì skip,
        // nhưng thường không cần.
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 dir = rot * Vector3.forward;

        MoveAimTarget(dir);
    }
#endif

    // ====== COMMON: di chuyển AimTarget theo hướng cho sẵn ======

    void MoveAimTarget(Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-6f)
            return;

        dir.Normalize();
        Vector3 desiredPos = aimOrigin.position + dir * distance;

        if (!_initialized)
        {
            _currentPos = desiredPos;
            _initialized = true;
        }

        _currentPos = Vector3.SmoothDamp(_currentPos, desiredPos, ref _vel, smoothTime);
        aimTarget.position = _currentPos;
    }

    public void SetAimEnabled(bool enabled, bool disableRig = true)
    {
        _aimEnabled = enabled;

        if (disableRig && upperBodyRig != null)
        {
            upperBodyRig.weight = enabled ? _defaultRigWeight : 0f;
        }

        if (enabled)
        {
            // cho lần bật lại sau revive mượt hơn
            _initialized = false;
        }
    }
}
