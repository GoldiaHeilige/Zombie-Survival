using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Fusion;
using Behaviour = UnityEngine.Behaviour;

/// Gate chuẩn: SP/Host bật AI; Client MP tắt AI & NavMeshAgent.
/// Gắn script này ở ROOT prefab Zombie.
public class AIAuthorityGate : NetworkBehaviour
{
    [Header("Auto collect common AI comps in children")]
    [Tooltip("Tự gom các script AI thường gặp ở mọi cấp con")]
    public bool autoCollect = true;

    [Header("Manual lists (nếu muốn thêm cụ thể)")]
    public List<Behaviour> behavioursToToggle = new(); // MonoBehaviours (Brain, Perception, Selector, Melee, Fallback…)
    public List<NavMeshAgent> navAgentsToToggle = new(); // NavMeshAgent ở root/children

    bool _initialized;

    // Các tên lớp thường gặp để auto-collect (không cần reference trực tiếp)
    static readonly HashSet<string> CommonAiTypeNames = new()
    {
        "ZombieBrain",
        "ZombiePerception",
        "ZombieTargetSmartSelector",
        "ZombieMeleeExecutor",
        "ZombieFallbackAI",
    };

    public override void Spawned()
    {
        InitializeIfNeeded();
        ApplyAuthorityGate();
    }

    void Awake()
    {
        // Trường hợp object có sẵn trong scene ở SP
        InitializeIfNeeded();
        ApplyAuthorityGate();
    }

    void InitializeIfNeeded()
    {
        if (_initialized) return;

        if (autoCollect)
        {
            // Gom tất cả Behaviour ở mọi cấp con và lọc theo tên lớp thường gặp
            var allBehaviours = GetComponentsInChildren<Behaviour>(true);
            foreach (var b in allBehaviours)
            {
                if (b == null) continue;
                var n = b.GetType().Name;
                if (CommonAiTypeNames.Contains(n))
                    behavioursToToggle.Add(b);
            }

            // Gom mọi NavMeshAgent ở root/children
            var agents = GetComponentsInChildren<NavMeshAgent>(true);
            navAgentsToToggle.AddRange(agents);
        }

        _initialized = true;
    }

    void ApplyAuthorityGate()
    {
        // Xác định vai trò
        var runner = Runner;               // NetworkBehaviour có sẵn Runner
        bool isSP = (runner == null);      // SP: không có runner
        bool isAuthority = isSP || (Object != null && Object.IsValid && Object.HasStateAuthority);

        // Client MP -> TẮT; SP/Host -> BẬT
        bool enableAI = isAuthority;

        // Toggle Behaviours (core AI scripts…)
        foreach (var b in behavioursToToggle)
        {
            if (!b) continue;
            b.enabled = enableAI;
        }

        // Toggle NavMeshAgent
        foreach (var agent in navAgentsToToggle)
        {
            if (!agent) continue;
            // Khi tắt NavMeshAgent, ta để Transform đồng bộ qua NetworkTransform nên OK.
            agent.enabled = enableAI;
        }
    }

#if UNITY_EDITOR
    // Tiện ích trong Inspector: click phải → Auto collect lại
    [ContextMenu("Recollect Children")]
    void Editor_Recollect()
    {
        behavioursToToggle.Clear();
        navAgentsToToggle.Clear();
        _initialized = false;
        InitializeIfNeeded();
    }
#endif
}
