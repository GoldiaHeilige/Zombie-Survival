using UnityEngine;

[DisallowMultipleComponent]
public class PowerUpGameplayBridge : MonoBehaviour
{
    private InstaKillProcessor _instaKillProc;
    private PowerUpManager _mgr;

    void Awake()
    {
        // cache manager once (avoid Instance calls during teardown)
        _mgr = PowerUpManager.Instance;

        // ===== PowerUps from manager =====
        if (_mgr != null)
        {
            _mgr.OnMaxAmmo += HandleMaxAmmo;
            _mgr.OnNuke += HandleNuke;
        }

        // ===== Damage pipeline hook (for InstaKill) =====
        DamageSystem.OnReady += OnDamageSystemReady;

        // If DamageSystem already exists
        if (DamageSystem.Instance != null)
            OnDamageSystemReady(DamageSystem.Instance);
    }

    void OnDestroy()
    {
        if (_mgr != null)
        {
            _mgr.OnMaxAmmo -= HandleMaxAmmo;
            _mgr.OnNuke -= HandleNuke;
            _mgr = null;
        }

        DamageSystem.OnReady -= OnDamageSystemReady;

        // Remove processor to avoid leaks across scene reloads
        if (DamageSystem.Instance != null && _instaKillProc != null)
            DamageSystem.Instance.RemoveProcessor(_instaKillProc);
    }


    void HandleMaxAmmo(GameObject collector)
    {
        var players = FindObjectsByType<PlayerStateProvider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var p in players)
        {
            if (!p) continue;
            var load = p.Loadout;
            if (load == null) continue;

            load.TryFillMaxAmmoAll();
        }

        try { TT.Observer.Instance?.Notify("powerup.maxammo.applied"); } catch { }
    }

    void OnDamageSystemReady(DamageSystem ds)
    {
        if (ds == null) return;

        if (_instaKillProc == null)
            _instaKillProc = new InstaKillProcessor();

        // Insert before Clamp (default pipeline: Friendly(0), Hitbox(1), Crit(2), Falloff(3), Clamp(4))
        ds.AddProcessor(_instaKillProc, index: 4);

        Debug.Log("[PowerUpGameplayBridge] InstaKillProcessor inserted (before Clamp).");
    }

    void HandleNuke(GameObject collector)
    {
        // ===== 1. Kill all enemies via damage =====
        var damageables = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        foreach (var mb in damageables)
        {
            if (mb is not IDamageable dmgable)
                continue;

            if (dmgable.GetTeam() != TeamId.Enemy)
                continue;

            var go = (dmgable as Component)?.gameObject;
            if (!go) continue;

            var e = new DamageEvent
            {
                victimGO = go,
                victim = dmgable,
                attacker = null,                 // ❗ NO attacker → no kill attribution
                baseDamage = 999999f,
                damageType = DamageType.Explosive,
                source = DamageSource.PowerUp    // nếu bạn có enum này
            };

            DamageRouter.Apply(e);
        }

        // ===== 2. Award flat points (COD style) =====
        const int NUKE_POINTS = 400;

        var players = FindObjectsByType<PlayerPoints>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        foreach (var p in players)
        {
            if (!p) continue;
            p.Add(NUKE_POINTS, PointReason.PowerUp);
        }

        try { TT.Observer.Instance?.Notify("powerup.nuke.applied"); } catch { }
    }

}
