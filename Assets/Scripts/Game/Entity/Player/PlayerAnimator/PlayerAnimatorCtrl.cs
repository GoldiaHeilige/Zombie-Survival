using UnityEngine;
#if FUSION_WEAVER
using Fusion;
#endif

[DisallowMultipleComponent]
public class PlayerAnimatorCtrl : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [AutoBindInParent, SerializeField]
    private PlayerStateProvider stateProvider;

    [Tooltip("Transform dùng để đo vận tốc (nếu không dùng CharacterController). Để trống = transform của Player.")]
    [SerializeField] private Transform velocitySource;

    [Header("Animator Params")]
    [SerializeField] private string speedParam = "Speed";
    [SerializeField] private string locomotionStateParam = "LocomotionState";
    [SerializeField] private string groundedParam = "Grounded";
    [SerializeField] private string sprintParam = "Sprinting";
    [SerializeField] private string crouchParam = "Crouch";
    [SerializeField] private string stunnedParam = "Stunned";
    [SerializeField] private string moveXParam = "MoveX";
    [SerializeField] private string moveYParam = "MoveY";

    [Header("Smoothing")]
    [SerializeField] private float speedSmoothTime = 0.15f;
    [SerializeField] private float dirSmoothTime = 0.1f;

    [Tooltip("Dùng để chuẩn hoá MoveX/MoveY về [-1,1]. Đặt gần max speed chạy.")]
    [SerializeField] private float maxDirectionalSpeed = 7.5f;

    int _hashSpeed, _hashLocomotionState, _hashGrounded, _hashCrouch, _hashStunned, _hashMoveX, _hashMoveY, _hashSprint;

    IMovementState _movementState;
    CharacterController _cc;

    bool _hasLastPos;
    Vector3 _lastPos;
    float _currentSpeed;
    float _currentMoveX, _currentMoveY;

    void Awake()
    {
        if (!animator)
            animator = GetComponent<Animator>();

        if (!stateProvider)
            stateProvider = GetComponentInParent<PlayerStateProvider>(true);

        if (!velocitySource && stateProvider)
            velocitySource = stateProvider.transform;

        _cc = GetComponentInParent<CharacterController>();

        _hashSpeed = Animator.StringToHash(speedParam);
        _hashLocomotionState = Animator.StringToHash(locomotionStateParam);
        _hashGrounded = Animator.StringToHash(groundedParam);
        _hashCrouch = Animator.StringToHash(crouchParam);
        _hashSprint = Animator.StringToHash(sprintParam);
        _hashStunned = Animator.StringToHash(stunnedParam);
        _hashMoveX = Animator.StringToHash(moveXParam);
        _hashMoveY = Animator.StringToHash(moveYParam);
    }

    void OnEnable()
    {
        TryBindMovementState();
    }

    void OnDisable()
    {
        if (_movementState != null)
        {
            _movementState.OnStateChanged -= OnMovementStateChanged;
            _movementState = null;
        }
    }

    /// <summary>
    /// Được gọi bởi PlayerAppearance khi skin (và Animator) thay đổi.
    /// </summary>
    public void SetAnimator(Animator newAnimator)
    {
        animator = newAnimator;
    }


    void Update()
    {
        if (!animator) return;

        if (_movementState == null)
            TryBindMovementState();

#if FUSION_WEAVER
        if (_movementState is NetworkBehaviour netMovement)
        {
            var obj = netMovement.Object;
            if (obj != null && obj.IsValid)
            {
                // ✅ chỉ proxy mới bỏ qua, local owner vẫn được tự set animator
                if (!obj.HasStateAuthority && !obj.HasInputAuthority)
                    return;
            }
        }
#endif


        MovementStateId stateId = MovementStateId.Idle;
        if (_movementState != null)
            stateId = _movementState.Current;

        ApplyStateToAnimator(stateId);
        UpdateKinematic(stateId);
    }

    void TryBindMovementState()
    {
        if (!stateProvider || stateProvider.Movement == null)
            return;

        if (_movementState != null)
            _movementState.OnStateChanged -= OnMovementStateChanged;

        _movementState = stateProvider.Movement;
        _movementState.OnStateChanged += OnMovementStateChanged;
    }

    void OnMovementStateChanged(MovementStateId prev, MovementStateId now)
    {
        // Update() đã poll mỗi frame rồi, event này chủ yếu để debug nếu cần
    }

    void ApplyStateToAnimator(MovementStateId stateId)
    {
        animator.SetInteger(_hashLocomotionState, (int)stateId);

        bool grounded = stateId != MovementStateId.Jumping &&
                        stateId != MovementStateId.Falling;

        bool crouch = stateId == MovementStateId.Crouching;
        bool stunned = stateId == MovementStateId.Stunned;
        bool sprint = stateId == MovementStateId.Sprinting && animator.GetFloat(_hashSpeed) > 0.1f;

        animator.SetBool(_hashGrounded, grounded);
        animator.SetBool(_hashCrouch, crouch);
        animator.SetBool(_hashStunned, stunned);
        animator.SetBool(_hashSprint, sprint);  
    }

    void UpdateKinematic(MovementStateId stateId)
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // --- Vận tốc ngang world-space ---
        Vector3 horizVel = Vector3.zero;

        if (_cc != null)
        {
            horizVel = _cc.velocity;
            horizVel.y = 0f;
        }
        else
        {
            Transform src = velocitySource ? velocitySource : transform;
            Vector3 pos = src.position;

            if (!_hasLastPos)
            {
                _hasLastPos = true;
                _lastPos = pos;
                return;
            }

            Vector3 delta = pos - _lastPos;
            _lastPos = pos;

            horizVel = delta / dt;
            horizVel.y = 0f;
        }

        float rawSpeed = horizVel.magnitude;

        const float SPEED_DEADZONE = 0.05f;

        // Đứng yên (Idle / Crouch đứng) thì ép về 0 để tránh jitter
        if (stateId == MovementStateId.Idle || stateId == MovementStateId.Crouching)
        {
            if (rawSpeed < 0.5f)
                rawSpeed = 0f;
        }

        if (rawSpeed < SPEED_DEADZONE)
            rawSpeed = 0f;

        float tSpeed = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, speedSmoothTime));
        _currentSpeed = Mathf.Lerp(_currentSpeed, rawSpeed, tSpeed);

        if (_currentSpeed < SPEED_DEADZONE)
            _currentSpeed = 0f;

        animator.SetFloat(_hashSpeed, _currentSpeed);

        // --- Hướng local MoveX/MoveY (chỉ lấy hướng, KHÔNG scale theo speed) ---
        float targetMoveX = 0f;
        float targetMoveY = 0f;

        if (rawSpeed > SPEED_DEADZONE)
        {
            // Hướng chuyển động world-space
            Vector3 dir = horizVel.normalized;
            // Đổi sang local-space của player
            Vector3 localDir = transform.InverseTransformDirection(dir);

            targetMoveY = Mathf.Clamp(localDir.z, -1f, 1f);   // -1 = lùi, 1 = tiến
            targetMoveX = Mathf.Clamp(localDir.x, -1f, 1f);   // -1 = trái, 1 = phải
        }

        // Làm mượt hướng
        float tDir = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, dirSmoothTime));
        _currentMoveX = Mathf.Lerp(_currentMoveX, targetMoveX, tDir);
        _currentMoveY = Mathf.Lerp(_currentMoveY, targetMoveY, tDir);

        if (Mathf.Abs(_currentMoveX) < 0.01f) _currentMoveX = 0f;
        if (Mathf.Abs(_currentMoveY) < 0.01f) _currentMoveY = 0f;

        animator.SetFloat(_hashMoveX, _currentMoveX);
        animator.SetFloat(_hashMoveY, _currentMoveY);

    }
}
