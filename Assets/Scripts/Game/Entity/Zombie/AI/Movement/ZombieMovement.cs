using UnityEngine;
using UnityEngine.AI;
using Fusion;

/// <summary>
/// Tick-based movement for zombie.
/// SP = SimTime 64Hz
/// MP Host = FixedUpdateNetwork (Fusion tick)
/// Client = AI disabled by AIAuthorityGate
/// 
/// NavMeshAgent is ONLY for pathfinding.
/// Transform movement is done manually, identical across Editor/Build.
/// </summary>
[DisallowMultipleComponent]
public class ZombieMovement : NetworkBehaviour, IPoolable
{
    [Header("Config")]
    public ZombieMovementConfig config;

    [Header("References")]
    public NavMeshAgent agent;

    [Header("Climb Window")]
    [Tooltip("Thời gian anim trèo (giây) – nên khớp với length clip ClimbWindow")]
    public float climbDuration = 1.0f;

    bool _isClimbing;
    ZombieBrain _brain;
    ZombieNetworkAnimator _netAnim;

    Animator _anim;
    int _hashClimbNonRootState;
    int _hashClimbRootState;

    Transform _hips;
    Vector3 _prevHipsLocal;
    bool _compensateHipXZ;

    float _stuckTimer;
    Vector3 _stuckLastPos;

    // cached
    bool isMP;
    bool isAuthority;

    Vector3 lastPos;
    Vector3 _lastDestination;
    bool _hasLastDestination;
    float _nextRetryTime;

    [Header("Debug Nav")]
    public bool debugNav = false;
    public float debugNavInterval = 0.25f;
    float _nextDebugTime;

    // Spawn/Nav init gating: prevent NavMeshAgent computing a path from a wrong internal position
    // in the first tick(s) after spawn (common in MP due to timing/replication).
    bool _pendingNavInit;
    int _spawnTick;
    bool _hasSpawnTick;

    /// <summary>
    /// True when it's safe to accept SetDestination calls.
    /// </summary>
    public bool NavReady => !_pendingNavInit && agent != null && agent.enabled && agent.isOnNavMesh;

    public bool IsDead { get; set; }
    public Vector3 ManualVelocity { get; private set; }

    void Awake()
    {
        if (!agent) agent = GetComponent<NavMeshAgent>();

        _netAnim = GetComponent<ZombieNetworkAnimator>();
        _brain = GetComponent<ZombieBrain>();

        // disable NavMeshAgent auto movement
        if (agent)
        {
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.autoTraverseOffMeshLink = false;
        }

        var runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        isMP = runner != null;
    }

    void OnEnable()
    {
        if (!isMP)  // SP = SimTime tick
            SimTime.onTick += SP_Tick64;
    }

    void OnDisable()
    {
        SimTime.onTick -= SP_Tick64;
    }

    // =============================
    // SP Tick
    // =============================
    void SP_Tick64()
    {
        if (IsDead)
        {
            ForceFreezeNav();   // hàm bạn thêm ở dưới
            return;
        }

        if (_pendingNavInit)
        {
            PerformNavInit();
            return;
        }

        TryHandleOffMeshLink();
        if (_isClimbing) return;

        TickMove(SimTime.Delta);
    }

