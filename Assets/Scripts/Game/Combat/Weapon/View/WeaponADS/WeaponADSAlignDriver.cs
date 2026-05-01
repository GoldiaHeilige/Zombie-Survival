// … các using cũ …
using System.Collections;
using UnityEngine;

[DefaultExecutionOrder(8000)]
[DisallowMultipleComponent]
public class WeaponADSAlignDriver : MonoBehaviour
{
    [Header("Provider (đọc ADSWeight)")]
    [SerializeField] private WeaponViewController provider;

    // ⬇️ GIỮ field này nhưng sẽ được gán runtime, không cần kéo tay
    [SerializeField] Transform aimCamera;

    [Header("Transforms")]
    [SerializeField] private Transform adsAlign;   // node động (mặc định = this)
    [SerializeField] private Transform refHip;     // mốc hip (static)
    [SerializeField] private Transform refADS;     // sẽ trỏ tới ANCHOR tĩnh

    [Header("Options")]
    [SerializeField] private bool runInLateUpdate = true;
    Transform _adsAnchor;

    // ⬇️ NEW
    PlayerRefs _refs;
    Transform _lastSightAim; // lưu lại tham chiếu sightAim gần nhất (nếu BindADSRef được gọi sớm)
                             // Lưu lại local base của refHip để cộng offset từ WeaponDef
    Vector3 _hipBaseLocalPos;
    Quaternion _hipBaseLocalRot;

    void Awake()
    {
        if (adsAlign == null) adsAlign = transform;
        if (provider == null) provider = GetComponentInParent<WeaponViewController>(true);

        // ⬇️ NEW: cache PlayerRefs
        _refs = GetComponentInParent<PlayerRefs>();
        if (refHip != null)
        {
            _hipBaseLocalPos = refHip.localPosition;
            _hipBaseLocalRot = refHip.localRotation;
        }
    }

    // ⬇️ NEW: đợi cameraReady rồi tự bind aimCamera
    IEnumerator Start()
    {
        if (_refs != null)
        {
            yield return new WaitUntil(() => _refs.cameraReady);

            // ưu tiên WeaponCam, thiếu thì dùng MainCam
            if (_refs.weaponCam) aimCamera = _refs.weaponCam.transform;
            else if (_refs.mainCam) aimCamera = _refs.mainCam.transform;

            // Nếu trước đó đã BindADSRef(sightAim) nhưng aimCamera chưa sẵn sàng,
            // thì khi aimCamera có rồi ta tính lại anchor để ADS khớp đúng camera.
            if (_lastSightAim != null)
                BindADSRef(_lastSightAim);
        }
    }

    void Update() { if (!runInLateUpdate) Drive(); }
    void LateUpdate() { if (runInLateUpdate) Drive(); }

    public void BindADSRef(Transform sightAim)
    {
        // ⬇️ NEW: nhớ sightAim để có thể rebind sau khi camera sẵn sàng
        _lastSightAim = sightAim;

        if (adsAlign == null || sightAim == null) { refADS = refHip; return; }

        var parent = adsAlign.parent;
        if (_adsAnchor == null)
        {
            _adsAnchor = new GameObject("ADSRefAnchor").transform;
            if (parent) _adsAnchor.SetParent(parent, worldPositionStays: true);
        }

        // —— Ma trận tính anchor như bạn đang dùng, giữ nguyên —— 
        Matrix4x4 M_parent = adsAlign.worldToLocalMatrix;
        Matrix4x4 M_childW = Matrix4x4.TRS(sightAim.position, sightAim.rotation, Vector3.one);

        // ⬇️ CHỈ THAY: đảm bảo dùng aimCamera (đã được auto-bind ở Start)
        var camTr = aimCamera != null ? aimCamera : (Camera.main ? Camera.main.transform : null);
        if (camTr == null) { refADS = refHip; return; }

        Matrix4x4 M_camW = Matrix4x4.TRS(camTr.position, camTr.rotation, Vector3.one);
        Matrix4x4 M_parent_to_child_local = M_parent * M_childW;
        Matrix4x4 M_parent_targetW = M_camW * M_parent_to_child_local.inverse;

        _adsAnchor.position = M_parent_targetW.GetColumn(3);
        _adsAnchor.rotation = Quaternion.LookRotation(M_parent_targetW.GetColumn(2), M_parent_targetW.GetColumn(1));
        refADS = _adsAnchor;
    }

    /// <summary>
    /// Áp offset HIP cho refHip từ WeaponDef (FP).
    /// Nếu def = null → reset về base ban đầu.
    /// </summary>
    public void ApplyHipFromDef(WeaponDef def)
    {
        if (refHip == null)
            return;

        if (def == null)
        {
            // reset về base
            refHip.localPosition = _hipBaseLocalPos;
            refHip.localRotation = _hipBaseLocalRot;
            return;
        }

        // Offset local tính từ base
        refHip.localPosition = _hipBaseLocalPos + def.fpHipPositionOffset;
        refHip.localRotation = _hipBaseLocalRot * Quaternion.Euler(def.fpHipRotationOffsetEuler);
    }

    /// <summary>
    /// Reset HIP ref về base, dùng khi không có súng.
    /// </summary>
    public void ResetHipToBase()
    {
        if (refHip == null)
            return;

/*        // Log stack trace để biết AI gọi hàm này
        var stackTrace = new System.Diagnostics.StackTrace(1, true);
        var frame = stackTrace.GetFrame(0);
        var callerMethod = frame.GetMethod();
        var callerType = callerMethod.DeclaringType;

        Debug.Log($"[WeaponADSAlignDriver] ResetHipToBase called by: {callerType.Name}.{callerMethod.Name} " +
                  $"at {frame.GetFileName()}:{frame.GetFileLineNumber()}");*/

        // Log thêm context
        if (provider != null && provider.gameObject != null)
        {
            var playerRefs = provider.GetComponentInParent<PlayerRefs>();
            if (playerRefs != null)
            {
                Debug.Log($"[WeaponADSAlignDriver] Associated player: {playerRefs.gameObject.name}, IsLocalOwner: {IsLocalPlayer(playerRefs)}");
            }
        }

        refHip.localPosition = _hipBaseLocalPos;
        refHip.localRotation = _hipBaseLocalRot;
    }

    private bool IsLocalPlayer(PlayerRefs refs)
    {
#if FUSION_WEAVER
        var netBridge = refs.GetComponentInParent<FusionNetBridge>();
        return netBridge != null && netBridge.IsLocalOwner;
#else
    return true;
#endif
    }


    void Drive()
    {
        if (adsAlign == null || refHip == null) return;
        if (refADS == null) refADS = refHip;

        float w = provider != null ? provider.ADSWeight : 0f;

        var parent = adsAlign.parent;
        Vector3 hipPosLocal = parent ? parent.InverseTransformPoint(refHip.position) : refHip.position;
        Vector3 adsPosLocal = parent ? parent.InverseTransformPoint(refADS.position) : refADS.position;

        Quaternion hipRotLocal = parent ? Quaternion.Inverse(parent.rotation) * refHip.rotation : refHip.rotation;
        Quaternion adsRotLocal = parent ? Quaternion.Inverse(parent.rotation) * refADS.rotation : refADS.rotation;

        adsAlign.localPosition = Vector3.Lerp(hipPosLocal, adsPosLocal, w);
        adsAlign.localRotation = Quaternion.Slerp(hipRotLocal, adsRotLocal, w);
    }
}
