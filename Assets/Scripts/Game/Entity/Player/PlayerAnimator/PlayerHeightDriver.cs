using UnityEngine;

/// <summary>
/// Điều khiển cao độ view (CameraRoot) dựa trên MovementStateId.
/// Dùng được cho cả SP lẫn MP vì lấy state qua PlayerStateProvider.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHeightDriver : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Transform sẽ bị chỉnh localPosition.y (thường là CameraRoot). Nếu để trống sẽ dùng chính GameObject này.")]
    [SerializeField] private Transform target;

    [Tooltip("Provider lấy IMovementState (SP/MP). Nếu trống sẽ tự tìm trên parent.")]
    [SerializeField] private PlayerStateProvider stateProvider;

    [Header("Heights")]
    [Tooltip("Độ cao local Y khi đứng bình thường.")]
    [SerializeField] private float standingHeight = 1.6f;

    [Tooltip("Độ cao local Y khi crouch.")]
    [SerializeField] private float crouchHeight = 1.0f;

    [Tooltip("Độ cao local Y khi stunned / downed (nếu muốn dùng sau).")]
    [SerializeField] private float stunnedHeight = 0.6f;

    [Header("Smoothing")]
    [Tooltip("Thời gian (giây) để lerp giữa các mức cao.")]
    [SerializeField] private float smoothTime = 0.12f;

    [Tooltip("Dùng localPosition.y hiện tại làm standingHeight khi Awake.")]
    [SerializeField] private bool useInitialAsStanding = true;

    IMovementState _movement;
    float _currentHeight;
    float _targetHeight;

    void Awake()
    {
        if (!target)
            target = transform;

        if (!stateProvider)
            stateProvider = GetComponentInParent<PlayerStateProvider>();

        if (useInitialAsStanding && target)
        {
            standingHeight = target.localPosition.y;
        }

        _currentHeight = standingHeight;
        _targetHeight = standingHeight;
    }

    void OnEnable()
    {
        TryBindMovement();
    }

    void OnDisable()
    {
        if (_movement != null)
        {
            _movement.OnStateChanged -= OnMovementStateChanged;
            _movement = null;
        }
    }

    void TryBindMovement()
    {
        if (stateProvider == null)
            stateProvider = GetComponentInParent<PlayerStateProvider>();

        if (stateProvider == null)
            return;

        var newMovement = stateProvider.Movement;
        if (newMovement == null || newMovement == _movement)
            return;

        if (_movement != null)
            _movement.OnStateChanged -= OnMovementStateChanged;

        _movement = newMovement;
        _movement.OnStateChanged += OnMovementStateChanged;
    }

    void OnMovementStateChanged(MovementStateId from, MovementStateId to)
    {
        _targetHeight = GetHeightForState(to);
    }

    float GetHeightForState(MovementStateId state)
    {
        switch (state)
        {
            case MovementStateId.Crouching:
                return crouchHeight;

            case MovementStateId.Stunned:
                return stunnedHeight;

            default: // Idle, Walking, Sprinting, Jumping, Falling...
                return standingHeight;
        }
    }

    void Update()
    {
        // Movement có thể được bind trễ (MP spawn xong mới có provider)
        if (_movement == null)
            TryBindMovement();

        if (target == null)
            return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // Exponential lerp cho mượt, không phụ thuộc FPS
        float t = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, smoothTime));
        _currentHeight = Mathf.Lerp(_currentHeight, _targetHeight, t);

        var local = target.localPosition;
        local.y = _currentHeight;
        target.localPosition = local;
    }
}
