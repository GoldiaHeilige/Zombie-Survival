using UnityEngine;
using System;
using Fusion;

public class SPHealthState : MonoBehaviour, IHealthState
{
    [SerializeField] private DamageableHealth _health;

    public float Current { get; private set; }
    public float Max { get; private set; }
    public bool IsDead { get; private set; }
    public bool IsDowned { get; private set; } // chưa dùng thì cứ false

    public event Action<float, float> OnHealthChanged;
    public event Action OnDeath;
    public event Action OnRevive;

    void Awake()
    {
        if (_health == null) _health = GetComponentInParent<DamageableHealth>();
    }

    void OnEnable()
    {
        if (_health != null)
        {
            Max = _health.maxHealth;
            Current = Mathf.Clamp(_health.currentHealth, 0, Max);
            IsDead = Current <= 0f;
            OnHealthChanged?.Invoke(Current, Max);

            // hook đúng chữ ký: Action<DamageEvent, DamageResult>
            _health.OnHit += HandleHit;
            _health.OnDeathLocal += HandleDeath;
            // nếu có OnReviveLocal thì gắn tương tự:
            // _health.OnReviveLocal += HandleRevive;
            _health.OnHpChanged += HandleHpChanged;
        }
    }

    void OnDisable()
    {
        if (_health != null)
        {
            _health.OnHit -= HandleHit;
            _health.OnDeathLocal -= HandleDeath;
            // _health.OnReviveLocal -= HandleRevive;
            _health.OnHpChanged -= HandleHpChanged;
        }
    }

    // ====== HANDLERS ĐÚNG CHỮ KÝ ======

    void HandleHit(DamageEvent e, DamageResult r)
    {
        Max = _health.maxHealth;
        Current = Mathf.Clamp(r.remainingHealth, 0, Max);
        IsDead = r.isFatal || Current <= 0f;

        // KHÔNG gọi OnHealthChanged nữa, đã có HandleHpChanged lo
        if (IsDead) OnDeath?.Invoke();
    }

    void HandleDeath(DamageEvent e, DamageResult r)
    {
        Max = _health.maxHealth;
        Current = 0f;
        IsDead = true;

        // KHÔNG gọi OnHealthChanged nữa, đã có HandleHpChanged lo
        OnDeath?.Invoke();
    }

    void HandleHpChanged(int before, int after)
    {
        Max = _health.maxHealth;
        Current = Mathf.Clamp(after, 0, Max);
        IsDead = Current <= 0f;
        OnHealthChanged?.Invoke(Current, Max);
    }

}
