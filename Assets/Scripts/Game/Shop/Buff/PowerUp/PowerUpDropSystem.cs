using UnityEngine;

#if FUSION_WEAVER
using Fusion;
#endif

[DisallowMultipleComponent]
public class PowerUpDropSystem : MonoBehaviour
{
    [Header("Prefabs (one per type)")]
    public GameObject prefabMaxAmmo;
    public GameObject prefabDoublePoints;
    public GameObject prefabInstaKill;
    public GameObject prefabNuke;

    [Header("Spawn Rules")]
    [Tooltip("Max number of power-up pickups allowed alive on the map (across ALL types).")]
    public int maxAlivePickups = 1;

    [Tooltip("Seconds before another power-up is allowed to drop.")]
    public float dropCooldownSeconds = 20f;

    [Header("Drop Chance")]
    [Range(0f, 1f)] public float dropChancePerKill = 0.035f;

    [Header("Weights (relative)")]
    public int weightMaxAmmo = 45;
    public int weightDoublePoints = 25;
    public int weightInstaKill = 20;
    public int weightNuke = 10;

    [Header("Anti-chain (recommended)")]
    [Tooltip("Max number of drops allowed per round. Helps prevent MP power-up spam.")]
    public int maxDropsPerRound = 2;

    [Tooltip("In MP: effectiveChance = baseChance * (mult ^ extraPlayers). Example: 0.75 => 3P ~ 0.56x, 4P ~ 0.42x.")]
    [Range(0.1f, 1f)] public float mpChanceMultiplierPerExtraPlayer = 0.75f;

    int _dropsThisRound = 0;
    int _lastRoundSeen = -1;


    float _nextDropAllowedAt;

#if FUSION_WEAVER
    NetworkRunner _runner;
#endif

    void Awake()
    {
#if FUSION_WEAVER
        _runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
#endif
    }

    bool IsAuthority()
    {
#if FUSION_WEAVER
        if (_runner != null && _runner.IsRunning)
            return _runner.IsServer;
#endif
        return true; // SP
    }

    float Now()
    {
#if FUSION_WEAVER
        if (_runner != null && _runner.IsRunning)
            return (float)_runner.SimulationTime;
#endif
        return Time.time;
    }

    void OnEnable()
    {
        if (RoundDirector.Instance != null)
            RoundDirector.Instance.OnRoundChanged += HandleRoundChanged;
    }

    void OnDisable()
    {
        if (RoundDirector.Instance != null)
            RoundDirector.Instance.OnRoundChanged -= HandleRoundChanged;
    }

    void HandleRoundChanged(int newRound)
    {
        _dropsThisRound = 0;
        _lastRoundSeen = newRound;
    }

    public bool TryRollDrop(Vector3 worldPos)
    {
        if (!IsAuthority()) return false;

        float now = Now();
        if (now < _nextDropAllowedAt) return false;

        int alive = FindObjectsByType<PowerUpPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
        if (alive >= Mathf.Max(0, maxAlivePickups)) return false;

        // ---- Round cap (anti-chain) ----
        // Nếu chưa bắt được OnRoundChanged vì lý do nào đó, vẫn cố sync round index bằng polling nhẹ.
        int roundIdx = (RoundDirector.Instance != null) ? RoundDirector.Instance.roundIndex : -1;
        if (roundIdx != -1 && roundIdx != _lastRoundSeen)
        {
            _lastRoundSeen = roundIdx;
            _dropsThisRound = 0;
        }

        if (maxDropsPerRound > 0 && _dropsThisRound >= maxDropsPerRound)
            return false;

        // ---- MP chance scaling (powerups are GLOBAL, so MP must be rarer) ----
        float effectiveChance = dropChancePerKill;

        if (RoundDirector.Instance != null)
        {
            int players = Mathf.Max(1, RoundDirector.Instance.scaledPlayerCount);
            int extra = Mathf.Max(0, players - 1);
            if (extra > 0)
                effectiveChance *= Mathf.Pow(mpChanceMultiplierPerExtraPlayer, extra);
        }

        effectiveChance = Mathf.Clamp01(effectiveChance);

        if (UnityEngine.Random.value > effectiveChance) return false;


        var type = RollTypeByWeight();
        var prefab = GetPrefab(type);
        if (!prefab) return false;

        SpawnPickup(prefab, worldPos);
        _dropsThisRound++;

        _nextDropAllowedAt = now + Mathf.Max(0f, dropCooldownSeconds);
        return true;
    }

    PowerUpType RollTypeByWeight()
    {
        int w1 = Mathf.Max(0, weightMaxAmmo);
        int w2 = Mathf.Max(0, weightDoublePoints);
        int w3 = Mathf.Max(0, weightInstaKill);
        int w4 = Mathf.Max(0, weightNuke);

        int total = w1 + w2 + w3 + w4;
        if (total <= 0) return PowerUpType.MaxAmmo;

        int r = UnityEngine.Random.Range(0, total);
        if (r < w1) return PowerUpType.MaxAmmo;
        r -= w1;
        if (r < w2) return PowerUpType.DoublePoints;
        r -= w2;
        if (r < w3) return PowerUpType.InstaKill;
        return PowerUpType.Nuke;
    }

    GameObject GetPrefab(PowerUpType type) => type switch
    {
        PowerUpType.MaxAmmo => prefabMaxAmmo,
        PowerUpType.DoublePoints => prefabDoublePoints,
        PowerUpType.InstaKill => prefabInstaKill,
        PowerUpType.Nuke => prefabNuke,
        _ => null
    };

    void SpawnPickup(GameObject prefab, Vector3 pos)
    {
        Vector3 spawnPos = pos + Vector3.up * 0.25f;

#if FUSION_WEAVER
        if (_runner != null && _runner.IsRunning && _runner.IsServer)
        {
            var no = prefab.GetComponent<NetworkObject>();
            if (no != null)
            {
                _runner.Spawn(no, spawnPos, Quaternion.identity, null);
                return;
            }
            else
            {
                Debug.LogWarning($"[PowerUpDropSystem] Prefab '{prefab.name}' has no NetworkObject. Clients won't see it in MP.", this);
            }
        }
#endif

        Instantiate(prefab, spawnPos, Quaternion.identity);
    }
}
