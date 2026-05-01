using UnityEngine;

public class PlayerRefs : MonoBehaviour
{
    [Header("Camera Anchors (set in prefab)")]
    public Transform camFollowTarget;

    [Tooltip("Anchor cho camera spectate / third-person khi downed.")]
    public Transform spectatorAnchor;

    [Header("Runtime refs (filled by binder)")]
    public Camera mainCam;
    public Camera weaponCam;
    public CameraADSFOVDriver fovDriver;

    [HideInInspector] public bool cameraReady;
    [HideInInspector] public bool cameraBinderInitialized = false;

    // ================== PlayerRegistry hook ==================

    void OnEnable()
    {
        PlayerRegistry.Register(this);
    }

    void OnDisable()
    {
        // OnDisable được gọi cả khi destroy, đủ dùng cho unregister
        PlayerRegistry.Unregister(this);
    }
}
