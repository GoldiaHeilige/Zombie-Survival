using UnityEngine;
using System.Collections;
using Fusion;
using System;
using TT;
using System.Linq;

/// RoundDirector (multi-wave, playlist + procedural + special-list + block alternating)
public class RoundDirector : MonoBehaviour
{
    public static RoundDirector Instance { get; private set; }

    [Header("Refs")]
    public SpawnManager spawnManager;

    [Header("Wave Source")]
    public WaveSet waveSet;

    [Header("Loop")]
    [Min(0.05f)] public float tickSeconds = 0.35f;

    [Header("Enemy HP Scaling (by round)")]
    public bool enableEnemyHpScaling = true;

    [Min(1)] public int enemyHpRampRounds = 10;     // ramp trong 10 round đầu
    [Min(1f)] public float enemyHpMultiplierAtRampEnd = 2f; // round 10 = x2
    [Min(1f)] public float enemyHpMultiplierCap = 2f;       // cap cứng, round >= 10 vẫn x2

    [Header("Enemy Mix Scaling (by round)")]
    public bool enableEnemyMixScaling = true;

    [Tooltip("EnemyDefinition.id của Sprinter (ví dụ: 'sprinter').")]
    public string sprinterId = "sprinter";

    [Tooltip("Round bắt đầu tăng tỷ trọng sprinter.")]
    [Min(1)] public int sprinterRampStartRound = 5;

    [Tooltip("Round kết thúc ramp (từ đây trở đi giữ max).")]
    [Min(1)] public int sprinterRampEndRound = 25;

    [Tooltip("Multiplier tối đa áp lên EnemyDefinition.weight của sprinter ở cuối ramp.")]
    [Min(1f)] public float sprinterWeightMultiplierAtRampEnd = 3f;

    public float appliedHpMultiplier { get; private set; } = 1f;

    [Header("Start Settings")]
    [Tooltip("Thời gian auto-start khi vừa vào game (giây).")]
    public float autoStartDelaySeconds = 300f; // 5 phút
    [Tooltip("Nếu người chơi bấm bắt đầu sớm, đếm ngược cooldown này (giây).")]
    public float manualCooldownSeconds = 5f;

    [Header("Safety")]
    public bool useGlobalDamageDeathHook = true;

    [Header("Audio")]
    [Tooltip("eventName (AudioEventSO.eventName) cho SFX UI khi BẮT ĐẦU round mới.")]
    public string roundStartUIEvent = "ui_round_start";

    [Tooltip("eventName cho SFX UI khi KẾT THÚC round hiện tại.")]
    public string roundEndUIEvent = "ui_round_end";

    [Tooltip("eventName cho SFX UI khi BẮT ĐẦU round ĐẦU TIÊN (nếu để trống sẽ dùng roundStartUIEvent).")]
    public string roundFirstStartUIEvent = "";

    [Header("Audio - Milestone (every N rounds)")]
    [Tooltip("Mỗi N round (5/10/15/...) sẽ phát SFX đặc biệt cho Round Start/End. 0 = tắt.")]
    [Min(0)] public int milestoneEveryNRounds = 5;

    [Tooltip("eventName cho SFX UI khi BẮT ĐẦU milestone round (vd: round 5/10/15...).")]
    public string milestoneRoundStartUIEvent = "ui_round_start_milestone";

    [Tooltip("eventName cho SFX UI khi KẾT THÚC milestone round (vd: round 5/10/15...).")]
    public string milestoneRoundEndUIEvent = "ui_round_end_milestone";

    [Tooltip("Cap cứng cho weight hiệu lực của sprinter sau khi nhân ramp. 0 = không cap.")]
    [Min(0f)] public float sprinterMaxEffectiveWeight = 8f;

    [Header("Early Wave End (Anti-kite)")]
    [Tooltip("Tỉ lệ ngân sách CÒN LẠI để bắt đầu cho phép kết thúc wave sớm (ví dụ 0.15 = còn 15%).")]
    [Range(0f, 0.9f)]
    public float earlyEndRemainingBudgetFraction = 0.15f;

    [Tooltip("Số zombie tối đa còn sống để được tính là 'đuôi wave' (ví dụ 2 con cuối).")]
    public int earlyEndTrailingZombiesCap = 2;

