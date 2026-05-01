using Fusion;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

[DisallowMultipleComponent]
[RequireComponent(typeof(ZombieBlackboard))]
public class ZombieTargetSmartSelector : NetworkBehaviour
{
    [Header("Retargeting")]
    public float retargetInterval = 0.3f;
    public float maxTargetDistance = 9999f;

    [Header("Anti-jitter")]
    [Range(0f, 0.5f)]
    public float switchDistanceGain = 0.15f; // target mới phải gần hơn ~15%

    public float minLockTime = 0.8f;        // sau khi đổi target, lock bấy nhiêu giây

    [Header("Reachability (NavMesh)")]
    public bool requireReachablePath = true;
    public float reachableCheckInterval = 0.6f;     // không check path liên tục (đỡ nặng)
    public int reachableTopKCandidates = 3;          // chỉ check path cho top K target gần nhất
    public float reachableSampleRadius = 1.5f;       // sample target lên navmesh

    NavMeshPath _path;
    readonly Dictionary<Transform, (double nextCheck, bool ok)> _reachCache = new();

    [Header("Debug")]
    public bool debugLogs = false;

    ZombieBlackboard _bb;
    ZombieBrain _brain;
    NavMeshAgent _agent;

    ITargetable _current;
    double _nextTick;
    double _lockUntil;
    float _maxDistSqr;

