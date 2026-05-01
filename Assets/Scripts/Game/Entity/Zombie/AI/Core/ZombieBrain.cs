// Assets/Scripts/AI/Zombie/ZombieBrain.cs
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
[RequireComponent(typeof(ZombieBlackboard))]
[RequireComponent(typeof(NavMeshAgent))]
public class ZombieBrain : MonoBehaviour, IPoolable
{
    public enum State { Idle, Chase, Attack, HitReact, Death }

    public State current;
    public float repathInterval = 0.2f;     // tần suất cập nhật đích
    public float attackHoldTime = 0.4f;     // thời gian đứng lại ở Attack (stub, chưa gây dmg)

    ZombieBlackboard bb;
    NavMeshAgent agent;
    DamageableHealth hp;
    ZombieMeleeExecutor melee;
    ZombieMovement mover;
    ZombieAudioDriver audio;
    ZombieStateNet _stateNet;

    [Header("Barricade")]
    [Tooltip("Khoảng cách zombie đứng trước cửa để bắt đầu đấm ván")]
    public float barricadeAttackRange = 1.6f;

    BarricadeWindow _currentBarricade;
    Transform _barricadePoint;

    Vector3 _lastChaseDest;
    bool _hasLastChaseDest;
    bool _freezeByGameOver;

    float _nextPathTime;
    float _stateEndTime;
    BarricadeLaneTrigger _currentLaneTrigger;
    Transform _lookTarget;

    [SerializeField] float barricadeClearGraceTime = 0.10f;
    float _barricadeJustClearedUntil = 0f;

    public bool IsTraversingLink { get; private set; }

    public void SetTraversingLink(bool v)
    {
        IsTraversingLink = v;
    }


    void Awake()
    {
        bb = GetComponent<ZombieBlackboard>();
        agent = GetComponent<NavMeshAgent>();
        hp = GetComponent<DamageableHealth>();
        melee = GetComponent<ZombieMeleeExecutor>();
        mover = GetComponent<ZombieMovement>();
        audio = GetComponent<ZombieAudioDriver>();               // NEW (cùng root)
        if (!audio)
            audio = GetComponentInChildren<ZombieAudioDriver>();

#if FUSION_WEAVER
        _stateNet = GetComponent<ZombieStateNet>();
#endif
    }

    void OnEnable()
    {
        GameOverManager.OnGameOver += OnGameOver;

        // SP: dùng SimTime tick
        if (GameSession.Mode == AppPlayMode.Single)
            SimTime.onTick += TickAI64;

        if (hp != null)
        {
            hp.OnDeathLocal += OnDeathLocal;
        }
    }

    void OnDisable()
    {
        GameOverManager.OnGameOver -= OnGameOver;

        ResetAllObjectives(resetPath: false);

        if (GameSession.Mode == AppPlayMode.Single)
            SimTime.onTick -= TickAI64;

        if (hp != null)
        {
            hp.OnDeathLocal -= OnDeathLocal;
        }
    }

    void Start()
    {
        ChangeState(State.Idle);
    }

    void TickAI64()
    {
        if (_freezeByGameOver) return;

        switch (current)
        {
            case State.Idle: TickIdle(); break;
            case State.Chase: TickChase(); break;
            case State.Attack: TickAttack(); break;
            case State.HitReact: TickHitReact(); break;
            case State.Death:  /* no-op */   break;
        }
    }

    void Update()
    {
        // MP (Host/Server): để não zombie chạy mỗi frame như trước đây
        if (GameSession.Mode != AppPlayMode.Single)
        {
            TickAI64();
        }
    }


    // ───────────── States ─────────────

    void TickIdle()
    {
        // thấy target trong tầm nhận thức → đuổi
        if (bb.HasTarget())
            ChangeState(State.Chase);
    }

