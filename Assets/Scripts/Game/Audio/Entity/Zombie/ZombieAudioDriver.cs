using UnityEngine;
using TT; // AudioEvents, AudioEventSO

#if FUSION_WEAVER
using Fusion;
#endif

/// <summary>
/// Điều khiển audio của 1 zombie: footstep + aggro + chase bark.
/// Mọi config/clip nằm trong ZombieAudioProfile.
/// </summary>
[DisallowMultipleComponent]
public class ZombieAudioDriver : MonoBehaviour, IPoolable
{
    [Header("Refs")]
    public ZombieBrain brain;
    public ZombieAudioProfile profile;

    [Header("Health (để nghe OnHit)")]
    [SerializeField] private DamageableHealth health;

    [Header("Death Audio")]
    bool _hasPlayedDeath;

    [Header("Hit Reaction")]
    [Tooltip("Thời gian tối thiểu giữa 2 tiếng rên khi bị trúng (per zombie).")]
    public float hitVocalCooldown = 0.3f;
    float _nextHitVocalTime;

    [Header("Global Footstep Limits (cho cả bầy)")]
    public int maxFootstepsPerFrame = 8;
    public float globalFootstepInterval = 0.03f;

    [Header("Global Vocal Limits (cho cả bầy)")]
    public int maxVoicesPerFrame = 4;
    public float globalVoiceInterval = 0.25f;

    // --- FOOTSTEP state ---
    float _nextLocalFootstepTime;
    static float s_nextGlobalFootstepTime = 0f;
    static int s_footstepFrame = -1;
    static int s_footstepsThisFrame = 0;

    // --- VOCAL state ---
    float _nextLocalVoiceTime;
    float _nextChaseBarkTime = Mathf.Infinity; // sẽ set khi vào Chase

    // phase ngẫu nhiên cho từng zombie để lệch nhịp vocal
    float _voicePhase; // 0..1

    static int s_voiceFrame = -1;
    static int s_voicesThisFrame = 0;
    static float s_nextGlobalVoiceTime = 0f;

    // Listener cache
    static Transform s_listener;
    static float s_nextListenerSearchTime = 0f;

#if FUSION_WEAVER

    NetworkObject _no;
    ZombieStateNet _stateNet;
    byte _lastNetState;

#endif

    void Awake()
    {
        if (!brain)  brain = GetComponent<ZombieBrain>();
        if (!health) health = GetComponent<DamageableHealth>();

#if FUSION_WEAVER
        _no = GetComponentInParent<NetworkObject>();
        _stateNet = GetComponent<ZombieStateNet>();
#endif


        if (profile != null)
        {
            // Phase ngẫu nhiên cho con này (giữ cố định suốt đời)
            _voicePhase = Random.Range(0f, 1f);

            // Phân tán local cooldown vocal ban đầu
            _nextLocalVoiceTime = Time.time + Random.Range(0f, profile.minLocalVoiceInterval);

            // Bark sẽ được schedule khi thật sự vào Chase
            _nextChaseBarkTime = Mathf.Infinity;
        }
    }

    void OnEnable()
    {
        if (!health) health = GetComponent<DamageableHealth>();
        if (health != null)
        {
            health.OnHit        += OnHealthHit;
            health.OnDeathLocal += OnHealthDeath;   // <<< THÊM
        }

#if FUSION_WEAVER
        if (GameSession.Mode != AppPlayMode.Single && _stateNet != null)
        {
            _stateNet.OnStateChanged += OnNetStateChanged;
            _stateNet.OnDeathPulse += OnNetDeathPulse;
            _stateNet.OnHitPlayerPulse += OnNetHitPlayerPulse;
        }
#endif

    }

    void OnDisable()
    {
        if (health != null)
        {
            health.OnHit        -= OnHealthHit;
            health.OnDeathLocal -= OnHealthDeath;   // <<< THÊM
        }

#if FUSION_WEAVER
        if (_stateNet != null)
        {
            _stateNet.OnStateChanged -= OnNetStateChanged;
            _stateNet.OnDeathPulse -= OnNetDeathPulse;
            _stateNet.OnHitPlayerPulse -= OnNetHitPlayerPulse;
        }
#endif

    }