    [Tooltip("Thời gian đếm ngược trước khi kết thúc wave sớm (giây). 0 = kết thúc ngay).")]
    public float earlyEndDelaySeconds = 20f;

    [Header("Runtime (read only)")]
    public int roundIndex = 0;
    public int alive = 0;
    public int spentBudget = 0;


    /// <summary>Bắn khi bắt đầu một round mới (1,2,3,...).</summary>
    public event Action<int> OnRoundChanged;
    public event Action<int> OnRoundEnded;

    AIPortHub _hub;
    [SerializeField] bool requirePorts = true;

    // Cho HUD đọc profile hiện tại
    public WaveProfile currentWave { get; private set; }

    // Nguồn wave để HUD/Debug
    public enum WaveSource { None, Fixed, Procedural, Special }
    public WaveSource currentSource { get; private set; } = WaveSource.None;

    [Header("Procedural Jitter (chỉ áp cho Procedural)")]
    [Tooltip("Jitter tỉ lệ ngân sách (0..0.5) -> ±% quanh giá trị tính toán.")]
    [Range(0f, 0.5f)] public float budgetJitterPct = 0.10f; // ±10%
    [Tooltip("Jitter số slot concurrency (+/-).")]
    [Range(0, 5)] public int capJitterMax = 1;               // ±1
    [Tooltip("Bật để log jitter áp dụng (debug).")]
    public bool logJitter = false;

    // HUD đọc thông tin jitter đã áp dụng vào wave hiện tại
    // HUD đọc thông tin jitter đã áp dụng vào wave hiện tại
    public float lastBudgetJitterFactor { get; private set; } = 1f; // ví dụ 0.93 ~ -7%
    public int lastCapJitterDelta { get; private set; } = 0;

    [Header("Player-count Scaling (snapshot per round)")]
    [Tooltip("Số người chơi được snapshot tại START của round (Host). Thay đổi join/leave sẽ chỉ áp dụng ở round kế tiếp.")]
    public int scaledPlayerCount { get; private set; } = 1;

    // Applied scaling values (để HUD debug)
    public float appliedBudgetMultiplier { get; private set; } = 1f;
    public int appliedCapAdd { get; private set; } = 0;
    public int appliedMaxCapAdd { get; private set; } = 0;
    public int appliedMaxConcurrency { get; private set; } = 0;
    public int appliedMaxBudgetAdd { get; private set; } = 0;
    public int appliedMaxBudget { get; private set; } = 0;


    // UI / state setup
    public bool isInSetup = false;
    public bool hasGameStarted = false;
    public float setupTimeRemaining = 0f; // hiển thị countdown còn lại (auto hoặc cooldown)