    void TickChase()
    {
        // Có cửa cần phá không?
        bool barricadeValid = _currentBarricade != null && _barricadePoint != null;

        // còn ván để hit?
        bool canHit = barricadeValid && _currentBarricade.CanTakeZombieHit();

        // vừa hết ván -> cho “grace” để vẫn chạy qua cửa 1 nhịp
        if (barricadeValid && !canHit)
        {
            // set 1 lần ngay lúc vừa clear (không spam mỗi frame)
            if (_barricadeJustClearedUntil < Time.time)
                _barricadeJustClearedUntil = Time.time + barricadeClearGraceTime;
        }

        bool grace = barricadeValid && Time.time < _barricadeJustClearedUntil;

        // ✅ coi như vẫn “đang xử lý barricade” nếu còn canHit hoặc đang grace
        bool hasBarricade = canHit || grace;


        if (hasBarricade)
        {
            // Khoảng cách tới điểm đứng đập cửa
            float distToBarricade = Vector3.Distance(transform.position, _barricadePoint.position);
            //      Debug.Log($"[Brain] {name} CHASE barricade {_currentBarricade.name} dist={distToBarricade:F2}");

            // Tới gần cửa → chuyển sang Attack (đấm ván)
            // Chỉ đánh cửa khi còn ván thật sự
            if (canHit && distToBarricade <= barricadeAttackRange)
            {
                ChangeState(State.Attack);
                return;
            }

            // Nếu chỉ đang grace (cửa vừa vỡ) -> KHÔNG vào Attack.
            // Cứ tiếp tục Chase tới _barricadePoint để traverse/offmesh link.

            // Chưa gần → tiếp tục đuổi tới cửa
            if (Time.time >= _nextPathTime)
            {
                _nextPathTime = Time.time + repathInterval;

                if (mover && mover.config)
                    mover.SetSpeed(mover.config.chaseSpeed);

                // ✅ Khi đang xử lý barricade: CHỈ dồn tới điểm đập cửa
                mover.SetDestination(_barricadePoint.position);
            }


            // Khi còn ván thì KHÔNG chase player trực tiếp
            return;
        }

        if (barricadeValid && !canHit && !grace)
        {
            ResetAllObjectives(resetPath: false);
            // Không return ở đây nếu phía dưới TickChase còn chạy chase player,
            // vì bạn muốn nó tiếp tục xuống logic chase player.
        }

        // ─────────────────────────────
        // Không còn barricade cần phá → chase player như cũ
        // ─────────────────────────────

        // trong tầm tấn công → Attack
        if (!IsTraversingLink && bb.distanceToTarget <= bb.attackRange * 1.05f)
        {
            ChangeState(State.Attack);
            return;
        }

        // cập nhật đích không quá dày
        if (Time.time >= _nextPathTime && bb.target)
        {
            _nextPathTime = Time.time + repathInterval;

            if (mover && mover.config)
                mover.SetSpeed(mover.config.chaseSpeed);

            Vector3 dest = bb.target.position;

            // chỉ chase nếu sample được lên navmesh
            if (NavMesh.SamplePosition(dest, out var hit, 1.5f, NavMesh.AllAreas))
                dest = hit.position;
            else
            {
                // target nằm ngoài navmesh / out of bounds => clear để SmartSelector chọn người khác
                bb.target = null;
                bb.distanceToTarget = 0f;
                bb.hasLOS = false;
                return;
            }


            // chỉ set destination khi thay đổi đáng kể
            if (!_hasLastChaseDest || (dest - _lastChaseDest).sqrMagnitude > 0.5f * 0.5f)
            {
                mover.SetDestination(dest);
                _lastChaseDest = dest;
                _hasLastChaseDest = true;
            }
        }

    }