    // =============================
    // MP Tick (Host only)
    // =============================
    public override void Spawned()
    {
        isAuthority = Object && Object.HasStateAuthority;

        // ✅ MP no pooling: still need nav init gate
        if (isAuthority)
        {
            _pendingNavInit = true;

            _spawnTick = Runner.Tick;
            _hasSpawnTick = true;

            // reset state giống OnSpawned()
            _isClimbing = false;
            _brain?.SetTraversingLink(false);
            ManualVelocity = Vector3.zero;

            _nextRetryTime = 0f;
            _hasLastDestination = false;
            _lastDestination = Vector3.zero;

            if (agent)
            {
                agent.enabled = true;
                agent.isStopped = false;

                if (agent.isOnOffMeshLink)
                    agent.CompleteOffMeshLink();

                agent.ResetPath();
                agent.nextPosition = transform.position;
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!isMP) return;
        if (!isAuthority) return;

        if (IsDead)
        {
            ForceFreezeNav();
            return;
        }

        if (_pendingNavInit)
        {
            if (_hasSpawnTick && Runner != null && Runner.Tick <= _spawnTick)
                return;

            PerformNavInit();
            return;
        }

        TryHandleOffMeshLink();
        if (_isClimbing) return;

        TickMove(Runner.DeltaTime);
    }

    // =============================
    // CORE MOVEMENT
    // =============================
    void TickMove(float dt)
    {
        if (IsDead)
        {
            ManualVelocity = Vector3.zero;
            return;
        }

        if (!agent || !agent.enabled)
            return;

        // sync path warped positions
        agent.nextPosition = transform.position;

        // Nếu agent đã bị stop/reset path thì đừng đi theo steeringTarget (tránh drift về 000)
        if (agent.isStopped || !agent.hasPath)
        {
            ManualVelocity = Vector3.zero;
            return;
        }

        if (!agent.pathPending && agent.pathStatus != NavMeshPathStatus.PathComplete)
        {
            ManualVelocity = Vector3.zero;
            agent.ResetPath(); // ép não set destination lại / selector đổi target
            return;
        }

        // ✅ Arrived handling: tới nơi thì đứng yên, đừng spam reset path/destination
        if (!agent.pathPending && agent.hasPath)
        {
            // remainingDistance đôi khi invalid nếu path chưa compute xong
            if (agent.remainingDistance <= Mathf.Max(agent.stoppingDistance, 0.05f))
            {
                ManualVelocity = Vector3.zero;
                return;
            }
        }

        if (!agent.hasPath)
        {
            ManualVelocity = Vector3.zero;

            if (_hasLastDestination && Time.time >= _nextRetryTime && agent.enabled && agent.isOnNavMesh)
            {
                _nextRetryTime = Time.time + 0.25f; // cooldown
                agent.SetDestination(_lastDestination);
            }
            return;
        }


        // ✅ Dùng corners để tránh “đâm thẳng vào tường”
        Vector3 next = PickNextCorner(agent.path.corners, transform.position, 0.25f); // 0.2–0.4 tuỳ bạn
        Vector3 toNext = next - transform.position;

        if (toNext.sqrMagnitude < 0.02f * 0.02f) // nếu điểm quá sát -> coi như corner “rác”
        {
            // fallback: dùng steeringTarget hoặc desiredVelocity để vẫn tiến lên
            Vector3 fallback = agent.steeringTarget - transform.position;
            if (fallback.sqrMagnitude > 0.02f * 0.02f)
                toNext = fallback;
            else
            {
                ManualVelocity = Vector3.zero;
                return;
            }
        }


        // HƯỚNG ĐẦY ĐỦ (CÓ CẢ Y) ĐỂ ĐI LÊN/XUỐNG BẬC THANG
        Vector3 moveDir = toNext.normalized;

        float speed = agent.speed;
        float rotSpeed = config ? config.rotationSpeed : 360f;


        // MOVE: cho phép thay đổi cả Y
        Vector3 delta = moveDir * speed * dt;
        ManualVelocity = dt > 0.00001f ? (delta / dt) : Vector3.zero;
        transform.position += delta;

        // ROTATE: chỉ xoay theo mặt phẳng ngang để không bị chúi đầu
        Vector3 flatDir = new Vector3(moveDir.x, 0f, moveDir.z);
        if (flatDir.sqrMagnitude > 0.0001f)
        {
            Quaternion target = Quaternion.LookRotation(flatDir.normalized);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                target,
                rotSpeed * dt
            );
        }

        if (NavMesh.SamplePosition(transform.position, out var hit, 0.5f, NavMesh.AllAreas))
        {
            var d = hit.position - transform.position;
            if (d.sqrMagnitude > 0.05f * 0.05f)
                transform.position = hit.position;
        }

        DebugNav("TickMove");
        agent.nextPosition = transform.position;

        // STUCK DETECT
        float movedSqr = (transform.position - _stuckLastPos).sqrMagnitude;
        if (movedSqr < 0.002f * 0.002f && agent.hasPath && !agent.pathPending && !_isClimbing)
        {
            _stuckTimer += dt;
            if (_stuckTimer > 0.35f) // 0.25–0.6 tuỳ bạn
            {
                if (_hasLastDestination && agent.isOnNavMesh)
                {
                    if (debugNav) Debug.LogWarning($"[ZNav][UNSTUCK] {name} repath to {_lastDestination}", this);
                    agent.ResetPath();
                    agent.SetDestination(_lastDestination);
                }
                _stuckTimer = 0f;
                _stuckLastPos = transform.position;
                ManualVelocity = Vector3.zero;
                return;
            }
        }
        else
        {
            _stuckTimer = 0f;
        }
        _stuckLastPos = transform.position;

    }

