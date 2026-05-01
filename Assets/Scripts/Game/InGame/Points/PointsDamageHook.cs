// PointsDamageHook.cs
using UnityEngine;
#if FUSION_WEAVER
using Fusion;
#endif

/// <summary>
/// Hook DamageSystem để cộng điểm khi bắn/giết zombie.
/// SP hoạt động ngay. MP sẽ hoạt động chuẩn sau khi ta sửa attacker trong MPDamageDriver.
/// </summary>
public class PointsDamageHook : MonoBehaviour
{
    [Header("Debug")]
    public bool logHit = false;
    public bool logKill = false;

    DamageSystem _ds;

    void OnEnable()
    {
#if FUSION_WEAVER
        // Trong MP client thì không cần hook damage vì damage chỉ áp trên host
        if (GameSession.Mode == AppPlayMode.Client)
        {
            enabled = false;
            return;
        }
#endif

        DamageSystem.OnReady += OnDamageSystemReady;

        // Nếu DamageSystem đã sẵn sàng trước đó:
        if (DamageSystem.Instance != null)
        {
            OnDamageSystemReady(DamageSystem.Instance);
        }
    }


    void OnDisable()
    {
        DamageSystem.OnReady -= OnDamageSystemReady;

        if (_ds != null)
        {
            _ds.OnDamageApplied -= OnDamageApplied;
            _ds.OnDeath -= OnDeath;
            _ds = null;
        }
    }

    void OnDamageSystemReady(DamageSystem ds)
    {
        if (_ds == ds) return;

        if (_ds != null)
        {
            _ds.OnDamageApplied -= OnDamageApplied;
            _ds.OnDeath -= OnDeath;
        }

        _ds = ds;
        if (_ds == null) return;

        _ds.OnDamageApplied += OnDamageApplied;
        _ds.OnDeath += OnDeath;

     //   Debug.Log("[PointsDamageHook] Subscribed to DamageSystem.");
    }

    bool TryGetZombie(in DamageEvent e, out DamageableHealth hp, out GameObject zombieGO)
    {
        hp = null;
        zombieGO = null;

        // Ưu tiên lấy trực tiếp từ e.victim
        if (e.victim is DamageableHealth dh && dh != null)
        {
            hp = dh;
            zombieGO = dh.gameObject;
        }
        // Fallback: thử từ victimGO
        else if (e.victimGO)
        {
            zombieGO = e.victimGO;
            hp = zombieGO.GetComponentInParent<DamageableHealth>();
        }

        if (hp == null)
            return false;

        // Chỉ quan tâm team Enemy (zombie)
        if (hp.team != TeamId.Enemy)
            return false;

        if (zombieGO == null)
            zombieGO = hp.gameObject;

        return true;
    }

    PlayerPoints GetAttackerWallet(in DamageEvent e)
    {
        if (!e.attacker) return null;
        return e.attacker.GetComponentInParent<PlayerPoints>();
    }

    ZombiePointConfig GetZombieConfig(GameObject zombieGO)
    {
        if (!zombieGO) return null;
        return zombieGO.GetComponentInParent<ZombiePointConfig>();
    }

    void OnDamageApplied(DamageEvent e, DamageResult result)
    {
        if (e.source == DamageSource.PowerUp) return;
        // Chỉ quan tâm những hit thực sự trừ máu
        if (!result.isApplied) return;
        if (result.finalDamage <= 0f) return;

        // ✅ NEW: nếu hit này là hit kết liễu -> KHÔNG cộng điểm hit
        // (để viên cuối chỉ ăn điểm ZombieKill ở OnDeath)
        if (result.isFatal) return;

        // Lấy info zombie một lần, dùng cho cả cfg
        if (!TryGetZombie(in e, out var hp, out var zombieGO))
            return;

        var cfg = GetZombieConfig(zombieGO);
        if (cfg == null) return;
        if (cfg.hitPoints <= 0) return;

        var wallet = GetAttackerWallet(in e);
        if (!wallet) return;

        wallet.Add(cfg.hitPoints, PointReason.ZombieHit, zombieGO);

        if (logHit)
        {
            Debug.Log($"[PointsDamageHook] Hit zombie {zombieGO.name} -> +{cfg.hitPoints} pts for {wallet.gameObject.name}");
        }
    }


    void OnDeath(DamageEvent e, DamageResult result)
    {
        if (e.source == DamageSource.PowerUp) return;

        if (!result.isFatal) return;

        if (!TryGetZombie(in e, out var hp, out var zombieGO))
            return;

        var cfg = GetZombieConfig(zombieGO);
        if (cfg == null) return;

        var wallet = GetAttackerWallet(in e);
        if (!wallet) return;

        // 1) Cộng point kill nếu killPoints > 0
        if (cfg.killPoints > 0)
        {
            wallet.Add(cfg.killPoints, PointReason.ZombieKill, zombieGO);

            if (logKill)
            {
                Debug.Log($"[PointsDamageHook] Kill zombie {zombieGO.name} -> +{cfg.killPoints} pts for {wallet.gameObject.name}");
            }
        }

        // 2) Cộng kill count cho player
        var playerRoot = wallet.gameObject.transform.root;
        var killStats = playerRoot.GetComponentInChildren<PlayerKillStats>(); // hoặc GetComponentInParent, tùy bạn đặt
        if (killStats != null)
        {
            killStats.AddKill(1);
        }
    }
}
