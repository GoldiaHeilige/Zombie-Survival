using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyHealthScaler : MonoBehaviour, IPoolable
{
    [Header("Refs")]
    [SerializeField] private DamageableHealth health;

    [Header("Options")]
    [Tooltip("Refill full HP after scaling max HP (recommended for freshly spawned zombies).")]
    public bool refillToFull = true;

    float _baseMax;
    float _lastAppliedMul = -1f;

    void Awake()
    {
        if (!health) health = GetComponentInChildren<DamageableHealth>(true);

        if (!health)
        {
            Debug.LogError($"[{nameof(EnemyHealthScaler)}] Missing DamageableHealth on '{name}' or children.", this);
            enabled = false;
            return;
        }

        // Cache HP gốc của zombie prefab (KHÔNG phải HP đã scale).
        _baseMax = Mathf.Max(1f, health.maxHealth);
    }

    void OnEnable()
    {
        // MP (không pool) thường chỉ cần OnEnable là đủ.
        ApplyIfNeeded();
    }

    public void OnSpawned()
    {
        // SP pool: mỗi lần lấy zombie từ pool ra sẽ gọi hook này -> apply đúng theo round mới
        // (Pool của bạn thường SetActive(true) trước, nên OnEnable có thể chạy rồi;
        // guard bằng _lastAppliedMul để không apply 2 lần.)
        _lastAppliedMul = -1f;
        ApplyIfNeeded();
    }

    public void OnDespawned()
    {
        // Reset guard để lần spawn sau apply lại theo round mới
        _lastAppliedMul = -1f;

        // Optional: trả state “sạch” về base
        health.maxHealth = _baseMax;
        if (refillToFull) health.currentHealth = health.maxHealth;
    }

    void ApplyIfNeeded()
    {
        // Safety: scaler này chỉ nên gắn cho zombie, nhưng check thêm cho chắc
        if (health.team != TeamId.Enemy) return;

        var dir = RoundDirector.Instance;
        float mul = (dir != null) ? Mathf.Max(0.01f, dir.appliedHpMultiplier) : 1f;

        if (Mathf.Approximately(mul, _lastAppliedMul)) return;
        _lastAppliedMul = mul;

        float oldMax = health.maxHealth;

        health.maxHealth = Mathf.Max(1f, _baseMax * mul);

        if (refillToFull)
            health.currentHealth = health.maxHealth;
        else
            health.currentHealth = Mathf.Clamp(health.currentHealth, 0f, health.maxHealth);
    }
}