    // =============================
    // API
    // =============================
    public void SetDestination(Vector3 pos)
    {
        if (_pendingNavInit) return;
        if (!agent || !agent.enabled) return;
        if (!agent.isOnNavMesh) return;

        // Guard NaN/Infinity (tránh destination bị ngáo rồi rơi về zero)
        if (!float.IsFinite(pos.x) || !float.IsFinite(pos.y) || !float.IsFinite(pos.z))
            return;

        if (agent.isOnOffMeshLink)
            DebugNav("SetDest-whileOffMesh");

        if (!NavMesh.SamplePosition(pos, out var hit, 1.5f, NavMesh.AllAreas))
        {
            if (debugNav) Debug.LogWarning($"[ZNav][SetDest] sample FAIL pos={pos}", this);
            return;
        }

        Vector3 dest = hit.position;

        if (!agent.isOnNavMesh)
        {
            if (debugNav) Debug.LogWarning($"[ZNav][SetDest] agent NOT on navmesh yet. zombiePos={transform.position} dest={dest}", this);
            return;
        }

        if (!agent.SetDestination(dest))
        {
            if (debugNav) Debug.LogWarning($"[ZNav][SetDest] SetDestination FAIL dest={dest}", this);
            return;
        }

        _lastDestination = dest;
        _hasLastDestination = true;

        DebugNav("SetDest-AFTER");
    }

    public void SetSpeed(float s)
    {
        if (agent) agent.speed = s;
    }

    void TryHandleOffMeshLink()
    {
        if (_isClimbing) return;
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        if (!agent.isOnOffMeshLink) return;

        bool hasBlock = _brain != null && _brain.HasBlockingBarricade;
        if (hasBlock) return;

        var data = agent.currentOffMeshLinkData;

        var owner = data.owner as Component;
        if (owner != null)
        {
            var w = owner.GetComponentInParent<BarricadeWindow>();
            if (w != null && w.CanTakeZombieHit())
                return; // còn ván -> cấm traverse
        }

        // ✅ Guard: endPos bị invalid hay rơi về 000 => không cho climb
        // (Không thể check == Vector3.zero tuyệt đối vì map có thể gần 000,
        // nên check theo khoảng cách bất thường + sample navmesh)
        Vector3 start = transform.position;
        Vector3 end = data.endPos;

        // Fallback 1: nếu sample navmesh quanh end fail => end đáng ngờ
        bool endOk = NavMesh.SamplePosition(end, out var endHit, 1.0f, NavMesh.AllAreas);
        if (endOk) end = endHit.position;

        // Nếu end vẫn “ngáo” (quá xa vô lý hoặc sample fail) => abort
        float dist = Vector3.Distance(start, end);
        if (!endOk || dist > 50f) // 50m tuỳ map, bạn có thể chỉnh
        {
            Debug.LogWarning(
                $"[ZombieMovement] OffMeshLink end invalid -> ABORT climb. " +
                $"start={start} end(raw)={data.endPos} sampleOk={endOk} dist={dist:F1} name={name}",
                this
            );

            // Thoát link để agent khỏi bị kẹt trạng thái isOnOffMeshLink
            agent.CompleteOffMeshLink();
            agent.ResetPath();
            return;
        }

        StartCoroutine(ClimbWindowRoutine(data, end));
    }

