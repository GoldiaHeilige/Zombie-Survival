#if FUSION_WEAVER
using Fusion;
using UnityEngine;
using System;

[DisallowMultipleComponent]
public class MPHealthState : NetworkBehaviour, IHealthState
{
    [SerializeField] private DamageableHealth _health;

    [Networked] public float NetCurrent { get; private set; }
    [Networked] public float NetMax { get; private set; }
    [Networked] public NetworkBool NetDead { get; private set; }
    [Networked] public LifeState NetLife { get; set; }


    // IHealthState (read-only)
    public float Current => NetCurrent;
    public float Max => NetMax;
    public bool IsDead => NetDead;
    public bool IsDowned => false;

    float _lastReportedCurrent = -1f;
    float _lastReportedMax = -1f;
    bool _lastReportedDead = false;

    public event Action<float, float> OnHealthChanged;
    public event Action OnDeath;
    public event Action OnRevive;

    // cache để phát event khi thay đổi
    float _lastCur, _lastMax;
    bool _lastDead;
    LifeState _lastNetLife;

    public override void Spawned()
    {
        base.Spawned();

        if (_health == null) _health = GetComponentInParent<DamageableHealth>();

        if (Object.HasStateAuthority)
        {
            if (_health != null)
            {
                NetMax = _health.maxHealth;
                NetCurrent = Mathf.Clamp(_health.currentHealth, 0, NetMax);
                NetDead = NetCurrent <= 0f;

                // ĐỒNG BỘ BAN ĐẦU LifeState từ PlayerLifeController
                var life = GetComponentInParent<PlayerLifeController>();
                if (life != null)
                {
                    NetLife = life.state;
                    Debug.Log($"[MPHealthState] Initial NetLife: {NetLife}");
                }

                _health.OnHit += HandleHit_Host;
                _health.OnDeathLocal += HandleDeath_Host;
                _health.OnHpChanged += HandleHpChanged_Host;

                // ÉP CHẮC: nếu có HealthSyncFusion + StateAuthority thì coi như đang chạy trong MP
                if (Object.HasStateAuthority && life != null)
                {
                    life.enableDownedInThisMode = true;
                    Debug.Log("[HealthSync] Force enableDownedInThisMode = true (MP host)");
                }

            }

        }

        // init cache + bắn event
        _lastCur = NetCurrent; _lastMax = NetMax; _lastDead = NetDead;
        _lastNetLife = NetLife;
        OnHealthChanged?.Invoke(NetCurrent, NetMax);
        if (NetDead) OnDeath?.Invoke();

        _lastReportedCurrent = Current;
        _lastReportedMax = Max;
        _lastReportedDead = IsDead;

        RaiseHealthEvents(force: true);
    }


    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Object.HasStateAuthority && _health != null)
        {
            _health.OnHit -= HandleHit_Host;
            _health.OnDeathLocal -= HandleDeath_Host;
            _health.OnHpChanged -= HandleHpChanged_Host;
        }
    }

    void HandleHpChanged_Host(int before, int after)
    {
        NetMax = _health.maxHealth;
        NetCurrent = Mathf.Clamp(after, 0, NetMax);

        // Dead = đúng LifeState, không chỉ dựa vào HP
        var lifeCtrl = GetComponentInParent<PlayerLifeController>();
        if (lifeCtrl != null)
        {
            NetDead = (lifeCtrl.state == LifeState.Dead);
        }
        else
        {
            NetDead = NetCurrent <= 0f;
        }

    //    Debug.Log($"[MPHealthState] HandleHpChanged: {before} -> {after}, NetDead={NetDead}");
    }

    // Poll nhẹ ở Render() để phát event UI khi Net* đổi
    public override void Render()
    {
        base.Render();
        // Health changes
        if (!Mathf.Approximately(_lastCur, NetCurrent) || !Mathf.Approximately(_lastMax, NetMax))
        {
         //   Debug.Log($"[MPHealthState] Health changed: {_lastCur} -> {NetCurrent}");
            _lastCur = NetCurrent; _lastMax = NetMax;
            OnHealthChanged?.Invoke(NetCurrent, NetMax);
        }

        // Death state changes
        if (_lastDead != NetDead)
        {
        //    Debug.Log($"[MPHealthState] Death state changed: {_lastDead} -> {NetDead}");
            _lastDead = NetDead;
            if (NetDead) OnDeath?.Invoke(); else OnRevive?.Invoke();
        }

        // LifeState changes (QUAN TRỌNG)
        if (_lastNetLife != NetLife)
        {
  //          Debug.Log($"[MPHealthState] LifeState changed: {_lastNetLife} -> {NetLife}");
            _lastNetLife = NetLife;
        }

        RaiseHealthEvents();
    }

    void RaiseHealthEvents(bool force = false)
    {
        float cur = Current;
        float max = Max;

        bool changedHp = !Mathf.Approximately(cur, _lastReportedCurrent) ||
                          !Mathf.Approximately(max, _lastReportedMax);
        bool changedAny = changedHp || force;

        if (!changedAny)
            return;

        _lastReportedCurrent = cur;
        _lastReportedMax = max;

        // Bắn event HP đổi cho các hệ thống (UI, audio, v.v.)
        OnHealthChanged?.Invoke(cur, max);

        // Đồng thời check Dead/Revive để đảm bảo các listener dùng IHealthState cũng bắt được
        bool deadNow = IsDead;
        if (deadNow && !_lastReportedDead)
        {
            OnDeath?.Invoke();
        }
        else if (!deadNow && _lastReportedDead)
        {
            OnRevive?.Invoke();
        }

        _lastReportedDead = deadNow;
    }


    // ===== Host ghi Networked khi core bắn event =====
    void HandleHit_Host(DamageEvent e, DamageResult r)
    {
        NetMax = _health.maxHealth;
        NetCurrent = Mathf.Clamp(r.remainingHealth, 0, NetMax);

        // QUAN TRỌNG: Chỉ set NetDead = true nếu thực sự là fatal VÀ không ở chế độ Downed
        var lifeCtrl = GetComponentInParent<PlayerLifeController>();
        bool shouldBeDead = r.isFatal && (!lifeCtrl || !lifeCtrl.enableDownedInThisMode);
        NetDead = shouldBeDead;

   //     Debug.Log($"[MPHealthState] HandleHit: HP={NetCurrent}, isFatal={r.isFatal}, enableDowned={lifeCtrl?.enableDownedInThisMode}, NetDead={NetDead}");
    }

    void HandleDeath_Host(DamageEvent e, DamageResult r)
    {
        NetMax = _health.maxHealth;
        NetCurrent = 0f;
        NetDead = true;
    }
}
#endif