    void TickAttack()
    {
        if (IsTraversingLink)
        {
            ChangeState(State.Chase);
            return;
        }

        // ===== Barricade objective (fixed) =====
        bool barricadeValid = _currentBarricade != null && _barricadePoint != null;
        bool canHit = barricadeValid && _currentBarricade.CanTakeZombieHit();
        bool grace = barricadeValid && Time.time < _barricadeJustClearedUntil;

        // Nếu cửa vừa vỡ (grace) mà đã hết ván để hit -> không được ở Attack nữa
        // (tránh loop Attack <-> Chase tại chỗ)
        if (barricadeValid && !canHit && grace)
        {
            ChangeState(State.Chase);
            return;
        }

        // Nếu đang Attack vì barricade nhưng barricade đã không còn hợp lệ (hết ván và hết grace)
        // thì reset sạch để khỏi "kẹt objective ma"
        if (barricadeValid && !canHit && !grace)
        {
            ResetAllObjectives(resetPath: false);
            ChangeState(State.Chase);
            return;
        }

        // Dưới đây, nếu bạn cần bool để dùng tiếp thì dùng biến này:
        bool hasBarricadeToHit = canHit;
        // ===== end =====


        // Dừng lại, xoay mặt về thứ mình đang đánh
        if (mover) mover.SetSpeed(0f);

        // ===== LOCK FACING =====
        // Nếu đang ở Attack và có barricade point -> LUÔN nhìn barricade
        if (_barricadePoint != null)
        {
            FaceTowards(_barricadePoint.position);
        }
        else if (bb.HasTarget())
        {
            FaceTowards(bb.target.position);
        }


        if (melee != null)
        {
            melee.TryAttackOnce();
        }

        // Đang trong 1 đòn attack (anim / executor busy) → giữ nguyên state
        if (melee != null && melee.IsBusy)
            return;

        // Hết thời gian AttackHold → quyết định state tiếp theo
        if (Time.time >= _stateEndTime)
        {
            if (hasBarricadeToHit)
            {
                // Vẫn còn ván → quay lại Chase để tiếp tục đuổi cửa
                ChangeState(State.Chase);
            }
            else if (bb.distanceToTarget <= bb.attackRange * 1.25f)
            {
                ChangeState(State.Chase);
            }
            else
            {
                ChangeState(State.Idle);
            }
        }
    }


    void TickHitReact()
    {
        // Trong thời gian ngắn zombie “khựng” lại
        if (mover) mover.SetSpeed(0f);
        if (Time.time >= _stateEndTime)
        {
            // Nếu còn target → tiếp tục đuổi
            if (bb.HasTarget())
                ChangeState(State.Chase);
            else
                ChangeState(State.Idle);
        }
    }

    // ───────────── Transitions ─────────────

    void ChangeState(State next)
    {
        var prev = current;   // NEW

        // Exit (nếu cần)
        switch (prev)
        {
            case State.Attack:
                _stateEndTime = Time.time + attackHoldTime;
                break;

            case State.HitReact:
                if (mover && mover.config)
                    mover.SetSpeed(mover.config.chaseSpeed);
                break;
        }

        current = next;

#if FUSION_WEAVER
        // MP: chỉ host/state authority được ghi networked state
        if (GameSession.Mode != AppPlayMode.Single && _stateNet != null)
        {
            _stateNet.SetStateAuthority((byte)current);
        }
#endif

        // Báo cho audio driver
        if (audio != null)
        {
            audio.OnStateChanged(prev, current);   // NEW
        }

        //        Debug.Log($"[Brain] {name} {prev} -> {current}  dist={bb.distanceToTarget:F2}");

        // Enter
        switch (current)
        {
            case State.Idle:
                if (mover && mover.config)
                    mover.SetSpeed(mover.config.walkSpeed);
                break;

            case State.Chase:
                if (mover && mover.config)
                    mover.SetSpeed(mover.config.chaseSpeed);

                _nextPathTime = 0f;
                break;

            case State.Attack:
                _stateEndTime = Time.time + attackHoldTime;
                if (mover) mover.SetSpeed(0f);
                break;

            case State.Death:
                if (mover)
                {
                    mover.SetSpeed(0f);
                    mover.IsDead = true;

                    // ép dọn offmesh + đóng băng agent ngay khi chết
                    mover.SendMessage("ForceFreezeNav", SendMessageOptions.DontRequireReceiver);
                    // hoặc tốt hơn: mover.ForceFreezeNav(); nếu bạn để method public
                }

                if (agent)
                    agent.ResetPath();
                break;

        }
    }

    // ───────────── Damage Events ─────────────

    void OnHit(DamageEvent e, DamageResult r)
    {
        if (current == State.Death) return;
    }

    void OnDeathLocal(DamageEvent e, DamageResult r)
    {
        ResetAllObjectives(resetPath: false);

        ChangeState(State.Death);

        var death = GetComponent<ZombieDeathHandler>();
        if (death != null)
            death.PlayDeathAndDespawn();
    }


    // ───────────── Utils ─────────────