    void Update()
    {
        if (!CanPlayCosmetic) return;
        if (!profile) return;

#if FUSION_WEAVER
        // MP client: brain có thể bị disabled -> dùng net state
        if (GameSession.Mode != AppPlayMode.Single && _stateNet != null)
        {
            if ((ZombieBrain.State)_stateNet.State == ZombieBrain.State.Chase)
                TryChaseBark();
            return;
        }
#endif

        // SP / (hoặc MP host nếu bạn vẫn muốn)
        if (!brain) return;
        if (brain.current == ZombieBrain.State.Chase)
            TryChaseBark();
    }


    // ============================================================
    // API cho Animator / Brain gọi
    // ============================================================

    /// <summary>Được gọi bởi AnimationEvent khi chân chạm đất.</summary>
    public void AnimEvent_Footstep()
    {
        if (!CanPlayCosmetic) return;
        if (profile == null) return;

        if (!IsWithinDistance(profile.maxFootstepDistance))
            return;

        // per-zombie footstep cooldown
        if (Time.time < _nextLocalFootstepTime)
            return;

        // global cooldown
        float gInterval = Mathf.Max(globalFootstepInterval, 0.01f);
        if (Time.time < s_nextGlobalFootstepTime)
            return;

        // reset counter theo frame
        if (s_footstepFrame != Time.frameCount)
        {
            s_footstepFrame = Time.frameCount;
            s_footstepsThisFrame = 0;
        }

        if (s_footstepsThisFrame >= maxFootstepsPerFrame)
            return;

        if (Random.value > profile.footstepPlayChance)
            return;

        var ev = GetFootstepEventForCurrentSurface();
        if (!ev) return;

        s_footstepsThisFrame++;

        _nextLocalFootstepTime = Time.time + Mathf.Max(profile.minFootstepInterval, 0.05f);
        s_nextGlobalFootstepTime = Time.time + gInterval;

        AudioEvents.PlayWorld3D(ev.eventId, transform.position);
    }

    /// <summary>ZombieBrain báo khi đổi state (Idle/Chase/Attack/HitReact/Death).</summary>
    public void OnStateChanged(ZombieBrain.State prev, ZombieBrain.State current)
    {
        if (!CanPlayCosmetic || profile == null) return;

        if (current == ZombieBrain.State.Chase && prev != ZombieBrain.State.Chase)
        {
            // Aggro hét ngay khi vào Chase (nếu đủ điều kiện)
            PlayAggroShout();

            // Bắt đầu schedule bark từ lúc này, dùng interval & phase riêng
            ScheduleNextChaseBark(profile.chaseBarkInterval.x, profile.chaseBarkInterval.y);
        }
    }

    // ============================================================
    // IPoolable - được gọi bởi ZombiePool khi reuse / trả về pool
    // ============================================================

    public void OnSpawned()
    {
        // Mỗi lần zombie được lấy lại từ pool là 1 "đời" mới
        // → cho phép play death VO lại.
        _hasPlayedDeath = false;

        // Các timer khác (hit, vocal, footstep) không bắt buộc phải reset:
        // - _nextHitVocalTime: so với Time.time, nếu đã trôi qua thì tự play được
        // - _nextLocalFootstepTime, _nextLocalVoiceTime: được update liên tục trong vòng đời
        // - _voicePhase: đã random trong Awake, giữ cố định cho "con" này là ổn
        //
        // Nếu sau này bạn thấy cần "fresh" hơn cho 1 số timer,
        // có thể thêm reset ở đây (ví dụ _nextChaseBarkTime = Mathf.Infinity).
    }

    public void OnDespawned()
    {
        // Hiện tại ZombieAudioDriver không giữ AudioHandle,
        // nên không cần dọn gì đặc biệt.
        // Nếu sau này có handle (loop VO, scream kéo dài...) thì stop/clear ở đây.
    }


