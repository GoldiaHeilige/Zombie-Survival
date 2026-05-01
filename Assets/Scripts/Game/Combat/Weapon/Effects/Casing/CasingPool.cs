using System.Collections.Generic;
using UnityEngine;

public class CasingPool : MonoBehaviour
{
    public static CasingPool Instance { get; private set; }

    [SerializeField] int defaultPrewarm = 24;
    [SerializeField] int maxPerPrefab = 256;

    readonly Dictionary<GameObject, Stack<PooledCasing>> _pool = new();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Prewarm(GameObject prefab, int count = -1)
    {
        if (prefab == null) return;
        if (count <= 0) count = defaultPrewarm;

        var stack = GetStack(prefab);
        for (int i = 0; i < count; i++)
        {
            if (stack.Count >= maxPerPrefab) break;
            var pc = CreateOne(prefab);
            Return(pc);
        }
    }

    public PooledCasing Rent(GameObject prefab, Vector3 pos, Quaternion rot, int layer)
    {
        if (prefab == null) return null;

        var stack = GetStack(prefab);
        PooledCasing pc = stack.Count > 0 ? stack.Pop() : CreateOne(prefab);

        var go = pc.gameObject;
        go.transform.SetPositionAndRotation(pos, rot);
        SetLayerRecursive(go, layer);

        pc.OnRented();
        go.SetActive(true);
        return pc;
    }

    public void Return(PooledCasing pc)
    {
        if (pc == null) return;
        if (pc.Prefab == null) { Destroy(pc.gameObject); return; }

        var stack = GetStack(pc.Prefab);
        if (stack.Count >= maxPerPrefab)
        {
            Destroy(pc.gameObject);
            return;
        }

        pc.OnReturned();
        pc.gameObject.SetActive(false);
        pc.transform.SetParent(transform, false);
        stack.Push(pc);
    }

    Stack<PooledCasing> GetStack(GameObject prefab)
    {
        if (!_pool.TryGetValue(prefab, out var stack))
        {
            stack = new Stack<PooledCasing>(32);
            _pool[prefab] = stack;
        }
        return stack;
    }

    PooledCasing CreateOne(GameObject prefab)
    {
        var go = Instantiate(prefab, transform);
        go.SetActive(false);

        var pc = go.GetComponent<PooledCasing>();
        if (pc == null) pc = go.AddComponent<PooledCasing>();
        pc.SetPrefab(prefab);

        return pc;
    }

    static void SetLayerRecursive(GameObject root, int layer)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }
}