    System.Collections.IEnumerator ClimbWindowRoutine(OffMeshLinkData data, Vector3 validatedEnd)
    {
        _isClimbing = true;
        _brain?.SetTraversingLink(true);

        // ✅ Stop agent trong lúc climb để tránh path/steering update phá pos
        bool prevStopped = agent.isStopped;
        agent.isStopped = true;

        agent.updatePosition = false;
        agent.updateRotation = false;

        Vector3 start = transform.position;
        Vector3 end = validatedEnd;

        // Face hướng nhảy
        Vector3 flatDir = end - start;
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(flatDir.normalized);

        if (_netAnim != null)
            _netAnim.PlayClimbWindow();

        float t = 0f;
        float dur = Mathf.Max(0.01f, climbDuration);

        while (t < dur)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Clamp01(t / dur);
            transform.position = Vector3.Lerp(start, end, alpha);
            yield return null;
        }

        // ✅ Sync lại agent internal position để tránh “snap” frame sau
        if (agent.enabled && agent.isOnNavMesh)
            agent.Warp(transform.position);

        // Kết thúc link
        agent.CompleteOffMeshLink();

        // ✅ ép sync lại agent internal
        if (agent.enabled && agent.isOnNavMesh)
            agent.Warp(transform.position);

        agent.nextPosition = transform.position;

        // ✅ RE-APPLY destination để khỏi đứng im 1 lúc vì agent mất path
        if (_hasLastDestination && agent.enabled && agent.isOnNavMesh)
        {
            agent.ResetPath();                 // optional nhưng giúp sạch
            agent.SetDestination(_lastDestination);
        }

        /*        agent.updatePosition = false;
                agent.updateRotation = false;

                _isClimbing = false;
        */

        agent.isStopped = prevStopped;
        agent.updatePosition = false;
        agent.updateRotation = false;

