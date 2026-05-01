using UnityEngine;
using System.Collections.Generic;

public class ImpactPool : MonoBehaviour
{
    public static ImpactPool Instance { get; private set; }

    [SerializeField] int capacityPerPrefab = 60;
    [SerializeField] Transform container;

    class Pool
    {
        public PooledImpactFX[] arr;
        public int next;
    }

    readonly Dictionary<GameObject, Pool> _pools = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (!container) container = transform;
    }


    public void Spawn(GameObject prefab, Vector3 point, Vector3 normal,
                      float lifeSeconds = -1f, float surfaceOffset = 0.002f)
    {
        if (!prefab) return;
        var pool = GetOrCreate(prefab);
        var fx = pool.arr[pool.next];
        pool.next = (pool.next + 1) % pool.arr.Length;

        if (fx == null)  // slot bị destroy (do StopAction/Unload)
        {
            Debug.LogWarning($"[ImpactPool] Recreating destroyed FX in slot {pool.next}");
            var go = Instantiate(prefab, container);
            fx = go.GetComponent<PooledImpactFX>() ?? go.AddComponent<PooledImpactFX>();
            pool.arr[pool.next] = fx;
        }


        var pos = point + normal * surfaceOffset; // tránh z-fighting
        fx.Activate(pos, normal, lifeSeconds);
    }

    Pool GetOrCreate(GameObject prefab)
    {
        if (_pools.TryGetValue(prefab, out var p)) return p;

        p = new Pool { arr = new PooledImpactFX[capacityPerPrefab], next = 0 };
        for (int i = 0; i < p.arr.Length; i++)
        {
            var go = Instantiate(prefab, container);
            var fx = go.GetComponent<PooledImpactFX>();
            if (!fx) fx = go.AddComponent<PooledImpactFX>();
            p.arr[i] = fx;
        }
        _pools[prefab] = p;
        return p;
    }
}