    void FaceTowards(Vector3 worldPos)
    {
        Vector3 dir = worldPos - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            var targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, 12f * Time.deltaTime);
        }
    }

    public void OnSpawned()
    {
        _freezeByGameOver = false;

        if (mover) mover.IsDead = false;
        ResetAllObjectives(resetPath: true);

        // Reset state và AI
        current = State.Idle;
        _nextPathTime = 0f;
        _stateEndTime = 0f;

        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isOnNavMesh)
            {
                if (mover && mover.config)
                    mover.SetSpeed(mover.config.walkSpeed);

                agent.ResetPath();
            }
        }


        if (hp != null)
        {
            hp.ResetHealth(); // ta sẽ thêm hàm ResetHealth() vào DamageableHealth
        }

        if (bb != null)
        {
            bb.target = null;
            bb.distanceToTarget = 0f;
            bb.hasLOS = false;
        }

        if (melee != null)
        {
            melee.ResetExecutor(); // ta sẽ thêm hàm ResetExecutor() vào ZombieMeleeExecutor
        }

        // Reset animation nếu có

        ChangeState(State.Idle);
    }

    public void OnDespawned()
    {
        StopAllCoroutines();
        ResetAllObjectives(resetPath: true);

        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isOnNavMesh)
            {
                if (mover && mover.config)
                    mover.SetSpeed(mover.config.walkSpeed);

                agent.ResetPath();
            }
        }


        // Nếu muốn, tạm thời disable collider để tránh overlap
        var cols = GetComponentsInChildren<Collider>();
        foreach (var c in cols)
            c.enabled = true; // đảm bảo bật lại lúc spawn
    }
    // ZombieBrain.cs

    /*    void SafeSetStopped(bool stopped)
        {
            if (agent == null) return;
            if (!agent.enabled) return;
            if (!agent.isOnNavMesh) return;

            agent.isStopped = stopped;
        }*/


    public void SetBarricadeLane(BarricadeWindow window, Transform point, BarricadeLaneTrigger lane)
    {
        // Nếu đang giữ lane khác -> release trước
        if (_currentLaneTrigger != null && _currentLaneTrigger != lane)
            _currentLaneTrigger.Release(this);

        _currentLaneTrigger = lane;
        _currentBarricade = window;
        _barricadePoint = point;

        // reset grace khi nhận lane mới
        _barricadeJustClearedUntil = 0f;

//        Debug.Log($"[Brain] Enter lane for {window.name}");
    }

    public void ClearBarricadeLane(BarricadeWindow window)
    {
        if (_currentBarricade == window)
        {
            ResetAllObjectives(); // release lane + clear barricade refs
        }
    }

    void ResetAllObjectives(bool resetPath = false)
    {
        // Release slot nếu đang giữ
        if (_currentLaneTrigger != null)
        {
            _currentLaneTrigger.Release(this);
            _currentLaneTrigger = null;
        }

        _currentBarricade = null;
        _barricadePoint = null;
        _barricadeJustClearedUntil = 0f;

        if (resetPath && agent != null && agent.enabled && agent.isOnNavMesh)
            agent.ResetPath();
    }


    public bool HasBlockingBarricade
    {
        get
        {
            return _currentBarricade != null &&
                   _barricadePoint != null &&
                   _currentBarricade.CanTakeZombieHit();
        }
    }

    public Transform CurrentBarricadePoint => _barricadePoint;
    public BarricadeWindow CurrentBarricade => _currentBarricade;

    void OnGameOver()
    {
        _freezeByGameOver = true;

        // Clear target để AI không attack nữa
        if (bb != null)
        {
            bb.target = null;
            bb.distanceToTarget = 999f;
            bb.hasLOS = false;
        }

        // Dọn objective (barricade lane...) nhưng KHÔNG quan trọng bằng freeze nav
        ResetAllObjectives(resetPath: true);

        if (melee != null)
            melee.ResetExecutor();

        // ✅ QUAN TRỌNG: đóng băng movement/agent để nó không tự steer về đâu cả
        if (mover != null)
            mover.ForceFreezeNav();   // sau khi bạn public method

        // Optional: vẫn set state để audio/anim idle đúng
        ChangeState(State.Idle);

        // Không cần SetSpeed(0f) nữa vì ForceFreezeNav đã stop agent và reset path
    }

}
