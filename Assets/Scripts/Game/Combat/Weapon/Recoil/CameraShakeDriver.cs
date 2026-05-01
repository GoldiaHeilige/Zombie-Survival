using UnityEngine;

/// <summary>
/// Driver trung tâm cho mọi loại camera shake / bob.
/// Hiện tại mới chỉ làm movement bob (idle / walk / sprint / crouch / ADS).
/// Sau này có thể add thêm recoil, explosion, v.v. vào cùng Offset.
/// 
/// Ý tưởng:
/// - Cinemachine xử lý aim/look như bình thường.
/// - Script này được gắn lên một transform "shake root" (thường là con của camera rig),
///   và chỉ thay đổi localPosition (và sau này có thể là localRotation) để tạo bob.
/// </summary>
public class CameraShakeDriver : MonoBehaviour
{
    [Header("Bindings")]
    [Tooltip("Transform sẽ được apply offset bob/shake. Để trống = chính transform này.")]
    [SerializeField] private Transform shakeTarget;

    [Header("Movement Bob")]
    [SerializeField] private bool enableMovementBob = true;

    [System.Serializable]
    public struct MovementBobSettings
    {
        [Header("Amplitude (độ mạnh lắc)")]
        [Tooltip("Độ lắc khi idle (đứng yên).")]
        public float idleAmplitude;

        [Tooltip("Độ lắc khi đi bộ / chạy bình thường.")]
        public float moveAmplitude;

        [Tooltip("Độ lắc khi sprint (chạy nước rút).")]
        public float sprintAmplitude;

        [Tooltip("Giảm amplitude nếu đang crouch.")]
        public float crouchMultiplier;

        [Tooltip("Giảm amplitude khi ADS (ngắm).")]
        public float adsMultiplier;

        [Header("Tần số nhịp bước")]
        [Tooltip("Base frequency cho bước chân (Hz).")]
        public float baseFrequency;

        [Tooltip("Multiplier cho idle (thường < 1).")]
        public float idleFrequencyMultiplier;

        [Tooltip("Multiplier cho sprint (> 1).")]
        public float sprintFrequencyMultiplier;

        [Header("Phân bố theo trục X/Y")]
        [Tooltip("X = trái/phải, Y = lên/xuống (0..1).")]
        public Vector2 axisMultiplier;

        [Header("Smooth")]
        [Tooltip("Tốc độ lerp khi chuyển amplitude (bật/tắt bob, chuyển state).")]
        public float amplitudeLerpSpeed;
    }

    [SerializeField]
    private MovementBobSettings movementBob = new MovementBobSettings
    {
        idleAmplitude = 0.005f,
        moveAmplitude = 0.02f,
        sprintAmplitude = 0.03f,
        crouchMultiplier = 0.5f,
        adsMultiplier = 0.3f,

        baseFrequency = 1.8f,
        idleFrequencyMultiplier = 0.5f,
        sprintFrequencyMultiplier = 1.4f,

        axisMultiplier = new Vector2(0.6f, 1.0f),
        amplitudeLerpSpeed = 10f
    };

    // base pose (không bob), lấy từ shakeTarget khi Awake
    Vector3 _baseLocalPos;
    Quaternion _baseLocalRot;

    // các offset/buffer nội bộ
    float _bobPhase;
    float _currentAmplitude;
    Vector3 _movementOffset;

    // dự phòng cho các hiệu ứng khác sau này (recoil, explosion…)
    Vector3 _eventOffsetPos;
    Vector3 _eventOffsetRotEuler;

    void Awake()
    {
        if (!shakeTarget)
            shakeTarget = transform;

        _baseLocalPos = shakeTarget.localPosition;
        _baseLocalRot = shakeTarget.localRotation;
    }

    void LateUpdate()
    {
        if (!Application.isPlaying || shakeTarget == null)
            return;

        float dt = Time.deltaTime;

        // 1) Movement bob
        if (enableMovementBob)
        {
            UpdateMovementBob(dt);
        }
        else
        {
            // tắt movement bob → fade amplitude về 0
            _currentAmplitude = Mathf.MoveTowards(_currentAmplitude, 0f, movementBob.amplitudeLerpSpeed * dt);
            _movementOffset = Vector3.zero;
        }

        // 2) Tính tổng offset pos/rot (hiện tại chỉ có movement + event placeholders)
        Vector3 finalPos = _baseLocalPos + _movementOffset + _eventOffsetPos;

        // Rotation: hiện giờ chưa dùng cho movement; để base + event (sau này).
        Quaternion finalRot = _baseLocalRot * Quaternion.Euler(_eventOffsetRotEuler);

        // 3) Apply lên target
        shakeTarget.localPosition = finalPos;
        shakeTarget.localRotation = finalRot;
    }

