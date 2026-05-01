using System.Collections;
using Fusion;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class ZombieFallbackAI : MonoBehaviour
{
    public enum FallbackState { Idle, CircleTarget, WanderLeash }

    [Header("Circle around downed")]
    public float circleRadiusMin = 3f;
    public float circleRadiusMax = 6f;
    public float circleRepathTime = 1.0f;

    [Header("Wander/Leash")]
    public Transform homeAnchor;
    public float leashRadius = 12f;
    public float wanderRadius = 8f;
    public float wanderRepathTime = 2.0f;
    public float homeReturnSpeedMul = 0.7f;

    [Header("Targeting")]
    public float retargetInterval = 0.5f;

    AIPortHub _hub;
    [SerializeField] bool requirePorts = true;

    NavMeshAgent _agent;
    ZombieMovement mover;
    ZombieBrain _brain;              // NEW: để biết Brain đang ở state nào

    ITargetable _currentFollow;
    FallbackState _state;
    bool _freezeByGameOver;

    float _nextTargetTick;
    float _circleNextTick;
    float _wanderNextTick;
    Vector3 _homePos;
    float _baseSpeed;

    void Awake()
    {
        // Nếu không phải StateAuthority → tắt luôn ở client
#if FUSION_WEAVER
        var runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        if (runner != null && !(runner.IsServer || runner.IsSharedModeMasterClient))
        {
            // Client không spawn → tắt hẳn, và đừng yêu cầu ports
            requirePorts = false;
            enabled = false;
            return;
        }
#endif

        _hub = FindFirstObjectByType<AIPortHub>(FindObjectsInactive.Include);
        if (requirePorts && (_hub == null || _hub.Spawn == null || _hub.Random == null))
        {
            Debug.LogError("[SpawnManager] AIPortHub/ports missing.", this);
            enabled = false;
            return;
        }

        _agent = GetComponent<NavMeshAgent>();
        mover = GetComponent<ZombieMovement>();

        _baseSpeed = _agent.speed;
        _homePos = homeAnchor ? homeAnchor.position : transform.position;
        _brain = GetComponent<ZombieBrain>(); // NEW
    }

    void OnEnable()
    {
        GameOverManager.OnGameOver += OnGameOver;
    }

    void OnDisable()
    {
        GameOverManager.OnGameOver -= OnGameOver;
    }

    void Update()
    {
        if (_freezeByGameOver) return;
        // NEW: nếu não đang điều khiển Chase/Attack thì fallback không được can thiệp NavMesh
        if (_brain != null)
        {
            if (_brain.current == ZombieBrain.State.Chase ||
                _brain.current == ZombieBrain.State.Attack)
            {
                // đảm bảo state nội bộ là Idle, không còn TickCircle / TickWander
                Enter(FallbackState.Idle);
                return;
            }
        }

        // 1) Tìm mục tiêu theo Followables (AliveLike) mỗi retargetInterval
        if (Time.time >= _nextTargetTick)
        {
            _nextTargetTick = Time.time + retargetInterval;
            // false = chỉ cần AliveLike, không bắt buộc CanBeAttacked
            // → dùng được cả cho case player downed
            if (_hub != null && _hub.Target != null)
                _currentFollow = _hub.Target.GetBestTarget(transform.position, false);
            else
                _currentFollow = null;
        }

        // 2) Quyết định state
        if (_currentFollow != null)
        {
            // Có người “còn hiện diện”
            if (!_currentFollow.CanBeAttacked)
            {
                // Ví dụ: player downed → đi vòng quanh
                Enter(FallbackState.CircleTarget);
            }
            else
            {
                // Player còn attack được → để Brain + SmartSelector/Perception xử lý combat
                Enter(FallbackState.Idle);
            }
        }
        else
        {
            // Không có target nào đáng quan tâm → đi lang thang quanh nhà
            Enter(FallbackState.WanderLeash);
        }

        // 3) Hành vi theo state
        switch (_state)
        {
            case FallbackState.CircleTarget:
                TickCircle();
                break;
            case FallbackState.WanderLeash:
                TickWanderLeash();
                break;
            case FallbackState.Idle:
                // không override gì; để hệ tấn công/path chính tự chạy
                break;
        }
    }

    void Enter(FallbackState s)
    {
        if (_state == s) return;
        _state = s;

        if (_state == FallbackState.WanderLeash)
            _agent.speed = _baseSpeed * homeReturnSpeedMul;
        else
            _agent.speed = _baseSpeed;
    }

    void TickCircle()
    {
        if (_currentFollow == null) return;
        if (Time.time < _circleNextTick) return;
        _circleNextTick = Time.time + circleRepathTime;

        var tr = _currentFollow.TargetTransform;
        if (!tr) return;

        Vector3 center = tr.position;
        float r = _hub.Random.RangeFloat(circleRadiusMin, circleRadiusMax);
        Vector2 v = _hub.Random.InsideUnitCircle();
        Vector2 dir = (v.sqrMagnitude > 0.0001f ? v.normalized : Vector2.right);
        Vector2 rnd = dir * r;
        Vector3 dst = center + new Vector3(rnd.x, 0f, rnd.y);

        if (NavMesh.SamplePosition(dst, out var hit, 2f, NavMesh.AllAreas))
            mover.SetDestination(hit.position);
    }

    void TickWanderLeash()
    {
        if (Time.time < _wanderNextTick) return;
        _wanderNextTick = Time.time + wanderRepathTime;

        Vector3 anchor = homeAnchor ? homeAnchor.position : _homePos;
        Vector2 v = _hub.Random.InsideUnitCircle();
        Vector3 random = anchor + new Vector3(v.x, 0f, v.y) * wanderRadius;

        random.y = anchor.y;

        if (Vector3.Distance(transform.position, anchor) > leashRadius)
            random = anchor; // kéo về gần nhà

        if (NavMesh.SamplePosition(random, out var hit, 3f, NavMesh.AllAreas))
            mover.SetDestination(hit.position);
    }

    void OnGameOver()
    {
        _freezeByGameOver = true;

        // Dừng hẳn fallback để không set destination nữa
        enabled = false;

        // Optional: dừng agent/mover ngay tại chỗ nếu bạn muốn chắc chắn
        if (mover != null)
            mover.ForceFreezeNav();
    }

}