    bool _inCombat = false;
    bool _isRunning = false;
    bool _manualRequested = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // KHÔNG check port ở Awake
        _hub = FindFirstObjectByType<AIPortHub>(FindObjectsInactive.Include);
    }

    bool ShouldRunAsAuthority()
    {
#if FUSION_WEAVER
        var runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        // Nếu đang Multiplayer (runner đang chạy) thì chỉ Server/Host mới được chạy
        if (runner != null && runner.IsRunning)
            return runner.IsServer;
#endif
        // Không có runner => coi như SP/offline, cho chạy
        return true;
    }



    void OnEnable()
    {
        // Host-only khi đang chạy Fusion
        if (!ShouldRunAsAuthority())
        {
            // đảm bảo không có coroutine nào chạy dở
            StopAllCoroutines();
            enabled = false;
            return;
        }

        ResetState();
        StartCoroutine(Boot());

        if (useGlobalDamageDeathHook)
        {
            DamageSystem.OnReady -= HookDamageDeath;
            DamageSystem.OnReady += HookDamageDeath;

            if (DamageSystem.Instance != null)
                HookDamageDeath(DamageSystem.Instance);
        }

        GameOverManager.OnGameOver += OnGameOver;
    }

    IEnumerator Boot()
    {
        // Wait ports trước khi chạy MainLoop
        yield return WaitForPortsOrDisable();

        if (!enabled) yield break; // timeout / thiếu port
        yield return MainLoop();
    }

    IEnumerator WaitForPortsOrDisable()
    {
        if (!requirePorts) yield break;

        float logEvery = 2f;
        float nextLog = 0f;

        while (true)
        {
            if (_hub == null)
                _hub = FindFirstObjectByType<AIPortHub>(FindObjectsInactive.Include);

            if (_hub != null && _hub.Random != null)
                yield break;

            if (Time.unscaledTime >= nextLog)
            {
                Debug.LogWarning("[RoundDirector] Waiting for AIPortHub/Random port… (scene load / runner init may be slow)", this);
                nextLog = Time.unscaledTime + logEvery;
            }

            yield return null;
        }
    }




    int GetPlayerCountNow()
    {
#if FUSION_WEAVER
        // Ưu tiên Runner nếu đang chạy (host/server)
        var runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        if (runner != null && runner.IsRunning)
        {
            try { return Mathf.Max(1, runner.ActivePlayers.Count()); }

            catch { /* fallback below */ }
        }
#endif
        // Fallback: dựa theo registry (nếu bạn dùng PlayerRegistry)
        try
        {
            var c = PlayerRegistry.Players != null ? PlayerRegistry.Players.Count : 0;
            return Mathf.Max(1, c);
        }
        catch
        {
            return 1;
        }
    }

    void RefreshScalingSnapshotForNewRound()
    {
        int pc = GetPlayerCountNow();
        scaledPlayerCount = Mathf.Max(1, pc);

        if (waveSet != null && waveSet.enableMpScaling && scaledPlayerCount > 1)
        {
            int extra = scaledPlayerCount - 1;
            appliedBudgetMultiplier = 1f + extra * waveSet.mpBudgetMulPerExtraPlayer;
            appliedCapAdd = extra * waveSet.mpCapAddPerExtraPlayer;
            appliedMaxCapAdd = extra * waveSet.mpMaxCapAddPerExtraPlayer;
            appliedMaxBudgetAdd = waveSet.mpMaxBudgetAddPerExtraPlayer * extra;
        }
        else
        {
            appliedBudgetMultiplier = 1f;
            appliedCapAdd = 0;
            appliedMaxCapAdd = 0;
            appliedMaxBudgetAdd = 0;
        }

        // --- Enemy HP scaling (by round, capped) ---
        if (enableEnemyHpScaling)
        {
            int r = Mathf.Max(1, roundIndex);

            // t = 0 ở round 1, t = 1 ở round enemyHpRampRounds
            float denom = Mathf.Max(1, enemyHpRampRounds - 1);
            float t = Mathf.Clamp01((r - 1) / denom);

            float roundMul = Mathf.Lerp(1f, enemyHpMultiplierAtRampEnd, t);

            // cap cứng
            appliedHpMultiplier = Mathf.Min(roundMul, enemyHpMultiplierCap);
        }
        else
        {
            appliedHpMultiplier = 1f;
        }

        if (waveSet != null && waveSet.enableMpScaling && waveSet.enableMpEnemyHpScaling && scaledPlayerCount > 1)
        {
            int extra = scaledPlayerCount - 1;
            float hpMulByPlayers = 1f + extra * waveSet.mpEnemyHpMulPerExtraPlayer;
            hpMulByPlayers = Mathf.Clamp(hpMulByPlayers, 1f, Mathf.Max(1f, waveSet.mpEnemyHpPlayerCap));
            appliedHpMultiplier *= hpMulByPlayers;
        }

        appliedMaxConcurrency = (waveSet != null)
            ? (waveSet.maxConcurrency + appliedMaxCapAdd)
            : 0;

        // ✅ ADD: budget cap snapshot per-round
        if (waveSet != null && waveSet.useBudgetCap)
            appliedMaxBudget = Mathf.Max(1, waveSet.maxBudget + appliedMaxBudgetAdd);
        else
            appliedMaxBudget = int.MaxValue;
    }

    WaveProfile CloneWaveProfile(WaveProfile src)
    {
        if (src == null) return null;
        var wp = ScriptableObject.CreateInstance<WaveProfile>();
        wp.budgetPoints = src.budgetPoints;
        wp.concurrencyCap = src.concurrencyCap;
        wp.spawnBurst = src.spawnBurst;
        wp.interBurstDelay = src.interBurstDelay;

        if (src.allowTypes != null && src.allowTypes.Count > 0)
            wp.allowTypes.AddRange(src.allowTypes);

        return wp;
    }

    WaveProfile ApplyPlayerScalingToWave(WaveProfile wp, bool isSpecialWave)
    {
        if (wp == null) return null;

        // Không scale special nếu tắt
        if (isSpecialWave && waveSet != null && !waveSet.mpScaleSpecialWaves)
            return wp;

        // Budget
        int budget = Mathf.Max(1, Mathf.RoundToInt(wp.budgetPoints * appliedBudgetMultiplier));

        if (waveSet != null && waveSet.useBudgetCap)
            budget = Mathf.Min(budget, appliedMaxBudget);   // ✅ clamp trần (MP đã nới trần ở snapshot)

        wp.budgetPoints = budget;


        // Cap + MaxCap
        int maxCap = (waveSet != null) ? (waveSet.maxConcurrency + appliedMaxCapAdd) : wp.concurrencyCap;
        maxCap = Mathf.Max(1, maxCap);

        int cap = wp.concurrencyCap + appliedCapAdd;
        cap = Mathf.Clamp(cap, 1, maxCap);
        wp.concurrencyCap = cap;

        // Extra safety: burst không vượt cap
        wp.spawnBurst = Mathf.Clamp(wp.spawnBurst, 1, wp.concurrencyCap);

        return wp;
    }

    void OnDisable()
    {
        StopAllCoroutines();

        if (useGlobalDamageDeathHook)
        {
            DamageSystem.OnReady -= HookDamageDeath;
            if (DamageSystem.Instance != null)
                DamageSystem.Instance.OnDeath -= OnAnyDeath;
        }

        GameOverManager.OnGameOver -= OnGameOver;
    }

    void OnGameOver()
    {
        Debug.Log("[Director] GameOver received → stopping RoundDirector.");

        // Dừng vòng lặp wave hiện tại
        _isRunning = false;
        _inCombat = false;

        // Dừng toàn bộ coroutine (MainLoop, setup, v.v.)
        StopAllCoroutines();

        // Tuỳ bạn: nếu SpawnManager có hàm riêng dừng spawn thì gọi thêm:
        // if (spawnManager) spawnManager.StopAllSpawns();
    }

    void ResetState()
    {
        roundIndex = 0;
        alive = 0;
        spentBudget = 0;
        _inCombat = false;
        _isRunning = false;
        _manualRequested = false;
        hasGameStarted = false;
        isInSetup = false;
        setupTimeRemaining = 0f;
        currentWave = null;
        currentSource = WaveSource.None;
        lastBudgetJitterFactor = 1f;
        lastCapJitterDelta = 0;
    }

    // --- API cho UI / console / trigger gọi manual start ---
    public void RequestManualStart()
    {
        if (hasGameStarted) return;
        _manualRequested = true;
        Debug.Log("[Director] Manual start requested → begin cooldown.");
    }

    public void ForceStart()  // phím F5 (nếu bạn muốn)
    {
        if (hasGameStarted) return;
        _manualRequested = true;
        manualCooldownSeconds = 0f;
        Debug.Log("[Director] ForceStart → skip cooldown.");
    }

    IEnumerator MainLoop()
    {
        if (spawnManager == null || waveSet == null)
        {
            Debug.LogError("[Director] Missing refs (SpawnManager/WaveSet).");
            yield break;
        }

        _isRunning = true;

        // 1) ĐỢI PLAYER
        Debug.Log("[Director] Waiting for Player to bind...");
        while (!spawnManager.HasPlayer())
        {
            yield return new WaitForSeconds(0.2f);
        }
        Debug.Log("[Director] Player detected.");

        // 2) BỎ SETUP PHASE, THAY BẰNG ĐẾM NGƯỢC NGẮN (ví dụ 10 giây)
        // Hoặc nếu muốn vào luôn thì bỏ luôn đoạn đếm ngược
        float countdownToStart = 5f; // Hoặc 0f nếu muốn vào wave ngay
        while (countdownToStart > 0f)
        {
            countdownToStart -= Time.deltaTime;
            setupTimeRemaining = countdownToStart; // Cho HUD hiển thị nếu cần
            yield return null;
        }

        // 3) LOOP QUA CÁC WAVE
        MatchClock.Begin();
        hasGameStarted = true;

        // Đảm bảo bắt đầu từ 0 trước khi vào vòng lặp
        if (roundIndex < 0) roundIndex = 0;

        while (_isRunning)
        {
            // Tăng round và bắn event cho HUD
            roundIndex++;
            OnRoundChanged?.Invoke(roundIndex);

#if FUSION_WEAVER
            if (RoundStateNet.Instance != null)
                RoundStateNet.Instance.Host_SetRound(roundIndex);
#endif

            // Lấy wave + nguồn cho round hiện tại
            WaveSource src;
            RefreshScalingSnapshotForNewRound();

            WaveProfile wave = ResolveWaveForRound(roundIndex, out src);

            if (wave == null || wave.allowTypes == null || wave.allowTypes.Count == 0)
            {
                Debug.LogWarning($"[Director] No wave data for round {roundIndex}. Stop.");
                break; // dừng hẳn nếu không có dữ liệu nào
            }

            PlayRoundStartSfx();

            currentWave = wave;     // HUD đọc
            currentSource = src;    // HUD đọc

            // Reset counters cho wave mới
            alive = 0;
            spentBudget = 0;

            _inCombat = true;
            Debug.Log($"[Director] === WAVE {roundIndex} START === (src={currentSource}, budget={wave.budgetPoints}, cap={wave.concurrencyCap})");

            float nextBurstAt = 0f;
            float earlyEndTimer = -1f;   // <— timer cho kết thúc sớm

            while (_inCombat)
            {
                // 3.1) Spawn thêm nếu đủ điều kiện
                if (Time.time >= nextBurstAt &&
                    alive < wave.concurrencyCap &&
                    spentBudget < wave.budgetPoints)
                {
                    var res = spawnManager.SpawnBurst(wave, alive, spentBudget, roundIndex);

                    if (res.spawnedCount == 0 && res.totalCost == 0)
                    {
                        Debug.LogWarning("[Director] SpawnBurst returned 0. " +
                                         "Check WaveProfile.allowTypes & prefab & SpawnPoints/NavMesh.");
                    }

                    nextBurstAt = Time.time + Mathf.Max(0.05f, wave.interBurstDelay);
                }

                // 3.2) Kết thúc wave SỚM khi:
                // - đã dùng >= (1 - earlyEndRemainingBudgetFraction) ngân sách
                // - và số zombie sống <= earlyEndTrailingZombiesCap
                if (wave.budgetPoints > 0 &&
                    earlyEndRemainingBudgetFraction > 0f && earlyEndRemainingBudgetFraction < 1f)
                {
                    int threshold = Mathf.RoundToInt(wave.budgetPoints * (1f - earlyEndRemainingBudgetFraction));

                    if (spentBudget >= threshold && alive <= earlyEndTrailingZombiesCap)
                    {
                        if (earlyEndDelaySeconds <= 0f)
                        {
                            // Kết thúc ngay
                            _inCombat = false;
                            Debug.Log($"[Director] Wave {roundIndex} early-complete (spent={spentBudget}/{wave.budgetPoints}, alive={alive}).");
                        }
                        else
                        {
                            if (earlyEndTimer < 0f)
                            {
                                earlyEndTimer = earlyEndDelaySeconds;
                                Debug.Log($"[Director] Wave {roundIndex} early-end countdown started: {earlyEndTimer:0.0}s (spent={spentBudget}/{wave.budgetPoints}, alive={alive}).");
                            }
                            else
                            {
                                earlyEndTimer -= tickSeconds;
                                if (earlyEndTimer <= 0f)
                                {
                                    _inCombat = false;
                                    Debug.Log($"[Director] Wave {roundIndex} early-complete after countdown (spent={spentBudget}/{wave.budgetPoints}, alive={alive}).");
                                }
                            }
                        }
                    }
                    else
                    {
                        // Chưa đủ điều kiện hoặc zombie tăng lên lại → reset timer
                        earlyEndTimer = -1f;
                    }
                }

                // 3.3) Kết thúc wave: đã xài hết budget và không còn zombie sống (rule cũ)
                if (spentBudget >= wave.budgetPoints && alive <= 0)
                {
                    _inCombat = false;
                    Debug.Log($"[Director] Wave {roundIndex} complete!");
                }

                yield return new WaitForSeconds(tickSeconds);
            }

            OnRoundEnded?.Invoke(roundIndex);

#if FUSION_WEAVER
            if (RoundStateNet.Instance != null)
                RoundStateNet.Instance.Host_EndRound(roundIndex);
#endif

            PlayRoundEndSfx();

            // Clear info wave cho HUD trong thời gian nghỉ
            currentWave = null;
            currentSource = WaveSource.None;

            // 4) NGHỈ GIỮA WAVE
            float delay = Mathf.Max(0f, waveSet.interWaveDelay);
            if (delay > 0f)
            {
                Debug.Log($"[Director] Inter-wave delay {delay:0.0}s...");
                yield return new WaitForSeconds(delay);
            }

            // Không tăng roundIndex ở đây nữa (đã tăng ở đầu vòng while)
        }
    }


    IEnumerator SetupPhase()
    {
        isInSetup = true;

        float autoTimer = autoStartDelaySeconds;
        setupTimeRemaining = autoTimer;

        Debug.Log($"[Director] SETUP started. Auto-start in {autoTimer:0}s. Press Start to begin earlier (cooldown {manualCooldownSeconds:0}s).");

        while (true)
        {
            if (_manualRequested)
            {
                float cd = Mathf.Max(0f, manualCooldownSeconds);
                setupTimeRemaining = cd;
                Debug.Log($"[Director] Manual requested → cooldown {cd:0}s…");

                while (setupTimeRemaining > 0f)
                {
                    setupTimeRemaining -= Time.deltaTime;
                    yield return null;
                }
                break; // ra khỏi Setup → vào Combat
            }

            autoTimer -= Time.deltaTime;
            setupTimeRemaining = Mathf.Max(0f, autoTimer);

            if (autoTimer <= 0f)
            {
                Debug.Log("[Director] Auto-start reached → begin immediately.");
                break;
            }
            yield return null;
        }

        isInSetup = false;
    }

    // Gọi từ EnemySpawnHandle khi zombie được tạo
    public void OnEnemySpawned(int cost)
    {
        alive += 1;
        spentBudget += Mathf.Max(1, cost);
    }

    // Gọi từ EnemySpawnHandle khi zombie chết
    public void OnEnemyDied()
    {
        alive = Mathf.Max(0, alive - 1);
    }

    void HookDamageDeath(DamageSystem ds)
    {
        if (ds == null) return;
        ds.OnDeath -= OnAnyDeath; // idempotent
        ds.OnDeath += OnAnyDeath;
    }

    void OnAnyDeath(DamageEvent e, DamageResult r)
    {
        var go = e.victimGO;
        if (go == null) return;

        // --- ZOMBIE path ---
        var ticket = go.GetComponent<EnemySpawnHandle>();
        if (ticket != null)
        {
            ticket.ReportDeath();               // đã chống double ở EnemySpawnHandle
            return;
        }

        // --- PLAYER path ---
        var life = go.GetComponent<PlayerLifeController>();
        if (life != null)
        {
            // SP: chuyển sang Dead, overlay/endgame xử lý sau
            return;
        }
    }

    // =======================================================================
    // AUDIO - UI ROUND
    // =======================================================================

    void PlayRoundStartSfx()
    {
        if (GameSession.Mode != AppPlayMode.Single) return;
        // 1) Round 1 special (ưu tiên cao nhất)
        if (roundIndex == 1 && !string.IsNullOrEmpty(roundFirstStartUIEvent))
        {
            AudioEvents.PlayUiGlobal(roundFirstStartUIEvent);
            return;
        }

        // 2) Milestone round special (5/10/15/...)
        if (milestoneEveryNRounds > 0 &&
            roundIndex > 0 &&
            (roundIndex % milestoneEveryNRounds) == 0 &&
            !string.IsNullOrEmpty(milestoneRoundStartUIEvent))
        {
            AudioEvents.PlayUiGlobal(milestoneRoundStartUIEvent);
            return;
        }

        // 3) Default
        if (!string.IsNullOrEmpty(roundStartUIEvent))
        {
            AudioEvents.PlayUiGlobal(roundStartUIEvent);
        }
    }

    void PlayRoundEndSfx()
    {
        if (GameSession.Mode != AppPlayMode.Single) return;
        // 1) Milestone round end special (5/10/15/...)
        if (milestoneEveryNRounds > 0 &&
            roundIndex > 0 &&
            (roundIndex % milestoneEveryNRounds) == 0 &&
            !string.IsNullOrEmpty(milestoneRoundEndUIEvent))
        {
            AudioEvents.PlayUiGlobal(milestoneRoundEndUIEvent);
            return;
        }

        // 2) Default
        if (!string.IsNullOrEmpty(roundEndUIEvent))
        {
            AudioEvents.PlayUiGlobal(roundEndUIEvent);
        }
    }



    // --------------------------------------------------------------------
    // Wave resolver: Ưu tiên Special → (Fixed|Procedural theo block)
    // --------------------------------------------------------------------
    WaveProfile ResolveWaveForRound(int round, out WaveSource src)
    {
        // 1) Special-wave mỗi N round (nếu cấu hình)
        var special = GetSpecialWaveForRound(round);
        if (special != null)
        {
            src = WaveSource.Special;
            // special: reset jitter info
            lastBudgetJitterFactor = 1f;
            lastCapJitterDelta = 0;
            return ApplyPlayerScalingToWave(CloneWaveProfile(special), isSpecialWave: true);
        }

        // 2) Theo khối Fixed/Procedural
        int fixedLen = Mathf.Max(0, waveSet.fixedBlockLength);
        int procLen = Mathf.Max(0, waveSet.proceduralBlockLength);
        int blockLen = fixedLen + procLen;

        if (blockLen > 0)
        {
            int pos = (round - 1) % blockLen;
            bool useFixed = pos < fixedLen;

            if (useFixed)
            {
                var fixedWp = waveSet.GetFixedWaveOrNull(round);
                if (fixedWp != null)
                {
                    src = WaveSource.Fixed;
                    lastBudgetJitterFactor = 1f;
                    lastCapJitterDelta = 0;
                    return ApplyPlayerScalingToWave(CloneWaveProfile(fixedWp), isSpecialWave: false);
                }

                if (waveSet.enableProcedural)
                {
                    src = WaveSource.Procedural;
                    return BuildProceduralWave(round);
                }
                src = WaveSource.None;
                return null;
            }
            else
            {
                if (waveSet.enableProcedural)
                {
                    src = WaveSource.Procedural;
                    return BuildProceduralWave(round);
                }

                var fixedWp = waveSet.GetFixedWaveOrNull(round);
                src = fixedWp != null ? WaveSource.Fixed : WaveSource.None;
                lastBudgetJitterFactor = 1f;
                lastCapJitterDelta = 0;
                return ApplyPlayerScalingToWave(CloneWaveProfile(fixedWp), isSpecialWave: false);
            }
        }
        else
        {
            // Không dùng block: fallback kiểu cũ – Fixed trước, hết thì Procedural (nếu bật)
            var fixedWp = waveSet.GetFixedWaveOrNull(round);
            if (fixedWp != null)
            {
                src = WaveSource.Fixed;
                lastBudgetJitterFactor = 1f;
                lastCapJitterDelta = 0;
                return ApplyPlayerScalingToWave(CloneWaveProfile(fixedWp), isSpecialWave: false);
            }

            if (waveSet.enableProcedural)
            {
                src = WaveSource.Procedural;
                return BuildProceduralWave(round);
            }

            src = WaveSource.None;
            return null;
        }
    }

    // Mỗi N wave: chọn special từ danh sách (nếu có) hoặc dùng specialOneShot
    WaveProfile GetSpecialWaveForRound(int round)
    {
        if (waveSet.everyNSpecial <= 0) return null;
        if (round % waveSet.everyNSpecial != 0) return null;

        // Ưu tiên danh sách specialWaves
        if (waveSet.specialWaves != null && waveSet.specialWaves.Count > 0)
        {
            int k = Mathf.Max(1, round / waveSet.everyNSpecial);
            int idx = (k - 1) % waveSet.specialWaves.Count;
            var wp = waveSet.specialWaves[idx];
            if (wp != null) return wp;
        }

        // Fallback 1 profile đặc biệt
        if (waveSet.specialOneShot != null)
            return ApplyPlayerScalingToWave(CloneWaveProfile(waveSet.specialOneShot), isSpecialWave: true);

        return null;
    }

    // Procedural wave runtime (không sửa asset) + JITTER
    WaveProfile BuildProceduralWave(int round)
    {
        if (waveSet == null) return null;

        var wp = ScriptableObject.CreateInstance<WaveProfile>();

        // Difficulty curve (tăng dần – kiểu CoD)
        int baseBudget = Mathf.RoundToInt(waveSet.baseBudget * Mathf.Pow(waveSet.budgetScalePerWave, round - 1));
        int baseCap = waveSet.baseConcurrency + (round / Mathf.Max(1, waveSet.addConcurrencyEvery));

        // Player-count scaling (snapshot per round)
        baseBudget = Mathf.RoundToInt(baseBudget * appliedBudgetMultiplier);
        baseCap += appliedCapAdd;
        int maxCapScaled = Mathf.Max(waveSet.baseConcurrency, waveSet.maxConcurrency + appliedMaxCapAdd);


        // === JITTER ===
        float jf = 1f;
        int jd = 0;

        var rnd = (_hub != null) ? _hub.Random : null;

        // Nếu chưa có RNG port thì tắt jitter (an toàn)
        if (rnd == null)
        {
            lastBudgetJitterFactor = 1f;
            lastCapJitterDelta = 0;
        }
        else
        {
            if (budgetJitterPct > 0f)
            {
                var r = rnd.RangeFloat(-budgetJitterPct, budgetJitterPct);
                jf = 1f + r;
            }
            if (capJitterMax > 0)
            {
                jd = rnd.RangeInt(-capJitterMax, capJitterMax + 1);
            }
        }

        Debug.Log($"[RoundDirector] Jitter: budget x{jf:F3} | cap Δ{jd}", this);

        int budget = Mathf.RoundToInt(baseBudget * jf);
        int cap = baseCap + jd;

        cap = Mathf.Clamp(cap, 1, maxCapScaled);
        budget = Mathf.Max(1, budget);

        if (waveSet != null && waveSet.useBudgetCap)
            budget = Mathf.Min(budget, appliedMaxBudget);   // ✅ clamp trần

        if (logJitter && (Mathf.Abs(jf - 1f) > 0.001f || jd != 0))
            Debug.Log($"[Director] Procedural jitter applied: budget x{jf:0.00}, cap {(jd >= 0 ? "+" : "")}{jd}");

        // Lưu cho HUD
        lastBudgetJitterFactor = jf;
        lastCapJitterDelta = jd;

        wp.budgetPoints = budget;
        wp.concurrencyCap = cap;
        wp.spawnBurst = Mathf.Max(1, waveSet.spawnBurst);
        wp.interBurstDelay = Mathf.Max(0f, waveSet.interBurstDelay);

        // Chọn loại enemy theo Catalog + minRound unlock
        wp.allowTypes.Clear();

        if (waveSet.catalog != null && waveSet.catalog.entries != null)
        {
            foreach (var def in waveSet.catalog.entries)
            {
                if (def == null || def.prefab == null) continue;
                if (def.minRound <= round) wp.allowTypes.Add(def);
            }
        }

        // Fallback: nếu filter quá chặt
        if (wp.allowTypes.Count == 0 && waveSet.catalog != null && waveSet.catalog.entries != null)
        {
            foreach (var def in waveSet.catalog.entries)
            {
                if (def == null || def.prefab == null) continue;
                wp.allowTypes.Add(def);
            }
        }

        return wp.allowTypes.Count > 0 ? wp : null;
    }

    public float GetEffectiveEnemyWeight(EnemyDefinition def, int round)
    {
        if (def == null) return 0f;

        float w = Mathf.Max(0f, def.weight);

        if (!enableEnemyMixScaling) return w;
        if (string.IsNullOrEmpty(sprinterId)) return w;

        // chỉ buff đúng sprinter id
        if (!string.Equals(def.id, sprinterId, StringComparison.OrdinalIgnoreCase))
            return w;

        // ramp 0..1
        if (round <= sprinterRampStartRound) return w;
        if (sprinterRampEndRound <= sprinterRampStartRound) return w * sprinterWeightMultiplierAtRampEnd;

        float t = Mathf.InverseLerp(sprinterRampStartRound, sprinterRampEndRound, round);
        float mul = Mathf.Lerp(1f, sprinterWeightMultiplierAtRampEnd, t);

        float effective = w * mul;

        // CAP
        if (sprinterMaxEffectiveWeight > 0f)
            effective = Mathf.Min(effective, sprinterMaxEffectiveWeight);

        return effective;

    }

}