    /// <summary>
    /// Reset mọi offset về 0 (khi respawn, teleport, đổi camera rig, v.v.).
    /// </summary>
    public void ResetOffsets()
    {
        _bobPhase = 0f;
        _currentAmplitude = 0f;
        _movementOffset = Vector3.zero;
        _eventOffsetPos = Vector3.zero;
        _eventOffsetRotEuler = Vector3.zero;

        if (shakeTarget)
        {
            shakeTarget.localPosition = _baseLocalPos;
            shakeTarget.localRotation = _baseLocalRot;
        }
    }

    /// <summary>
    /// Movement bob: dựa trên input hiện tại (InputHub) để sinh offset trái/phải + lên/xuống.
    /// Không dùng noise, chỉ sin/cos có nhịp (pattern rõ ràng).
    /// </summary>
    void UpdateMovementBob(float dt)
    {
        // Nếu đang block Full/UI → không bob
        bool hardBlocked =
            InputBlockerSystem.Has(InputBlocker.Full) ||
            InputBlockerSystem.Has(InputBlocker.UIOnly);

        float moveMagnitude = 0f;
        bool isSprinting = false;
        bool isCrouching = false;
        bool isADS = false;

        if (!hardBlocked && InputHub.Instance != null)
        {
            var snap = InputHub.Instance.Current;
            moveMagnitude = Mathf.Clamp01(snap.Move.magnitude);   // 0..1
            isSprinting = snap.Sprint;
            isCrouching = snap.Crouch;
            isADS = snap.ADS;
        }

        // 1) Tính amplitude target
        float targetAmp;
        float freqMultiplier;

        if (moveMagnitude <= 0.01f)
        {
            // Idle
            targetAmp = movementBob.idleAmplitude;
            freqMultiplier = movementBob.idleFrequencyMultiplier;
        }
        else
        {
            if (isSprinting)
            {
                targetAmp = movementBob.sprintAmplitude;
                freqMultiplier = movementBob.sprintFrequencyMultiplier;
            }
            else
            {
                targetAmp = movementBob.moveAmplitude;
                freqMultiplier = 1f;
            }

            // scale thêm theo độ mạnh input Move (đi nhẹ vs full stick/WSAD)
            targetAmp *= Mathf.Clamp01(moveMagnitude);
        }

        if (isCrouching)
            targetAmp *= movementBob.crouchMultiplier;

        if (isADS)
            targetAmp *= movementBob.adsMultiplier;

        if (hardBlocked)
            targetAmp = 0f;

        // 2) Lerp amplitude hiện tại về target (cho smooth)
        _currentAmplitude = Mathf.Lerp(
            _currentAmplitude,
            targetAmp,
            1f - Mathf.Exp(-movementBob.amplitudeLerpSpeed * dt)
        );

        if (_currentAmplitude <= 0.0001f)
        {
            _movementOffset = Vector3.zero;
            return;
        }

        // 3) Tăng phase theo thời gian & frequency
        float freq = movementBob.baseFrequency * freqMultiplier;
        _bobPhase += dt * freq * Mathf.PI * 2f; // chuyển Hz → rad/s

        if (_bobPhase > Mathf.PI * 2f)
            _bobPhase -= Mathf.PI * 2f;

        // 4) Tạo offset:
        //    - Y: nhún lên/xuống (sin 1x)
        //    - X: lắc trái/phải (sin với pha khác để giả lập bước trái/phải)
        float sin1 = Mathf.Sin(_bobPhase);
        float sin2 = Mathf.Sin(_bobPhase * 2f);

        float offsetY = sin1 * _currentAmplitude * movementBob.axisMultiplier.y;
        float offsetX = sin2 * _currentAmplitude * movementBob.axisMultiplier.x;

        _movementOffset = new Vector3(offsetX, offsetY, 0f);
    }

    // =========================
    // Các API cho event shake sau này (recoil, explosion...)
    // =========================

    /// <summary>
    /// Thêm một offset event theo local position (dùng cho các effect ngắn, ví dụ nổ).
    /// Hạn chế: hiện mới là direct add; sau này có thể thêm hệ thống queue/decay.
    /// </summary>
    public void AddEventOffset(Vector3 localPosOffset)
    {
        _eventOffsetPos += localPosOffset;
    }

    /// <summary>
    /// Thêm một offset event theo local rotation Euler (deg).
    /// </summary>
    public void AddEventRotation(Vector3 localEulerOffset)
    {
        _eventOffsetRotEuler += localEulerOffset;
    }
}