    bool IsSinglePlayerLike()
    {
        // SP: không có NetworkObject/Runner
        return (Object == null || Object.Runner == null);
    }


    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _bb = GetComponent<ZombieBlackboard>();
        _brain = GetComponent<ZombieBrain>();
        _maxDistSqr = maxTargetDistance <= 0 ? float.MaxValue : maxTargetDistance * maxTargetDistance;
        _path = new NavMeshPath();
    }

    void OnEnable()
    {
        _nextTick = 0;
    }

    void OnDisable()
    {
        if (Object == null || !Object.Runner) return;

        // host cleanup
        if (Object.HasStateAuthority && _current != null)
        {
            // nếu sau này còn dùng TargetService thì để NoteAttacker ở đây,
            // còn hiện tại bỏ hẳn cho đơn giản.
            _current = null;
        }
        _reachCache.Clear();
    }

    void Update()
    {
        if (!IsSinglePlayerLike()) return;

        // SP chạy bằng Time.time
        TickSelector(Time.timeAsDouble);
    }


    string NameOf(ITargetable t)
    {
        if (t is Component c && c) return c.name;
        return "<null>";
    }

    static bool IsUnityAlive(ITargetable t)
    {
        if (t == null) return false;
        if (t is Component c) return c;   // Unity fake-null (destroyed => false)
        return true;
    }


    public override void FixedUpdateNetwork()
    {
        if (!Object.HasStateAuthority) return;
        TickSelector(Runner.SimulationTime);
    }


    void TickSelector(double now)
    {
        bool inAttackState = _brain && _brain.current == ZombieBrain.State.Attack;

        // validate current
        // validate current
        if (_current != null)
        {
            if (!IsUnityAlive(_current))
            {
                _current = null;
            }
            else
            {
                bool invalid =
                    !_current.IsAliveLike ||
                    !_current.CanBeAttacked ||
                    DistanceSqrTo(_current) > _maxDistSqr;

                if (!invalid)
                {
                    var tr = _current.TargetTransform;
                    if (tr && !IsReachableCached(tr, now))
                        invalid = true;
                }

                if (invalid) _current = null;
            }
        }


        if (now >= _nextTick)
        {
            _nextTick = now + retargetInterval;

            ITargetable best = FindNearestPlayer();
            bool locked = _current != null && now < _lockUntil;

            bool shouldSwitch = false;

            if (_current == null)
            {
                shouldSwitch = (best != null);
            }
            else if (best != null && best != _current)
            {
                if (!inAttackState && !locked)
                {
                    float dCurr2 = DistanceSqrTo(_current);
                    float dBest2 = DistanceSqrTo(best);

                    float gain = Mathf.Clamp01(switchDistanceGain);
                    float factor = (1f - gain);
                    factor *= factor;

                    shouldSwitch = dBest2 < dCurr2 * factor;
                }
            }

            if (shouldSwitch)
            {
                _current = best;
                if (_current != null)
                    _lockUntil = now + minLockTime;
            }
        }

        // write to blackboard
        var t = _current?.TargetTransform;
        _bb.target = t;

        if (t)
        {
            _bb.distanceToTarget = Vector3.Distance(transform.position, t.position);
            _bb.hasLOS = true;
            _bb.lastSawTargetTime = Time.time;
        }
        else
        {
            _bb.distanceToTarget = 0f;
            _bb.hasLOS = false;
        }
    }


    // ─────────────────── Helpers ───────────────────

    ITargetable FindNearestPlayer()
    {
        var players = PlayerRegistry.Players;
        if (players == null || players.Count == 0)
            return null;

        // 1) gom candidate theo khoảng cách (chưa check path)
        List<(ITargetable t, Transform tr, float d2)> cands = null;

        foreach (var p in players)
        {
            if (!p) continue;

            ITargetable t =
                p.GetComponent<ITargetable>() ??
                p.GetComponentInChildren<ITargetable>(true);

            if (!IsUnityAlive(t)) continue;
            if (!t.IsAliveLike || !t.CanBeAttacked) continue;

            var tr = t.TargetTransform;
            if (!tr) continue;

            float d2 = (tr.position - transform.position).sqrMagnitude;
            if (d2 > _maxDistSqr) continue;

            cands ??= new List<(ITargetable, Transform, float)>(players.Count);
            cands.Add((t, tr, d2));
        }

        if (cands == null || cands.Count == 0) return null;

        // 2) sort theo gần nhất
        cands.Sort((a, b) => a.d2.CompareTo(b.d2));

        // 3) chỉ check reachability cho top K gần nhất
        double now = IsSinglePlayerLike() ? Time.timeAsDouble : Runner.SimulationTime;
        int k = Mathf.Clamp(reachableTopKCandidates, 1, cands.Count);

        for (int i = 0; i < k; i++)
        {
            var tr = cands[i].tr;
            if (IsReachableCached(tr, now))
                return cands[i].t;
        }

        // 4) nếu top K đều unreachable, fallback: trả về gần nhất (để khỏi “mù” hoàn toàn)
        return cands[0].t;
    }

    float DistanceSqrTo(ITargetable t)
    {
        if (!IsUnityAlive(t)) return float.MaxValue;

        var tr = t.TargetTransform;
        if (!tr) return float.MaxValue;

        return (tr.position - transform.position).sqrMagnitude;
    }


    bool IsReachableCached(Transform targetTr, double now)
    {
        if (!requireReachablePath) return true;
        if (!targetTr) return false;

        if (_reachCache.TryGetValue(targetTr, out var e) && now < e.nextCheck)
            return e.ok;

        // zombie phải ở trên navmesh
        if (!NavMesh.SamplePosition(transform.position, out var zHit, 1.0f, NavMesh.AllAreas))
        {
            _reachCache[targetTr] = (now + reachableCheckInterval, false);
            return false;
        }


        // target phải sample được lên navmesh
        if (!NavMesh.SamplePosition(targetTr.position, out var tHit, reachableSampleRadius, NavMesh.AllAreas))
        {
            _reachCache[targetTr] = (now + reachableCheckInterval, false);
            return false;
        }

        int mask = (_agent != null) ? _agent.areaMask : NavMesh.AllAreas;

        bool ok = NavMesh.CalculatePath(zHit.position, tHit.position, mask, _path) &&
                  _path.status == NavMeshPathStatus.PathComplete;




        _reachCache[targetTr] = (now + reachableCheckInterval, ok);
        return ok;
    }

}