    // ============================================================
    // VOCAL IMPLEMENT
    // ============================================================

    void TryChaseBark()
    {
        if (profile == null) return;

        if (Time.time < _nextChaseBarkTime)
            return;

        float maxDist = GetVocalMaxDistance(profile.chaseMaxDistance);
        if (!IsWithinDistance(maxDist))
        {
            // Quá xa: delay lần sau 1 khoảng ngẫu nhiên (ngắn hơn interval chính)
            ScheduleNextChaseBark(
                profile.chaseBarkInterval.x * 0.5f,
                profile.chaseBarkInterval.y * 0.8f
            );
            return;
        }

        // Local cooldown chưa xong -> delay
        if (!TryReserveLocalVoice())
        {
            ScheduleNextChaseBark(
                profile.chaseBarkInterval.x * 0.5f,
                profile.chaseBarkInterval.y
            );
            return;
        }

        // Global limiter chặn -> delay
        if (!CanPlayGlobalVoice())
        {
            ScheduleNextChaseBark(
                profile.chaseBarkInterval.x * 0.5f,
                profile.chaseBarkInterval.y
            );
            return;
        }

        var ev = GetRandomEvent(profile.chaseBarks, profile.chaseBarkCollection);
        if (!ev)
        {
            ScheduleNextChaseBark(
                profile.chaseBarkInterval.y,
                profile.chaseBarkInterval.y * 1.5f
            );
            return;
        }

        // THÀNH CÔNG: play & đặt lịch lần sau đúng theo interval config
        AudioEvents.PlayWorld3DAttached(ev.eventId, transform);

        ScheduleNextChaseBark(profile.chaseBarkInterval.x, profile.chaseBarkInterval.y);
    }

    void PlayAggroShout()
    {
        if (profile == null) return;

        if (Random.value > profile.aggroShoutChance)
            return;

        float maxDist = GetVocalMaxDistance(profile.aggroMaxDistance);
        if (!IsWithinDistance(maxDist))
            return;

        if (!TryReserveLocalVoice())
            return;

        if (!CanPlayGlobalVoice())
            return;

        var ev = GetRandomEvent(profile.aggroShouts, profile.aggroCollection);
        if (!ev) return;

        AudioEvents.PlayWorld3DAttached(ev.eventId, transform);
    }

    void OnHealthHit(DamageEvent e, DamageResult result)
    {
        if (!CanPlayCosmetic) return;
        if (profile == null) return;

        // Nếu hit này giết luôn thì để dành death VO làm sau
        if (result.isFatal) return;

        if (Time.time < _nextHitVocalTime)
            return;

        var ev = GetRandomEvent(profile.hitReactions, profile.hitReactionCollection);
        if (!ev) return;

        // cooldown per-zombie cho tiếng đau
        _nextHitVocalTime = Time.time + hitVocalCooldown;

        // dùng chung logic distance vocal
        float maxDist = GetVocalMaxDistance(
            profile.vocalMaxDistanceOverride > 0 ? profile.vocalMaxDistanceOverride : profile.chaseMaxDistance);

        if (!IsWithinDistance(maxDist))
            return;

        AudioEvents.PlayWorld3DAttached(ev.eventId, transform);
    }

    void OnHealthDeath(DamageEvent e, DamageResult result)
    {
        if (!CanPlayCosmetic) return;
        if (profile == null) return;
        if (_hasPlayedDeath) return;   // tránh double nếu OnDeathLocal bắn 2 lần

        _hasPlayedDeath = true;

        // Random chance để có con gào, có con tắt hơi luôn
        if (Random.value > profile.deathVocalChance)
            return;

        var ev = GetRandomEvent(profile.deathVocals, profile.deathVocalCollection);
        if (!ev) return;

        float maxDist = GetDeathMaxDistance();
        if (!IsWithinDistance(maxDist))
            return;

        // Death vocal: 3D tại vị trí zombie, không cần attach
        AudioEvents.PlayWorld3D(ev.eventId, transform.position);
    }

