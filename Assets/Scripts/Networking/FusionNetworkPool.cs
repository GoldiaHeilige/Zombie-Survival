#if FUSION_WEAVER
using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// NetworkObject provider có pooling cho Fusion 2.
/// Gắn script này lên GameObject chứa NetworkRunner
/// và set vào field "Object Provider" của Runner.
/// KHÔNG prewarm, pool tự nở dần, object inactive được parent vào PoolRoot.
/// </summary>
public class FusionNetworkPool : NetworkObjectProviderDefault
{
    [Header("Pool Root (auto create nếu null)")]
    [SerializeField] private Transform poolRoot;

    // key: prefab gốc, value: queue instance
    private readonly Dictionary<NetworkObject, Queue<NetworkObject>> _pools = new();

    // map instance -> prefab gốc (dùng khi trả về pool)
    private readonly Dictionary<NetworkObject, NetworkObject> _instanceToPrefab = new();

    private static readonly List<IPoolable> _tempPoolables = new();

    void Awake()
    {
        if (!poolRoot)
        {
            var go = new GameObject("FusionPoolRoot");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            poolRoot = go.transform;
        }
    }

    // ───────────────── Queue helpers ─────────────────

    private Queue<NetworkObject> GetQueue(NetworkObject prefab)
    {
        if (!_pools.TryGetValue(prefab, out var q))
        {
            q = new Queue<NetworkObject>();
            _pools.Add(prefab, q);
        }
        return q;
    }

    // ───────────────── Spawn side ─────────────────
    // Runner.Spawn(...) → gọi vào đây

    protected override NetworkObject InstantiatePrefab(NetworkRunner runner, NetworkObject prefab)
    {
        var queue = GetQueue(prefab);
        NetworkObject inst;

        if (queue.Count > 0)
        {
            // Lấy từ pool
            inst = queue.Dequeue();
        }
        else
        {
            // Chưa có trong pool → Instantiate 1 con mới
            inst = base.InstantiatePrefab(runner, prefab);
            if (inst)
            {
                // Ghi nhớ prefab gốc, lần sau despawn còn biết trả về queue nào
                _instanceToPrefab[inst] = prefab;
            }
        }

        if (!inst)
            return null;

        // Active object ra khỏi PoolRoot
        inst.transform.SetParent(null, true);
        inst.gameObject.SetActive(true);
        CallOnSpawned(inst.gameObject);

        return inst;
    }

    // ───────────────── Despawn side ─────────────────
    // Runner.Despawn(...) → gọi vào đây

    protected override void DestroyPrefabInstance(NetworkRunner runner, NetworkPrefabId prefabId, NetworkObject instance)
    {
        if (!instance)
            return;

        // Nếu instance chưa từng được pool quản lý → phá luôn như mặc định
        if (!_instanceToPrefab.TryGetValue(instance, out var prefab) || prefab == null)
        {
            base.DestroyPrefabInstance(runner, prefabId, instance);
            return;
        }

        CallOnDespawned(instance.gameObject);
        instance.gameObject.SetActive(false);
        instance.transform.SetParent(poolRoot, false);

        var queue = GetQueue(prefab);
        queue.Enqueue(instance);
    }


    // ───────────────── IPoolable helpers ─────────────────

    private static void CallOnSpawned(GameObject go)
    {
        go.GetComponents(_tempPoolables);
        for (int i = 0; i < _tempPoolables.Count; i++)
            _tempPoolables[i].OnSpawned();
        _tempPoolables.Clear();
    }

    private static void CallOnDespawned(GameObject go)
    {
        go.GetComponents(_tempPoolables);
        for (int i = 0; i < _tempPoolables.Count; i++)
            _tempPoolables[i].OnDespawned();
        _tempPoolables.Clear();
    }
}
#endif