        _brain?.SetTraversingLink(false);
        _isClimbing = false;
    }


    public void OnSpawned()
    {
        // Gate nav/path for 1 tick after spawn (MP) / 1 SimTime tick (SP)
        _pendingNavInit = true;
        _hasSpawnTick = false;
        if (isMP)
        {
            var runner = Runner != null ? Runner : FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
            if (runner != null)
            {
                _spawnTick = runner.Tick;
                _hasSpawnTick = true;
            }
        }

        _isClimbing = false;
        _brain?.SetTraversingLink(false);
        _compensateHipXZ = false;
        ManualVelocity = Vector3.zero;

        _nextRetryTime = 0f;
        _hasLastDestination = false;         // QUAN TRỌNG: tránh reuse dest cũ
        _lastDestination = Vector3.zero;

        if (agent)
        {
            agent.enabled = true;
            agent.isStopped = false;

            if (agent.isOnOffMeshLink)
                agent.CompleteOffMeshLink();

            // Don't reset/warp here yet; we'll do a single authoritative snap in PerformNavInit()
            agent.ResetPath();
            agent.nextPosition = transform.position;
        }


        if (_anim) _anim.applyRootMotion = false;
    }

    public void OnDespawned()
    {
        // đảm bảo không kẹt climbing khi pooled
        _isClimbing = false;
        _brain?.SetTraversingLink(false);
        _compensateHipXZ = false;
        ManualVelocity = Vector3.zero;

        if (agent)
        {
            // không để lại stopped=true
            agent.isStopped = false;

            if (agent.isOnOffMeshLink)
                agent.CompleteOffMeshLink();

            if (agent.enabled && agent.isOnNavMesh)
                agent.ResetPath();
        }

        if (_anim) _anim.applyRootMotion = false;
    }


    public void ForceFreezeNav()
    {
        if (!agent || !agent.enabled) return;

        // quan trọng: ngăn agent tự update bất cứ thứ gì
        agent.isStopped = true;
        _brain?.SetTraversingLink(false);

        // nếu còn đang "kẹt" state offmesh (dù transform đã đi xa) => thoát ngay
        if (agent.isOnOffMeshLink)
            agent.CompleteOffMeshLink();

        // reset path để nó không tính lại route nữa
        if (agent.isOnNavMesh)
        {
            agent.ResetPath();

            // sync internal position về đúng transform để khỏi snap về offmesh start
            agent.Warp(transform.position);
            agent.nextPosition = transform.position;
        }

        // nếu đang climb coroutine mà chết => stop luôn
        _isClimbing = false;
        StopAllCoroutines();
    }

    void DebugNav(string tag)
    {
        if (!debugNav) return;
        if (Time.time < _nextDebugTime) return;
        _nextDebugTime = Time.time + Mathf.Max(0.05f, debugNavInterval);

        if (!agent)
        {
            Debug.LogWarning($"[ZNav][{tag}] {name} agent=NULL", this);
            return;
        }

        Vector3 dest = agent.destination;
        Vector3 steer = agent.steeringTarget;

        Debug.Log(
            $"[ZNav][{tag}] {name} " +
            $"pos={transform.position} " +
            $"onNav={agent.isOnNavMesh} enabled={agent.enabled} stopped={agent.isStopped} " +
            $"hasPath={agent.hasPath} pending={agent.pathPending} status={agent.pathStatus} " +
            $"dest={dest} steer={steer} rem={agent.remainingDistance:F2} " +
            $"offLink={agent.isOnOffMeshLink} nextPos={agent.nextPosition}",
            this
        );

        if (agent.hasPath && agent.path != null && agent.path.corners != null)
        {
            var c = agent.path.corners;
            int show = Mathf.Min(c.Length, 4);
            string s = "";
            for (int i = 0; i < show; i++) s += $"{i}:{c[i]} ";
            Debug.Log($"[ZNav][{tag}] corners({c.Length}) {s}", this);
        }

        if (agent.isOnOffMeshLink)
        {
            var d = agent.currentOffMeshLinkData;
            Debug.Log($"[ZNav][{tag}] OML start={transform.position} end={d.endPos} owner={(d.owner ? d.owner.name : "<null>")}", this);
        }
    }

    void PerformNavInit()
    {
        _pendingNavInit = false;

        if (!agent) return;

        agent.enabled = true;
        agent.isStopped = false;

        if (agent.isOnOffMeshLink)
            agent.CompleteOffMeshLink();

        // Always snap to navmesh (even if currently off-mesh). This prevents paths starting at origin/old pos.
        if (NavMesh.SamplePosition(transform.position, out var hit, 2.0f, NavMesh.AllAreas))
        {
            transform.position = hit.position;
            agent.Warp(hit.position);
            agent.nextPosition = hit.position;
        }
        else
        {
            // keep internal in sync anyway
            agent.Warp(transform.position);
            agent.nextPosition = transform.position;
        }

        agent.ResetPath();
        _hasLastDestination = false;
        _lastDestination = Vector3.zero;
    }

    static Vector3 PickNextCorner(Vector3[] corners, Vector3 pos, float minDist)
    {
        if (corners == null || corners.Length == 0) return pos;

        float minSqr = minDist * minDist;

        // chọn corner đầu tiên có khoảng cách đủ xa
        for (int i = 0; i < corners.Length; i++)
        {
            if ((corners[i] - pos).sqrMagnitude > minSqr)
                return corners[i];
        }

        // nếu tất cả đều quá gần => lấy corner cuối (đích)
        return corners[corners.Length - 1];
    }

}
