using UnityEngine;
using NIX.Core.DesignPatterns;
using System;
using TT;

public class GameOverManager : SingletonBehaviour<GameOverManager>, IAutoDontDestroy
{
    [Header("Audio")]
    [SerializeField] private string gameOverUIEvent = "ui_game_over_defeat";

    [SerializeField] private bool logDebug = true;
    private bool _isGameOver;

    public static event Action OnGameOver;
    public bool IsGameOver => _isGameOver;


    protected override void Awake()
    {
        base.Awake();
    }

    void OnEnable()
    {
        PlayerRegistry.OnPlayerRegistered += OnPlayerRegistered;
        PlayerRegistry.OnPlayerUnregistered += OnPlayerUnregistered;
    }

    void OnDisable()
    {
        PlayerRegistry.OnPlayerRegistered -= OnPlayerRegistered;
        PlayerRegistry.OnPlayerUnregistered -= OnPlayerUnregistered;
    }

    void Start()
    {
        foreach (var p in PlayerRegistry.Players)
            HookPlayer(p);

        EvaluateGameOver(); // ✅ NEW: check ngay từ đầu
    }

    void OnPlayerRegistered(PlayerRefs refs)
    {
        HookPlayer(refs);
        EvaluateGameOver(); // ✅ NEW: registry đổi -> check lại
    }

    void OnPlayerUnregistered(PlayerRefs refs)
    {
        UnhookPlayer(refs);
        EvaluateGameOver(); // ✅ NEW: registry đổi -> check lại
    }

    void HookPlayer(PlayerRefs refs)
    {
        if (!refs) return;
        var life = refs.GetComponentInChildren<PlayerLifeController>(true);
        if (!life) return;

        // clear trước cho chắc
        life.OnDowned -= OnPlayerLifeChanged;
        life.OnDead -= OnPlayerLifeChanged;
        life.OnRevived -= OnPlayerLifeChanged;
        life.OnRespawned -= OnPlayerLifeChanged;

        life.OnDowned += OnPlayerLifeChanged;
        life.OnDead += OnPlayerLifeChanged;
        life.OnRevived += OnPlayerLifeChanged;
        life.OnRespawned += OnPlayerLifeChanged;
    }

    void UnhookPlayer(PlayerRefs refs)
    {
        if (!refs) return;
        var life = refs.GetComponentInChildren<PlayerLifeController>(true);
        if (!life) return;

        life.OnDowned -= OnPlayerLifeChanged;
        life.OnDead -= OnPlayerLifeChanged;
        life.OnRevived -= OnPlayerLifeChanged;
        life.OnRespawned -= OnPlayerLifeChanged;
    }

    void OnPlayerLifeChanged(PlayerLifeController life)
    {
        EvaluateGameOver();
    }

    void EvaluateGameOver()
    {
        if (_isGameOver) return;

        var players = PlayerRegistry.Players;
        if (players == null || players.Count == 0)
            return;

        int aliveCount = 0;
        int downedCount = 0;
        int deadCount = 0;

        foreach (var p in players)
        {
            if (!p) continue;
            var life = p.GetComponentInChildren<PlayerLifeController>(true);
            if (!life) continue;

            switch (life.state)
            {
                case LifeState.Alive: aliveCount++; break;
                case LifeState.Downed: downedCount++; break;
                case LifeState.Dead: deadCount++; break;
            }
        }

        // Điều kiện game over: không còn Alive, nhưng có ít nhất 1 Downed hoặc Dead
        bool shouldTrigger = (aliveCount == 0) && (downedCount + deadCount > 0);
        if (!shouldTrigger)
            return;

        // Chỉ máy "host" mới được convert DOWNED -> DEAD
        bool hostCanConvert = true;

#if FUSION_WEAVER
        var runner = GameObject.FindFirstObjectByType<Fusion.NetworkRunner>(FindObjectsInactive.Include);
        if (runner != null && runner.IsRunning)
        {
            hostCanConvert = runner.IsServer || runner.IsSharedModeMasterClient;
        }
#endif

        InputBlockerSystem.Add(InputBlocker.Full);
        // ⚠ MỌI máy đều gọi TriggerGameOver,
        // nhưng chỉ host mới thật sự convert state bên trong TriggerGameOver
        TriggerGameOver(hostCanConvert);
    }

    void TriggerGameOver(bool convertDownedToDead)
    {
        if (_isGameOver) return;
        _isGameOver = true;

        // (convert state giữ nguyên như bạn đang có)

        // 🔊 ENDGAME SFX – phát cho TẤT CẢ client
        if (!string.IsNullOrEmpty(gameOverUIEvent))
        {
            AudioEvents.PlayUiGlobal(gameOverUIEvent);
        }

#if FUSION_WEAVER
        if (convertDownedToDead)
        {
            var net = FindFirstObjectByType<GameOverNet>(FindObjectsInactive.Include);
            if (net != null)
            {
                net.FinalizeOnHost(RoundDirector.Instance);
            }
        }
#endif


        OnGameOver?.Invoke();
    }
}
