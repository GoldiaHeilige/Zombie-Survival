using System.Collections.Generic;
using UnityEngine;

public class TargetService : MonoBehaviour
{
    public static TargetService I { get; private set; }

    [Header("Defaults for legacy overload")]
    [SerializeField] float defaultCrowdPenalty = 3f;
    [SerializeField] int defaultMaxAttackersPerTarget = -1;

    [Header("Debug")]
    [SerializeField] bool debugSelection = true;

    class Entry { public ITargetable t; public Transform tr; public int attackerCount; public float threatBonus; }
    readonly Dictionary<ITargetable, Entry> _map = new();

    // --- NEW: self-heal scan ---
    float _nextScanAt;
    [SerializeField] float rescanInterval = 1.0f; // mỗi 1s quét lại 1 lần trên Host

    Fusion.NetworkRunner _runner;
    bool _isHostLikeCached;
    float _nextRunnerRefreshAt;
    [SerializeField] float runnerRefreshInterval = 2.0f; // cực thưa


    void Awake() => I = this;

    void Update()
    {
        if (Time.unscaledTime >= _nextRunnerRefreshAt)
        {
            _nextRunnerRefreshAt = Time.unscaledTime + runnerRefreshInterval;
            RefreshRunnerRole();
        }

        if (!_isHostLikeCached) return;

        if (Time.unscaledTime >= _nextScanAt)
        {
            _nextScanAt = Time.unscaledTime + rescanInterval;
            SelfHealScan();
        }
    }


    void SelfHealScan()
    {
        // Quét tất cả ITargetable đang active (kể cả của client ở Host)
        var allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var seen = new HashSet<ITargetable>();
        foreach (var b in allBehaviours)
        {
            if (b == null) continue;
            if (b is ITargetable t && t.TargetTransform)
            {
                seen.Add(t);
                if (!_map.ContainsKey(t))
                {
                    _map[t] = new Entry { t = t, tr = t.TargetTransform, attackerCount = 0, threatBonus = 0f };
                    // Debug.Log($"[TargetService] Auto-registered: {((Component)t).name}");
                }
            }
        }

        // Gỡ những target đã biến mất
        var toRemove = new List<ITargetable>();
        foreach (var kv in _map)
        {
            var e = kv.Value;
            if (e.t == null || e.tr == null || !seen.Contains(e.t))
                toRemove.Add(kv.Key);
        }
        foreach (var k in toRemove) _map.Remove(k);
    }

    // API public cũ + mới (như mình đã gửi trước)
    public void Register(ITargetable t)
    {
        if (t == null || _map.ContainsKey(t)) return;
        _map[t] = new Entry { t = t, tr = t.TargetTransform, attackerCount = 0, threatBonus = 0f };
    }
    public void Unregister(ITargetable t) { if (t != null) _map.Remove(t); }
    public void NoteAttacker(ITargetable t, int d)
    {
        if (t != null && _map.TryGetValue(t, out var e)) e.attackerCount = Mathf.Max(0, e.attackerCount + d);
    }

    public ITargetable GetBestTarget(Vector3 origin, bool requireAttackable = true)
        => GetBestTarget(origin, requireAttackable, defaultCrowdPenalty, defaultMaxAttackersPerTarget);

    public ITargetable GetBestTarget(Vector3 origin, bool requireAttackable, float crowdPenalty, int maxAttackersPerTarget = -1)
    {
        ITargetable best = null;
        float bestScore = float.NegativeInfinity;

/*        if (debugSelection)
            Debug.Log($"[TargetService] Query from {origin} requireAttackable={requireAttackable} " +
                      $"crowd={crowdPenalty} maxAttackers={maxAttackersPerTarget} count={_map.Count}");*/

        foreach (var kv in _map)
        {
            var e = kv.Value;
            var t = e.t;
            if (t == null || e.tr == null) continue;

            bool aliveLike = t.IsAliveLike;
            bool attackable = t.CanBeAttacked;

            // Điều kiện filter
            bool reject = requireAttackable ? !(aliveLike && attackable) : !aliveLike;
            if (reject)
            {
/*                if (debugSelection)
                    Debug.Log($"[TargetService]  - SKIP {(t as Component)?.name} " +
                              $"aliveLike={aliveLike} attackable={attackable}");*/
                continue;
            }

            if (maxAttackersPerTarget >= 0 && e.attackerCount >= maxAttackersPerTarget)
            {
                if (debugSelection)
/*                    Debug.Log($"[TargetService]  - SKIP {(t as Component)?.name} attackerCount={e.attackerCount}");*/
                continue;
            }

            float dist = Vector3.Distance(origin, e.tr.position);
            float near = 1000f / (0.5f + dist);
            float crowd = -crowdPenalty * e.attackerCount;
            float tie = Random.Range(-0.5f, 0.5f);
            float score = near + crowd + e.threatBonus + tie;

            if (debugSelection)
/*                Debug.Log($"[TargetService]  * CAND {(t as Component)?.name} " +
                          $"dist={dist:F2} attackers={e.attackerCount} " +
                          $"aliveLike={aliveLike} attackable={attackable} score={score:F1}");*/

            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }

        if (debugSelection)
        {
            var bestName = (best as Component)?.name ?? "<none>";
/*            Debug.Log($"[TargetService] => BEST = {bestName} (score={bestScore})");*/
        }

        return best;
    }


    public bool TryGetScoreEstimate(ITargetable t, out float score)
    {
        score = 0f;
        if (!_map.TryGetValue(t, out var e) || e.tr == null) return false;
        float dist = Vector3.Distance(Vector3.zero, Vector3.zero); // không quan trọng – dummy
        score = 1000f / (0.5f + dist) - defaultCrowdPenalty * e.attackerCount + e.threatBonus;
        return true;
    }

    void RefreshRunnerRole()
    {
        // refresh rất thưa, hoặc chỉ khi runner null
        if (_runner == null)
            _runner = FindFirstObjectByType<Fusion.NetworkRunner>(FindObjectsInactive.Include);

        // SP: runner null => host-like (vì offline)
        _isHostLikeCached = (_runner == null) || _runner.IsServer || _runner.IsSharedModeMasterClient;
    }

}
