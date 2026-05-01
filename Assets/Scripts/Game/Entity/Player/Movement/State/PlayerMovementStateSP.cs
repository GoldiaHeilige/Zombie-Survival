using UnityEngine;
using TT; // dùng Observer (topic bus) nếu bạn muốn phát ra toàn hệ thống
using static PlayerMovementController; // <- THÊM: để dùng MoveState

[DisallowMultipleComponent]
public class PlayerMovementStateSP : MonoBehaviour, IMovementState
{
    [Header("Refs")]
    [SerializeField] PlayerMovementController _movement;

    MovementStateId _current;
    float _stamina;
    float _maxStamina;

    public MovementStateId Current => _current;
    public float Stamina => _stamina;
    public float MaxStamina => _maxStamina;

    public event System.Action<MovementStateId, MovementStateId> OnStateChanged;

    const string TOPIC = "player.movement.changed";

    void Awake()
    {
        if (_movement == null) _movement = GetComponentInParent<PlayerMovementController>(true);
        if (_movement == null)
        {
            Debug.LogError("[PlayerMovementStateSP] Missing PlayerMovementController.", this);
            enabled = false;
            return;
        }

        // Lấy max từ config (đơn giản, đủ cho UI)
        _maxStamina = (_movement.config != null && _movement.config.staminaMax > 0f)
            ? _movement.config.staminaMax
            : 100f;

        _current = ToId(_movement.state);
        _stamina = Mathf.Clamp(_movement.stamina, 0f, _maxStamina);
    }

    void Update()
    {
        var newState = ToId(_movement.state);
        if (newState != _current)
        {
            var prev = _current;
            _current = newState;
            OnStateChanged?.Invoke(prev, _current);
            Observer.Instance?.NotifyWithData(TOPIC, (prev, _current, gameObject));
        }

        // Cập nhật stamina thô, clamp về [0, Max]
        _stamina = Mathf.Clamp(_movement.stamina, 0f, _maxStamina);
    }

    static MovementStateId ToId(MoveState s)
    {
        return s switch
        {
            MoveState.Idle => MovementStateId.Idle,
            MoveState.Walking => MovementStateId.Walking,
            MoveState.Sprinting => MovementStateId.Sprinting,
            MoveState.Jumping => MovementStateId.Jumping,
            MoveState.Falling => MovementStateId.Falling,
            MoveState.Crouching => MovementStateId.Crouching,
            MoveState.Stunned => MovementStateId.Stunned,
            _ => MovementStateId.Idle
        };
    }
}
