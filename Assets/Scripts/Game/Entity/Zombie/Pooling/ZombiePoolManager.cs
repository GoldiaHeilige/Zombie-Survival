// ZombiePoolManager.cs - Sửa Awake() và Start()
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ZombiePoolManager : MonoBehaviour
{
    public static ZombiePoolManager Instance { get; private set; }

    [Header("Zombie Pools Setup")]
    public List<ZombiePool> zombiePools = new List<ZombiePool>();

    private readonly Dictionary<string, ZombiePool> poolDict = new Dictionary<string, ZombiePool>();

    private bool _isInitializing = false;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // KHÔNG khởi tạo pool ở đây nữa
    }

    void Start()
    {
        // Đợi 1 frame để các system khác (GameSession, AI Port) có thời gian khởi tạo
        StartCoroutine(DelayedInitialize());
    }

    private IEnumerator DelayedInitialize()
    {
        if (_isInitializing) yield break;
        _isInitializing = true;

        // Đợi end of frame để đảm bảo tất cả Awake() và Start() khác đã chạy
        yield return new WaitForEndOfFrame();

        // Kiểm tra GameSession mode
        if (GameSession.Mode != AppPlayMode.Single)
        {
            Debug.Log($"[ZombiePoolManager] Not Singleplayer ({GameSession.Mode}) → skip pool init.", this);
            enabled = false;
            _isInitializing = false;
            yield break;
        }

        // Kiểm tra AIPortHub đã sẵn sàng chưa
        if (AIPortHub.I == null)
        {
            Debug.Log("[ZombiePoolManager] Waiting for AIPortHub...", this);

            // Thử tìm AIPortHub trong scene
            int maxAttempts = 10;
            for (int i = 0; i < maxAttempts; i++)
            {
                var hub = FindFirstObjectByType<AIPortHub>(FindObjectsInactive.Include);
                if (hub != null)
                {
                    Debug.Log($"[ZombiePoolManager] Found AIPortHub after {i + 1} attempts", this);
                    break;
                }
                yield return new WaitForSeconds(1f); // Chờ 0.1 giây mỗi lần
            }
        }

        if (AIPortHub.I == null)
        {
            Debug.LogError("[ZombiePoolManager] AIPortHub still not found after waiting! Pool may not work correctly.", this);
            // Vẫn tiếp tục khởi tạo pool nếu mode là Singleplayer
        }

        // Bắt đầu khởi tạo pool
        InitializePool();
        _isInitializing = false;
    }

    private void InitializePool()
    {
        var parent = new GameObject("Zombie_Pool").transform;
        parent.SetParent(transform);

        foreach (var zp in zombiePools)
        {
            zp.Initialize(parent);
            if (!poolDict.ContainsKey(zp.id))
                poolDict.Add(zp.id, zp);
        }

        Debug.Log($"[ZombiePoolManager] Initialized {poolDict.Count} zombie pools for Singleplayer mode.", this);
    }

    // --- Lấy zombie ---
    public GameObject Spawn(string id, Vector3 pos, Quaternion rot)
    {
        // Nếu pool chưa được khởi tạo và chúng ta đang ở Singleplayer mode
        if (poolDict.Count == 0 && GameSession.Mode == AppPlayMode.Single && !_isInitializing)
        {
            Debug.LogWarning("[ZombiePoolManager] Pool not initialized yet! Initializing now...", this);
            InitializePool();
        }

        if (!poolDict.TryGetValue(id, out var pool))
        {
            Debug.LogError($"[ZombiePoolManager] Pool with id '{id}' not found!");
            return null;
        }
        return pool.Get(pos, rot);
    }

    // --- Trả zombie ---
    public void Despawn(GameObject zombie)
    {
        if (zombie == null) return;

        var comp = zombie.GetComponent<ZombieComponent>();

        if (comp == null || string.IsNullOrEmpty(comp.poolId))
        {
            Debug.LogWarning($"[ZombiePool] Despawn called on NON-ZOMBIE: {zombie.name} — IGNORED");
            return;
        }

        if (poolDict.TryGetValue(comp.poolId, out var pool))
        {
            pool.Return(zombie);
        }
        else
        {
            Debug.LogWarning($"[ZombiePool] Pool '{comp.poolId}' not found for {zombie.name} — IGNORED");
        }
    }
}