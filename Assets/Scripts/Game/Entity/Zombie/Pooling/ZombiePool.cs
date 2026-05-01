using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ZombiePool
{
    [Header("Pool Info")]
    public string id;               // ví dụ "walker1"
    public GameObject prefab;       // Prefab zombie gốc
    public int initialSize = 10;    // Số lượng spawn sẵn
    public bool canExpand = true;   // Cho phép mở rộng khi hết

    [HideInInspector] public Transform container;

    private readonly Queue<GameObject> pool = new Queue<GameObject>();

    // --- Khởi tạo ---
    public void Initialize(Transform parent)
    {
        container = new GameObject($"Pool_{id}").transform;
        container.SetParent(parent);
        container.localPosition = Vector3.zero;

        for (int i = 0; i < initialSize; i++)
        {
            var obj = Object.Instantiate(prefab, container);
            obj.SetActive(false);
            pool.Enqueue(obj);
        }
    }

    // --- Lấy zombie từ pool ---
    // --- Lấy zombie từ pool ---
    public GameObject Get(Vector3 pos, Quaternion rot)
    {
        GameObject obj = (pool.Count > 0) ? pool.Dequeue()
                                          : (canExpand ? Object.Instantiate(prefab, container) : null);
        if (obj == null)
        {
            Debug.LogWarning($"[ZombiePool] Pool '{id}' is empty!");
            return null;
        }

        obj.transform.SetPositionAndRotation(pos, rot);
        obj.SetActive(true);

        // Reset “phiếu” nếu có
        if (obj.TryGetComponent<EnemySpawnHandle>(out var handle))
            handle.ResetForReuse();

        // GỌI TẤT CẢ các component implement IPoolable
        var poolables = obj.GetComponents<IPoolable>();
        for (int i = 0; i < poolables.Length; i++)
            poolables[i].OnSpawned();

        return obj;
    }

    // --- Trả zombie về pool ---
    public void Return(GameObject zombie)
    {
        if (zombie == null) return;

        // GỌI TẤT CẢ các component implement IPoolable
        var poolables = zombie.GetComponents<IPoolable>();
        for (int i = 0; i < poolables.Length; i++)
            poolables[i].OnDespawned();

        zombie.transform.SetParent(container);
        zombie.SetActive(false);
        pool.Enqueue(zombie);
    }

}
