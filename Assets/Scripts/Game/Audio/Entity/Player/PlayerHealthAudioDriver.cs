using UnityEngine;
using TT; // AudioEvents, AudioEventSO

#if FUSION_WEAVER
using Fusion;
#endif

[DisallowMultipleComponent]
public class PlayerHealthAudioDriver : MonoBehaviour
{
    [Header("Profile")]
    public PlayerHealthAudioProfile profile;

    [Header("Refs")]
    [Tooltip("StateProvider để lấy IHealthState. Nếu trống sẽ auto-find.")]
    public PlayerStateProvider stateProvider;

    [Tooltip("LifeController để biết Alive/Downed/Dead + enableDownedInThisMode.")]
    public PlayerLifeController life;

    [Tooltip("Emitter cho SFX (nên là root/chest của world model). Nếu null sẽ dùng transform.")]
    public Transform audioEmitterOverride;

    [Header("Debug")]
    public bool debugLog;

    IHealthState _health;

    float _lastHealth = -1f;
    float _nextHurtTime;

    bool _lowHpActive;
    float _nextLowHpBreathTime;

    bool _fatalPlayedThisLife;

    AudioHandle _lowHpHandle;
    bool _lowHpLoopPlaying;

#if FUSION_WEAVER
    FusionNetBridge _bridge;
    NetworkObject _no;
#endif

    void Awake()
    {
        if (!stateProvider)
            stateProvider = GetComponent<PlayerStateProvider>();
        if (!life)
            life = GetComponentInParent<PlayerLifeController>();

#if FUSION_WEAVER
        _bridge = GetComponentInParent<FusionNetBridge>();
        _no = GetComponentInParent<NetworkObject>();
#endif
    }

    void OnEnable()
    {
        BindHealth();

        if (life != null)
        {
            life.OnDowned += OnLifeDowned;
            life.OnDead += OnLifeDead;
            life.OnRevived += OnLifeRevivedOrRespawned;
            life.OnRespawned += OnLifeRevivedOrRespawned;
        }
    }

    void OnDisable()
    {
        StopLowHpLoop(fade: false);

        if (_health != null)
        {
            _health.OnHealthChanged -= OnHealthChanged;
            _health.OnDeath -= OnHealthDeath;
            _health.OnRevive -= OnHealthRevive;
        }

        if (life != null)
        {
            life.OnDowned -= OnLifeDowned;
            life.OnDead -= OnLifeDead;
            life.OnRevived -= OnLifeRevivedOrRespawned;
            life.OnRespawned -= OnLifeRevivedOrRespawned;
        }
    }

    void Update()
    {
        if (profile == null) return;
    }

    void BindHealth()
    {
        _health = null;

        // 1) Ưu tiên lấy từ PlayerStateProvider (đã chọn SP/MP đúng mode)
        if (stateProvider && stateProvider.Health != null)
        {
            _health = stateProvider.Health;
        }

        // 2) Nếu vẫn null, quét tất cả MonoBehaviour con để tìm cái nào implement IHealthState
        if (_health == null)
        {
            var monos = GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var m in monos)
            {
                if (m is IHealthState hs)
                {
                    _health = hs;
                    break;
                }
            }
        }

