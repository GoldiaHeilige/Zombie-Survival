#if FUSION_WEAVER
using UnityEngine;
using Fusion;
using TT;
using static PlayerMovementController;

[DisallowMultipleComponent]
public class PlayerMovementStateMP : NetworkBehaviour, IMovementState
{
    [Header("Refs")]
    [SerializeField] PlayerMovementController _movement;

    // Fusion 2: dùng OnChangedRender thay cho OnChanged/Changed<T>
    [Networked, OnChangedRender(nameof(OnNetStateChanged))]

    public MovementStateId NetState { get; private set; }

    [Networked, OnChangedRender(nameof(OnNetStaminaChanged))]
    public float NetStamina { get; private set; }

    [Networked, OnChangedRender(nameof(OnNetMaxStaminaChanged))]
    public float NetMaxStamina { get; private set; }

    // Local cache cho UI / code ngoài Fusion
    float _stamina;
    float _maxStamina = 100f;

    public MovementStateId Current
    {
        get
        {
            // Nếu chưa spawned / đã despawn / runner không chạy -> tuyệt đối không đọc NetState
            if (Object == null || !Object.IsValid || Runner == null || !Runner.IsRunning)
            {
                // fallback ưu tiên movement local nếu còn
                if (_movement != null)
                    return ToId(_movement.state);

                // fallback cuối: cache state trước đó (hoặc Idle)
                return _prevStateCache;
            }

            // predicted (client owner)
            if (Object.HasInputAuthority && !Object.HasStateAuthority && _movement != null)
                return ToId(_movement.state);

            // replicated/authoritative (host + others)
            return NetState;
        }
    }



    // UI chỉ đọc cache, KHÔNG đụng tới Net*
    public float Stamina => _stamina;
    public float MaxStamina => Mathf.Max(1f, _maxStamina);

    public event System.Action<MovementStateId, MovementStateId> OnStateChanged;

    const string TOPIC = "player.movement.changed";

    // cache để biết "previous" (OnChangedRender không cấp previous như Changed<T>)
    private MovementStateId _prevStateCache;

    public override void Spawned()
    {
        if (_movement == null) _movement = GetComponentInParent<PlayerMovementController>(true);
        if (_movement == null)
        {
            Debug.LogError("[PlayerMovementStateMP] Missing PlayerMovementController.", this);
            enabled = false;
            return;
        }

        NetState = ToId(_movement.state);

        float max = (_movement.config != null && _movement.config.staminaMax > 0f)
            ? _movement.config.staminaMax
            : 100f;

        NetMaxStamina = max;
        NetStamina = Mathf.Clamp(_movement.stamina, 0f, max);

        // Cập nhật cache cho UI
        _maxStamina = max;
        _stamina = NetStamina;

        _prevStateCache = NetState;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object || !Object.HasStateAuthority) return;

        NetState = ToId(_movement.state);

        // Lấy max từ config nếu có, fallback về NetMaxStamina
        float configMax = (_movement.config != null && _movement.config.staminaMax > 0f)
            ? _movement.config.staminaMax
            : NetMaxStamina;

        float max = Mathf.Max(1f, configMax);

        NetMaxStamina = max;
        NetStamina = Mathf.Clamp(_movement.stamina, 0f, max);

        // Cập nhật cache local cho UI (host side)
        _maxStamina = max;
        _stamina = NetStamina;
    }

    // Callback của OnChangedRender – KHÔNG có tham số Changed<T> nữa ở Fusion 2
    public void OnNetStateChanged()
    {
        try
        {
            if (Object == null || !Object.IsValid || Runner == null || !Runner.IsRunning)
                return;

            var prev = _prevStateCache;
            var now = NetState;
            if (prev != now)
            {
                OnStateChanged?.Invoke(prev, now);
                Observer.Instance?.NotifyWithData(TOPIC, (prev, now, gameObject));
                _prevStateCache = now;
            }
        }
        catch (System.InvalidOperationException)
        {
            // có thể bị gọi trong quá trình despawn -> ignore
        }
    }


    static MovementStateId ToId(MoveState s) => s switch
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

    void OnNetStaminaChanged()
    {
        _stamina = NetStamina;
    }

    void OnNetMaxStaminaChanged()
    {
        _maxStamina = Mathf.Max(1f, NetMaxStamina);
    }

}
#endif