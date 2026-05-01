using UnityEngine;

[DefaultExecutionOrder(10000)] // kéo chạy muộn để chắc đã có Cinemachine update
public class CopyMainCamPose : MonoBehaviour
{
    [SerializeField] Camera mainCam;
    Camera self;

    public void Init(Camera main) => mainCam = main;

    void Awake() { self = GetComponent<Camera>(); }

    // Đồng bộ đúng trước khi camera này cull → không bị trễ frame
    void OnPreCull() { SyncFull(); }

    // Fallback trong Editor nếu OnPreCull không gọi vì tắt render, vv.
    void LateUpdate()
    {
        if (!self.enabled) return;
        SyncFull();
    }

    void SyncFull()
    {
        if (!mainCam) return;
        var mt = mainCam.transform;

        // Copy pose tức thời
        transform.position = mt.position;
        transform.rotation = mt.rotation;

        // Copy lens để ADS không lệch
        self.fieldOfView = mainCam.fieldOfView;
        self.orthographic = mainCam.orthographic;

        // Nếu dùng Physical Camera / URP advanced lens, có thể copy thêm:
        self.usePhysicalProperties = mainCam.usePhysicalProperties;
        if (self.usePhysicalProperties)
        {
            self.focalLength = mainCam.focalLength;
            self.sensorSize = mainCam.sensorSize;
            self.lensShift = mainCam.lensShift;
            self.gateFit = mainCam.gateFit;
        }
    }
}
