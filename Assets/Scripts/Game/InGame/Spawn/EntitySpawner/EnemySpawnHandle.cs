using UnityEngine;

/// "Phiếu" đếm alive/budget cho từng enemy instance.
public class EnemySpawnHandle : MonoBehaviour
{
    [Tooltip("Chi phí budget của enemy này. SpawnManager sẽ set khi tạo.")]
    public int cost = 1;

    [Tooltip("Bật để in log chi tiết cho instance này.")]
    public bool verbose = false;

    bool _spawnReported = false;   // đã báo +alive/+budget?
    bool _deathReported = false;   // đã báo -alive?

    /// Gọi 1 LẦN ngay sau khi Instantiate (hoặc lôi từ pool).
    public void MarkSpawned(int c)
    {
        cost = Mathf.Max(1, c);

        // Nếu lỡ gọi trùng => bỏ qua, tránh +budget quá giới hạn
        if (_spawnReported)
        {
            if (verbose) Debug.Log($"[EnemySpawnHandle] Duplicate MarkSpawned ignored on {name}");
            return;
        }

        _spawnReported = true;
        _deathReported = false;

        RoundDirector.Instance?.OnEnemySpawned(cost);
        if (verbose) Debug.Log($"[EnemySpawnHandle] Spawned -> +alive, +{cost}pts | {name}");
    }

    /// Gọi đúng lúc CHẾT THẬT (từ Health/Bridge/Hook).
    public void ReportDeath()
    {
        // Chỉ trừ nếu đã từng spawn và chưa báo chết
        if (!_spawnReported || _deathReported) return;

        _deathReported = true;
        RoundDirector.Instance?.OnEnemyDied();
        if (verbose) Debug.Log($"[EnemySpawnHandle] Died -> -alive | {name}");
    }

    // Pooling: khi trả về pool bạn có thể Disable object; tránh reset _spawnReported ở đây
    // để không gây đếm sai giữa vòng đời. Khi tái sử dụng, SpawnManager sẽ gọi MarkSpawned() lại.
    void OnEnable()
    {
        // Không reset _spawnReported ở đây! (tránh double-count)
        _deathReported = false;
    }

    /// Nếu bạn có pipeline pool chủ động, gọi hàm này TRƯỚC khi reuse để làm sạch cờ.
    public void ResetForReuse()
    {
        _spawnReported = false;
        _deathReported = false;
    }
}
