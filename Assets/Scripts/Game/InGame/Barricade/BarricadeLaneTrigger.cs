using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class BarricadeLaneTrigger : MonoBehaviour
{
    [SerializeField] private BarricadeWindow window;

    [Header("Attack Slots")]
    [Tooltip("Nhiều điểm đứng đập cửa. Zombie sẽ tự chọn điểm ít người/ gần nhất.")]
    [SerializeField] private Transform[] attackPoints;

    [Tooltip("Nếu attackPoints rỗng, dùng transform của trigger làm fallback.")]
    [SerializeField] private Transform fallbackAttackPoint;

    // brain -> index slot
    readonly Dictionary<ZombieBrain, int> _assigned = new();
    int[] _slotCounts;

    private void Awake()
    {
        if (!window)
            window = GetComponentInParent<BarricadeWindow>(true);

        if (!fallbackAttackPoint)
            fallbackAttackPoint = transform;

        if (attackPoints == null || attackPoints.Length == 0)
            attackPoints = new[] { fallbackAttackPoint };

        _slotCounts = new int[attackPoints.Length];
    }

    private void OnTriggerEnter(Collider other)
    {
        var brain = other.GetComponentInParent<ZombieBrain>();
        if (!brain || !window) return;

        if (brain.IsTraversingLink) return; // ✅ đang trèo -> ignore

        if (!window.CanTakeZombieHit())
            return;

        Transform best = ReserveBestPoint(brain);
        brain.SetBarricadeLane(window, best, this);
    }

    private void OnTriggerExit(Collider other)
    {
        var brain = other.GetComponentInParent<ZombieBrain>();
        if (!brain || !window) return;

        if (brain.IsTraversingLink) return;
    }


    // ===== Reserve / Release =====

    Transform ReserveBestPoint(ZombieBrain brain)
    {
        if (_assigned.TryGetValue(brain, out int existing))
            return attackPoints[Mathf.Clamp(existing, 0, attackPoints.Length - 1)];

        int bestIdx = 0;
        float bestScore = float.PositiveInfinity;

        Vector3 from = brain.transform.position;

        for (int i = 0; i < attackPoints.Length; i++)
        {
            Transform p = attackPoints[i] ? attackPoints[i] : fallbackAttackPoint;
            Vector3 dest = p.position;

            // đảm bảo điểm hợp lệ trên navmesh
            if (!NavMesh.SamplePosition(dest, out var hit, 1.0f, NavMesh.AllAreas))
                continue;

            // check path basic (đỡ chọn điểm “trông gần” nhưng lại không đi được)
            var agent = brain.GetComponent<NavMeshAgent>();
            if (agent != null && agent.enabled && agent.isOnNavMesh)
            {
                NavMeshPath path = new NavMeshPath();
                if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete)
                    continue;
            }

            float dist = Vector3.Distance(from, hit.position);

            // score: ưu tiên slot ít người trước, rồi mới tới gần
            float score = _slotCounts[i] * 1000f + dist;

            if (score < bestScore)
            {
                bestScore = score;
                bestIdx = i;
            }
        }

        _assigned[brain] = bestIdx;
        _slotCounts[bestIdx]++;

        return attackPoints[bestIdx] ? attackPoints[bestIdx] : fallbackAttackPoint;
    }

    public void Release(ZombieBrain brain)
    {
        if (brain == null) return;

        if (_assigned.TryGetValue(brain, out int idx))
        {
            _assigned.Remove(brain);
            if (_slotCounts != null && idx >= 0 && idx < _slotCounts.Length)
                _slotCounts[idx] = Mathf.Max(0, _slotCounts[idx] - 1);
        }
    }
}