    // Gọi khi bắt đầu 1 đòn attack (sau khi trigger anim)
    public void OnAttackWindup()
    {
        if (!CanPlayCosmetic || profile == null) return;
        if (Random.value > profile.attackVocalChance) return;

        var ev = GetRandomEvent(profile.attackVocals, profile.attackVocalCollection);
        if (!ev) return;

        float maxDist = GetAttackMaxDistance();
        if (!IsWithinDistance(maxDist)) return;

        // dùng chung limiter vocal global cho vocal attack
        if (!CanPlayGlobalVoice()) return;

        AudioEvents.PlayWorld3DAttached(ev.eventId, transform);
    }

    // Gọi từ Animation Event trong clip attack (vung tay)
    public void AnimEvent_AttackSwing()
    {
        if (!CanPlayCosmetic || profile == null) return;
        if (Random.value > profile.attackSwingChance) return;

        var ev = GetRandomEvent(profile.attackSwings, profile.attackSwingCollection);
        if (!ev) return;

        float maxDist = GetAttackMaxDistance();
        if (!IsWithinDistance(maxDist)) return;

        AudioEvents.PlayWorld3DAttached(ev.eventId, transform);
    }

    // Gọi từ MeleeExecutor khi thực sự đánh trúng PLAYER
    public void OnAttackHitPlayer()
    {
        if (!CanPlayCosmetic || profile == null) return;
        if (Random.value > profile.attackHitPlayerChance) return;

        var ev = GetRandomEvent(profile.attackHitPlayer, profile.attackHitPlayerCollection);
        if (!ev) return;

        float maxDist = GetAttackMaxDistance();
        if (!IsWithinDistance(maxDist)) return;

        AudioEvents.PlayWorld3DAttached(ev.eventId, transform);
    }

    public void AnimEvent_DeathBodyFall()
    {
        if (!CanPlayCosmetic || profile == null) return;

        var ev = GetRandomEvent(profile.bodyFallSfx, profile.bodyFallCollection);
        if (!ev) return;

        float maxDist = GetDeathMaxDistance();
        if (!IsWithinDistance(maxDist))
            return;

        // Body fall: cũng 3D tại vị trí, không attach
        AudioEvents.PlayWorld3D(ev.eventId, transform.position);
    }


    float GetAttackMaxDistance()
    {
        if (profile != null && profile.attackMaxDistanceOverride > 0f)
            return profile.attackMaxDistanceOverride;

        // fallback: dùng chase distance
        return profile != null ? profile.chaseMaxDistance : 30f;
    }

    float GetDeathMaxDistance()
    {
        if (profile != null && profile.deathMaxDistanceOverride > 0f)
            return profile.deathMaxDistanceOverride;

        // fallback: dùng vocal override nếu có, không thì chase distance
        if (profile != null && profile.vocalMaxDistanceOverride > 0f)
            return profile.vocalMaxDistanceOverride;

        return profile != null ? profile.chaseMaxDistance : 30f;
    }

    // ============================================================
    // HELPERS
    // ============================================================

    void ScheduleNextChaseBark(float minInterval, float maxInterval)
    {
        if (profile == null) return;

        minInterval = Mathf.Max(0.1f, minInterval);
        maxInterval = Mathf.Max(minInterval, maxInterval);

        // interval base cho con này
        float baseInterval = Random.Range(minInterval, maxInterval);
        // phase riêng 0.7–1.3
        float phaseScale = Mathf.Lerp(0.7f, 1.3f, _voicePhase);

        _nextChaseBarkTime = Time.time + baseInterval * phaseScale;
    }

