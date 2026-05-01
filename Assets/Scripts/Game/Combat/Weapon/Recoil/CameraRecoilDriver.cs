using UnityEngine;
using Unity.Cinemachine;

[DefaultExecutionOrder(9000)]

[DisallowMultipleComponent]
public class CameraRecoilDriver : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CinemachineCamera cmCam;       // New in CM 3
    [SerializeField] private CinemachinePanTilt panTilt;    // Replaces CinemachinePOV

    [Header("Tuning")]
    [Tooltip("How fast recoil returns to zero (deg/sec).")]
    public float returnSpeed = 12f;
    [Tooltip("How snappy the camera chases the target recoil offset.")]
    public float snappiness = 18f;

    [Tooltip("Clamp for tilt to avoid flipping. (Pan is usually Wrap, so no clamp)")]
    public Vector2 tiltClamp = new Vector2(-85f, 85f);

    // runtime
    Vector2 _target;   // desired recoil offset (yaw, pitch)
    Vector2 _current;  // smoothed offset
    Vector2 _applied;  // last offset applied to axes (for delta-apply, no drift)

    void Reset() { AutoWire(); }
    void Awake() { AutoWire(); }

    void AutoWire()
    {
        if (cmCam == null)
            cmCam = GetComponent<CinemachineCamera>()
                 ?? GetComponentInParent<CinemachineCamera>()
                 ?? GetComponentInChildren<CinemachineCamera>(true);

        if (panTilt == null)
        {
            if (cmCam != null)
                panTilt = cmCam.GetComponent<CinemachinePanTilt>()
                       ?? cmCam.GetComponentInChildren<CinemachinePanTilt>(true);
            if (panTilt == null)
                panTilt = GetComponent<CinemachinePanTilt>()
                       ?? GetComponentInParent<CinemachinePanTilt>()
                       ?? GetComponentInChildren<CinemachinePanTilt>(true);
        }
    }

    /// <summary>
    /// Add recoil (degrees). Positive yaw rotates RIGHT. Positive pitch rotates UP.
    /// Typically gun kick uses small right yaw and UP pitch (e.g., AddRecoil(0.5f, 3f)).
    /// </summary>
    public void AddRecoil(float yawDeg, float pitchDeg)
    {
        _target.x += yawDeg;
        _target.y += pitchDeg;
    }
    public void AddRecoil(Vector2 yawPitch) => AddRecoil(yawPitch.x, yawPitch.y);

    /// <summary> Hard reset (e.g., on respawn/equip change). </summary>
    public void Clear()
    {
        // remove any residual offset from axes
        if (panTilt != null)
        {
            panTilt.PanAxis.Value -= _applied.x;
            panTilt.TiltAxis.Value -= _applied.y;
        }
        _target = _current = _applied = Vector2.zero;
    }

    void OnDisable() => Clear();

    void LateUpdate()
    {
        if (panTilt == null) return;

        // decay target towards zero
        _target = Vector2.MoveTowards(_target, Vector2.zero, returnSpeed * Time.deltaTime);

        // critically-damped like lerp
        float k = 1f - Mathf.Exp(-snappiness * Time.deltaTime);
        _current = Vector2.Lerp(_current, _target, k);

        // delta to apply this frame (so we don't fight player input)
        Vector2 delta = _current - _applied;
        if (delta.sqrMagnitude > 0f)
        {
            // YAW (Pan) – usually Wrap on, so just add
            panTilt.PanAxis.Value += delta.x;

            // PITCH (Tilt) – clamp to safe range
            float newTilt = panTilt.TiltAxis.Value - delta.y;
            newTilt = Mathf.Clamp(newTilt, tiltClamp.x, tiltClamp.y);
            panTilt.TiltAxis.Value = newTilt;

            _applied += delta;
        }
    }

    public void SetRecoilOffsets(float yawDeg, float pitchDeg, bool instant = false)
    {
        Vector2 v = new Vector2(yawDeg, pitchDeg);

        if (!instant)
        {
            // Cho bộ lọc của driver làm việc (snappiness/returnSpeed)
            _target = v;
            return;
        }

        // Áp ngay: điều chỉnh trục theo delta so với _applied hiện tại, rồi đồng bộ trạng thái
        if (panTilt != null)
        {
            // Yaw (Pan) thường Wrap, cứ cộng delta
            panTilt.PanAxis.Value += (v.x - _applied.x);

            // Pitch (Tilt) nên clamp để an toàn
            float newTilt = panTilt.TiltAxis.Value - (v.y - _applied.y);
            panTilt.TiltAxis.Value = Mathf.Clamp(newTilt, tiltClamp.x, tiltClamp.y);
        }

        _applied = v;
        _current = v;
        _target = v;
    }
}
