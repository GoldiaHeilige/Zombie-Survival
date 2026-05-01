#if FUSION_WEAVER
using System;
using System.Collections;
using Fusion;
using UnityEngine;

public class AIMultiplayerImpl :
  MonoBehaviour, ISpawnPort, IRandomPort, ITargetQuery, IGameStartPort, IDeathEvents
{
    [Header("Refs (auto-find nếu để trống)")]
    [SerializeField] private NetworkRunner runner;
    [SerializeField] private RoundDirector roundDirector;
    [SerializeField] private TargetService targetService;

    [Header("Logs")]
    [SerializeField] private bool verboseLogs = true;

    // ===== Hub =====
    AIPortHub _hub;
    Coroutine _waitCo;

    // ===== IDeathEvents =====
    public event Action<EnemyLifeToken> OnEnemyDied;
    public void RaiseEnemyDied(EnemyLifeToken token) {
        try { OnEnemyDied?.Invoke(token); } catch (Exception e) { Debug.LogException(e, this); }
    }

    void OnEnable()
    {
        _hub = FindFirstObjectByType<AIPortHub>(FindObjectsInactive.Include);
        if (!_hub) { Debug.LogError("[MPImpl] AIPortHub not found in scene.", this); enabled = false; return; }

        if (!runner) runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        if (!roundDirector) roundDirector = FindFirstObjectByType<RoundDirector>(FindObjectsInactive.Include);
        if (!targetService) targetService = FindFirstObjectByType<TargetService>(FindObjectsInactive.Include);

        // Gán tối thiểu để UI/client gọi start không bị null
        _hub.GameStart = this;

        if (runner == null || !runner.IsRunning)
        {
            if (verboseLogs) Debug.LogWarning("[MPImpl] NetworkRunner not running yet → waiting to register ports…", this);
            if (_waitCo != null) StopCoroutine(_waitCo);
            _waitCo = StartCoroutine(WaitRunnerThenRegister());
            return;
        }

        RegisterPortsByRole();
    }

    IEnumerator WaitRunnerThenRegister()
    {
        while (runner == null || !runner.IsRunning)
        {
            if (!runner) runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
            yield return null;
        }

        RegisterPortsByRole();
        _waitCo = null;
    }


    void RegisterPortsByRole() {
        // Clear mọi cổng trước (phòng trường hợp re-apply)
        if (object.ReferenceEquals(_hub.Spawn, this)) _hub.Spawn = null;
        if (object.ReferenceEquals(_hub.Random, this)) _hub.Random = null;
        if (object.ReferenceEquals(_hub.Target, this)) _hub.Target = null;
        if (object.ReferenceEquals(_hub.GameStart, this)) _hub.GameStart = null;
        if (object.ReferenceEquals(_hub.DeathEvents, this)) _hub.DeathEvents = null;

        bool isServer = runner && runner.IsServer;

        // Cả Host lẫn Client đều có thể gán GameStart (client gửi RPC → host)
        _hub.GameStart = this;

        if (isServer) {
            // HOST là nguồn sự thật
            _hub.Spawn       = this;
            _hub.Random      = this;
            _hub.Target      = this;   // Host chọn mục tiêu qua TargetService
            _hub.DeathEvents = this;

            if (verboseLogs) Debug.Log("[MPImpl] Registered ports for HOST (Spawn/Random/Target/DeathEvents/GameStart).", this);
        } else {
            // CLIENT: chỉ cung cấp GameStart. Các core yêu cầu ports khác sẽ tự disable (requirePorts = true).
            if (verboseLogs) Debug.Log("[MPImpl] Registered port for CLIENT (GameStart only).", this);
        }
    }

    void OnDisable()
    {
        if (_waitCo != null) { StopCoroutine(_waitCo); _waitCo = null; }

        if (_hub != null)
        {
            if (object.ReferenceEquals(_hub.Spawn, this)) _hub.Spawn = null;
            if (object.ReferenceEquals(_hub.Random, this)) _hub.Random = null;
            if (object.ReferenceEquals(_hub.Target, this)) _hub.Target = null;
            if (object.ReferenceEquals(_hub.GameStart, this)) _hub.GameStart = null;
            if (object.ReferenceEquals(_hub.DeathEvents, this)) _hub.DeathEvents = null;
        }
    }


    // ===== ISpawnPort (HOST only) =====
    public GameObject Spawn(EnemyDefinition def, Vector3 position, Quaternion rotation) {
        if (runner == null || !runner.IsServer) {
            Debug.LogError("[MPImpl] Spawn called on non-host. This should not happen.", this);
            return null;
        }
        if (!def || !def.prefab) { Debug.LogError("[MPImpl] EnemyDefinition/prefab missing.", this); return null; }

        // Prefab phải có NetworkObject và được Runner biết (NetworkProjectConfig / Prefab table)
        var no = def.prefab.GetComponent<NetworkObject>();
        if (!no) { Debug.LogError($"[MPImpl] Prefab '{def.prefab.name}' lacks NetworkObject.", def.prefab); return null; }

        var spawned = runner.Spawn(no, position, rotation, inputAuthority: null);
        if (verboseLogs) Debug.Log($"[MPImpl] Spawned '{def.name}' via NetworkRunner @ {position}", this);
        return spawned ? spawned.gameObject : null;
    }

    public void Despawn(GameObject instance) {
        if (runner == null || !runner.IsServer) {
            Debug.LogError("[MPImpl] Despawn called on non-host.", this);
            return;
        }
        if (!instance) return;
        var no = instance.GetComponent<NetworkObject>();
        if (!no) { Debug.LogWarning($"[MPImpl] Despawn target has no NetworkObject: {instance.name}", instance); return; }
        runner.Despawn(no);
        if (verboseLogs) Debug.Log($"[MPImpl] Despawn '{instance.name}'", this);
    }

    // ===== IRandomPort (HOST only) =====
    public int RangeInt(int minInclusive, int maxExclusive) {
        AssertHostRng();
        int v = UnityEngine.Random.Range(minInclusive, maxExclusive);
        if (verboseLogs) Debug.Log($"[MPImpl] RNG Int [{minInclusive},{maxExclusive}) => {v}", this);
        return v;
    }
    public float RangeFloat(float minInclusive, float maxInclusive) {
        AssertHostRng();
        float v = UnityEngine.Random.Range(minInclusive, maxInclusive);
        if (verboseLogs) Debug.Log($"[MPImpl] RNG Float [{minInclusive},{maxInclusive}] => {v}", this);
        return v;
    }
    public Vector2 InsideUnitCircle() {
        AssertHostRng();
        var v = UnityEngine.Random.insideUnitCircle;
        if (verboseLogs) Debug.Log($"[MPImpl] RNG InsideUnitCircle => {v}", this);
        return v;
    }
    public void SetSeed(int? seed) {
        AssertHostRng();
        if (seed.HasValue) UnityEngine.Random.InitState(seed.Value);
        if (verboseLogs) Debug.Log($"[MPImpl] RNG Seed = {(seed.HasValue ? seed.Value.ToString() : "<unchanged>")}", this);
    }
    void AssertHostRng() {
        if (!(runner && runner.IsServer))
            throw new InvalidOperationException("[MPImpl] RNG used on client. Director/AI/Spawner must be Host-only.");
    }

    // ===== ITargetQuery (HOST only) =====
    public ITargetable GetBestTarget(Vector3 fromPosition, bool requireAttackable = true) {
        if (!(runner && runner.IsServer)) return null;
        if (!targetService) return null;
        return targetService.GetBestTarget(fromPosition, requireAttackable);
    }

    // ===== IGameStartPort =====
    public void RequestStart() {
        if (runner && runner.IsServer) {
            // Host tự start
            if (!roundDirector) roundDirector = FindFirstObjectByType<RoundDirector>(FindObjectsInactive.Include);
            if (!roundDirector) { Debug.LogWarning("[MPImpl] RoundDirector not found on Host."); return; }
            if (verboseLogs) Debug.Log("[MPImpl] Host RequestStart → Director.RequestManualStart()", this);
            roundDirector.RequestManualStart();
        } else {
            // Client gửi RPC lên Host
            if (verboseLogs) Debug.Log("[MPImpl] Client RequestStart → RPC_RequestStart()", this);
            RPC_RequestStart();
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    void RPC_RequestStart() {
        if (!runner || !runner.IsServer) return;
        if (!roundDirector) roundDirector = FindFirstObjectByType<RoundDirector>(FindObjectsInactive.Include);
        if (roundDirector) {
            if (verboseLogs) Debug.Log("[MPImpl] RPC_RequestStart received on Host → Director.RequestManualStart()", this);
            roundDirector.RequestManualStart();
        }
    }
}
#endif