    AudioEventSO GetFootstepEventForCurrentSurface()
    {
        if (profile == null) return null;

        Vector3 origin = transform.position + Vector3.up * profile.footRaycastHeight;

        if (Physics.Raycast(origin, Vector3.down, out var hit, profile.footRaycastDistance,
                            profile.footstepMask, QueryTriggerInteraction.Ignore))
        {
            var surfMat = hit.collider.GetComponent<SurfaceMaterial>();
            var surfType = surfMat ? surfMat.type : SurfaceType.Default;

            var arr = profile.surfaceFootsteps;
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i].surface == surfType && arr[i].eventSO != null)
                    return arr[i].eventSO;
            }
        }

        return profile.defaultFootstep;
    }

    static AudioEventSO GetRandomEvent(AudioEventSO[] arr, AudioEventCollection col)
    {
        if (arr != null && arr.Length > 0)
        {
            if (arr.Length == 1) return arr[0];
            int idx = Random.Range(0, arr.Length);
            return arr[idx];
        }

        if (col != null && col.events != null && col.events.Length > 0)
        {
            if (col.events.Length == 1) return col.events[0];
            int idx = Random.Range(0, col.events.Length);
            return col.events[idx];
        }

        return null;
    }

    float GetVocalMaxDistance(float profileMax)
    {
        if (profile != null && profile.vocalMaxDistanceOverride > 0f)
            return profile.vocalMaxDistanceOverride;
        return profileMax;
    }

    bool IsWithinDistance(float maxDist)
    {
        if (maxDist <= 0f) return true;

        var lis = GetListener();
        if (!lis) return true;

        float sq = (transform.position - lis.position).sqrMagnitude;
        return sq <= maxDist * maxDist;
    }

    static Transform GetListener()
    {
        if (s_listener && s_listener.gameObject.activeInHierarchy)
            return s_listener;

        if (Time.time < s_nextListenerSearchTime)
            return s_listener;

        s_nextListenerSearchTime = Time.time + 0.5f;

        var cam = Camera.main;
        if (cam)
            s_listener = cam.transform;

        return s_listener;
    }

    bool TryReserveLocalVoice()
    {
        float interval = profile != null
            ? Mathf.Max(profile.minLocalVoiceInterval, 3f)
            : 4f;

        if (Time.time < _nextLocalVoiceTime)
            return false;

        _nextLocalVoiceTime = Time.time + interval;
        return true;
    }

    bool CanPlayGlobalVoice()
    {
        float interval = Mathf.Max(globalVoiceInterval, 0.1f);
        if (Time.time < s_nextGlobalVoiceTime)
            return false;

        if (s_voiceFrame != Time.frameCount)
        {
            s_voiceFrame = Time.frameCount;
            s_voicesThisFrame = 0;
        }

        if (s_voicesThisFrame >= maxVoicesPerFrame)
            return false;

        s_voicesThisFrame++;
        s_nextGlobalVoiceTime = Time.time + interval;
        return true;
    }

    bool CanPlayCosmetic
    {
        get
        {
#if FUSION_WEAVER
            if (GameSession.Mode == AppPlayMode.Single)
                return true;

            // Trong MP: âm thanh zombie nên phát trên mọi client để mỗi máy tự spatialize theo listener của họ
            return true;
#else
        return true;
#endif
        }
    }

#if FUSION_WEAVER
    void OnNetStateChanged(byte prev, byte cur)
    {
        // Convert byte -> ZombieBrain.State
        var p = (ZombieBrain.State)prev;
        var c = (ZombieBrain.State)cur;
        OnStateChanged(p, c); // reuse logic cũ của bạn
    }
#endif

#if FUSION_WEAVER
    void OnNetDeathPulse()
    {
        // reuse logic y hệt OnHealthDeath nhưng không có DamageEvent
        if (!CanPlayCosmetic) return;
        if (profile == null) return;
        if (_hasPlayedDeath) return;

        _hasPlayedDeath = true;

        if (Random.value > profile.deathVocalChance)
            return;

        var ev = GetRandomEvent(profile.deathVocals, profile.deathVocalCollection);
        if (!ev) return;

        float maxDist = GetDeathMaxDistance();
        if (!IsWithinDistance(maxDist))
            return;

        AudioEvents.PlayWorld3D(ev.eventId, transform.position);
    }
#endif


#if FUSION_WEAVER
    void OnNetHitPlayerPulse()
    {
        // reuse logic sẵn có
        OnAttackHitPlayer();
    }
#endif

}