        if (_health != null)
        {
            _health.OnHealthChanged += OnHealthChanged;
            _health.OnDeath += OnHealthDeath;
            _health.OnRevive += OnHealthRevive;

#if FUSION_WEAVER
            // MP: _health sẽ là MPHealthState (NetworkBehaviour) → KHÔNG được đọc Current/Max ở đây
            // vì OnEnable có thể chạy trước khi NetworkObject.Spawned() được gọi.
            if (_health is NetworkBehaviour)
            {
                // Để _lastHealth = -1f, lần OnHealthChanged đầu tiên sẽ coi như "init", delta = 0 → không play hurt.
                _lastHealth = -1f;
                // Không gọi UpdateLowHpActive với Current/Max ở đây để tránh đụng Net fields.
            }
            else
#endif
            {
                // SP: dùng luôn Current/Max hiện tại để init, giống behaviour cũ
                _lastHealth = _health.Current;
                UpdateLowHpActive(_health.Current, _health.Max);
            }

            if (debugLog)
                Debug.Log($"[PlayerHealthAudioDriver] Bound IHealthState={_health.GetType().Name} on {name}");
        }
        else
        {
            if (debugLog)
                Debug.LogWarning($"[PlayerHealthAudioDriver] No IHealthState found on {name}");
        }
    }


    // ===== Event từ Health =====

    void OnHealthChanged(float current, float max)
    {
        if (profile == null)
        {
            _lastHealth = current;
            return;
        }

        float prev = _lastHealth < 0f ? current : _lastHealth;
        float delta = prev - current; // >0 = mất máu

        // Cập nhật low HP flag
        UpdateLowHpActive(current, max);

        // Nếu lethal làm player chết/downed thì tránh chơi hurt nữa,
        // để dành cho fatal SFX.
        bool isDeadLike = (_health != null && (_health.IsDead || _health.IsDowned));
        if (!isDeadLike)
        {
            TryPlayHurt(delta);
        }

        _lastHealth = current;
    }

    void OnHealthDeath()
    {
        // IHealthState.OnDeath – thường trùng với Life.OnDead, nên dùng Life event cho logic chính
        if (debugLog)
            Debug.Log("[PlayerHealthAudioDriver] OnHealthDeath fired");
    }

    void OnHealthRevive()
    {
        // Placeholder – Life event sẽ xử lý reset
        if (debugLog)
            Debug.Log("[PlayerHealthAudioDriver] OnHealthRevive fired");
    }

    // ===== Event từ LifeController =====

    void OnLifeDowned(PlayerLifeController who)
    {
        if (profile == null) return;
        if (who != life) return;

        if (who != life) return;

        StopLowHpLoop(fade: false);

        // Chỉ nên chơi "fatal vocal" 1 lần / 1 mạng
        if (_fatalPlayedThisLife)
            return;

        // Nếu game có Downed mode (MP) thì đây là lethal đầu tiên
        if (life.enableDownedInThisMode)
        {
            var ev = profile.downedSFX != null ? profile.downedSFX : profile.deathSFX;
            PlayFatal(ev);
        }

    }

    void OnLifeDead(PlayerLifeController who)
    {
        if (profile == null) return;
        if (who != life) return;

        if (who != life) return;

        StopLowHpLoop(fade: false);

        if (_fatalPlayedThisLife)
            return;

        // Case 1: SP (enableDownedInThisMode == false) → Alive -> Dead trực tiếp
        // Case 2: MP nhưng vì lý do nào đó đi thẳng Alive -> Dead (không Downed)
        var ev = profile.deathSFX != null ? profile.deathSFX : profile.downedSFX;
        PlayFatal(ev);

        UpdateLowHpActive();
    }

    void OnLifeRevivedOrRespawned(PlayerLifeController who)
    {
        if (!IsAuthority) return;
        if (who != life) return;

        if (who != life) return;

        _fatalPlayedThisLife = false;
        // Khi revive/respawn thường HP sẽ hồi; OnHealthChanged sẽ lo low HP breathing
    }

    // ===== Core helpers =====

    void TryPlayHurt(float damageDelta)
    {
        if (damageDelta <= 0f)
            return;

        if (damageDelta < profile.minDamageToPlayHurt)
            return;

        if (Time.time < _nextHurtTime)
            return;

        // Nếu health báo Dead/Downed thì bỏ qua hurt
        if (_health != null && (_health.IsDead || _health.IsDowned))
            return;

        var ev = profile.GetRandomHurt();
        if (ev == null)
            return;

        var emitter = GetEmitter();
        if (emitter == null)
            return;

        AudioEvents.PlayWorld3DAttached(ev.eventId, emitter);

        _nextHurtTime = Time.time + Mathf.Max(0.01f, profile.minHurtInterval);

        if (debugLog)
            Debug.Log($"[PlayerHealthAudioDriver] Hurt SFX played (damage={damageDelta:F1}) on {name}");
    }

    void PlayFatal(AudioEventSO ev)
    {
        if (ev == null)
            return;

        var emitter = GetEmitter();
        if (emitter == null)
            return;

        AudioEvents.PlayWorld3DAttached(ev.eventId, emitter);
        _fatalPlayedThisLife = true;

        if (debugLog)
            Debug.Log($"[PlayerHealthAudioDriver] Fatal SFX played: {ev.name} on {name}");
    }

    void UpdateLowHpActive()
    {
        if (_health == null || profile == null)
        {
            _lowHpActive = false;
            return;
        }

        UpdateLowHpActive(_health.Current, _health.Max);
    }

    void UpdateLowHpActive(float current, float max)
    {
        if (profile == null || max <= 0f)
        {
            _lowHpActive = false;
            return;
        }

        float ratio = current / max;

        // CHỈ active khi HP thấp và NGƯỜI ĐANG ALIVE (không Dead, không Downed)
        bool newActive = (ratio <= profile.lowHpThreshold)
                         && _health != null
                         && !_health.IsDead
                         && !_health.IsDowned;

        if (newActive && !_lowHpActive)
        {
            // Vừa vào vùng low HP → schedule ngay tiếng thở sau 0–1s
            _nextLowHpBreathTime = Time.time + Random.Range(0.1f, 1.0f);
        }

        bool wasActive = _lowHpActive;
        _lowHpActive = newActive;

        if (_lowHpActive && !wasActive) StartLowHpLoop();
        else if (!_lowHpActive && wasActive) StopLowHpLoop(fade: true);
    }


    Transform GetEmitter()
    {
        if (audioEmitterOverride != null)
            return audioEmitterOverride;

        return transform;
    }

    bool IsAuthority
    {
        get
        {
#if FUSION_WEAVER
            // SP mode
            if (GameSession.Mode == AppPlayMode.Single)
                return true;

            // MP: ưu tiên FusionNetBridge
            if (_bridge == null)
                _bridge = GetComponentInParent<FusionNetBridge>();

            if (_bridge != null)
            {
                if (_bridge.Runner == null || !_bridge.Runner.IsRunning)
                    return true;

                return _bridge.HasStateAuth;
            }

            // Fallback: NetworkObject
            if (_no == null)
                _no = GetComponentInParent<NetworkObject>();

            if (_no == null || _no.Runner == null || !_no.Runner.IsRunning)
                return true;

            return _no.HasStateAuthority;
#else
            return true;
#endif
        }
    }

    void StartLowHpLoop()
    {
        if (_lowHpLoopPlaying) return;
        if (profile == null || profile.lowHpBreathSFX == null) return;

       
#if FUSION_WEAVER
        if (GameSession.Mode != AppPlayMode.Single && !IsLocalOwner) return;
#endif

        var emitter = GetEmitter();
        if (emitter == null) return;

        // 3D attached + có handle (để stop/fade)
        _lowHpHandle = AudioManager.Instance.Play3DAttachedHandle(profile.lowHpBreathSFX.eventId, emitter);
        _lowHpLoopPlaying = _lowHpHandle.IsValid;
    }

    void StopLowHpLoop(bool fade)
    {
        if (!_lowHpLoopPlaying) return;

        if (_lowHpHandle.IsValid && AudioManager.Instance != null)
        {
            if (fade) AudioManager.Instance.FadeOutAndStop(_lowHpHandle, 0.35f);
            else AudioManager.Instance.Stop(_lowHpHandle);
        }

        _lowHpHandle = default;
        _lowHpLoopPlaying = false;
    }

    bool IsLocalOwner
    {
        get
        {
#if FUSION_WEAVER
            // SP
            if (GameSession.Mode == AppPlayMode.Single) return true;

            if (_bridge == null) _bridge = GetComponentInParent<FusionNetBridge>();
            if (_bridge != null && _bridge.Runner != null && _bridge.Runner.IsRunning)
                return _bridge.IsLocalOwner;

            if (_no == null) _no = GetComponentInParent<NetworkObject>();
            if (_no != null && _no.Runner != null && _no.Runner.IsRunning)
                return _no.HasInputAuthority;

            return true; // fallback an toàn
#else
        return true;
#endif
        }
    }

}
