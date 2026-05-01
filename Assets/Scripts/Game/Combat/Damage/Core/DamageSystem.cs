// Assets/Scripts/Combat/Damage/DamageSystem.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public class DamageSystem : MonoBehaviour
{
    public static DamageSystem Instance { get; private set; }

    public event Action<DamageEvent, DamageResult> OnDamageApplied;
    public event Action<DamageEvent, DamageResult> OnAfterApplied;
    public event Action<DamageEvent, DamageResult> OnDeath;

    readonly List<IDamageProcessor> _processors = new();

    [Header("Default Processors (order matters)")]
    public bool allowFriendlyFire = false;
    public bool useDefaultProcessors = true;

    FriendlyFireProcessor _friendly;
    HitboxMultiplierProcessor _hitboxMult;
    DistanceFalloffProcessor _falloff;
    ClampProcessor _clamp;
    CritChanceProcessor _crit;

    public static event System.Action<DamageSystem> OnReady; 

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (useDefaultProcessors)
        {
            _friendly = new FriendlyFireProcessor { allowFriendlyFire = allowFriendlyFire };
            _hitboxMult = new HitboxMultiplierProcessor();
            _crit = new CritChanceProcessor();
            _falloff = new DistanceFalloffProcessor();
            _clamp = new ClampProcessor();

            _processors.Add(new LimbDetachProcessor());
            _processors.Add(_friendly);
            _processors.Add(_hitboxMult);
            _processors.Add(_crit);
            _processors.Add(_falloff);
            _processors.Add(_clamp);
        }

        OnReady?.Invoke(this);
    }

    public void AddProcessor(IDamageProcessor p, int index = -1)
    {
        if (index < 0 || index > _processors.Count) _processors.Add(p);
        else _processors.Insert(index, p);
    }
    public void RemoveProcessor(IDamageProcessor p) => _processors.Remove(p);

    public DamageResult Apply(DamageEvent e)
    {
        // 1) Resolve victim nếu thiếu
        if (e.victim == null && e.victimGO)
        {
            // ƯU TIÊN gateway nếu có (để Downed/Dead chạy đúng logic)
            var lc = e.victimGO.GetComponent<PlayerLifeController>();
            if (lc != null) e.victim = lc as IDamageable;

            // Fallback nếu không có gateway
            if (e.victim == null)
                e.victim = e.victimGO.GetComponent<IDamageable>();
        }
        if (e.victim == null)
        {
            Debug.LogWarning("[DMG] No victim resolved, cancel.");
            return new DamageResult { isApplied = false, finalDamage = 0f, remainingHealth = 0f, isFatal = false };
        }

        // 2) Log BEGIN: attacker/victim + team
        TeamId atkTeam = TeamId.Neutral;
        if (e.attacker)
        {
            var atkDmg = e.attacker.GetComponentInParent<IDamageable>();
            if (atkDmg != null) atkTeam = atkDmg.GetTeam();
        }
        TeamId vicTeam = e.victim.GetTeam();
/*        Debug.Log($"[DMG-BEGIN] atk='{(e.attacker ? e.attacker.name : "ENV")}'({atkTeam}) " +
                  $"-> vic='{(e.victimGO ? e.victimGO.name : "NULL")}'({vicTeam}); " +
                  $"base={e.baseDamage:0.##}, type={e.damageType}, dist={e.distance:0.0}, hitbox={e.hitboxId}");*/

        // 3) Chạy PROCESSORS + log từng bước
        float before = e.baseDamage;
        for (int i = 0; i < _processors.Count; i++)
        {
            var p = _processors[i];
            bool ok = p.Process(ref e);
/*            Debug.Log($"[DMG-PROC#{i + 1}:{p.GetType().Name}] {(ok ? "OK" : "BLOCK")} " +
                      $"dmg {before:0.##} -> {e.baseDamage:0.##} | flags: crit={e.isCritical} ffIgnored={e.friendlyFireIgnored}");*/
            if (!ok)
            {
                var rBlocked = new DamageResult { isApplied = false, finalDamage = 0f, remainingHealth = (e.victimGO ? e.victimGO.GetComponent<DamageableHealth>()?.currentHealth ?? 0f : 0f), isFatal = false };
                OnAfterApplied?.Invoke(e, rBlocked);
                return rBlocked;
            }
            before = e.baseDamage;
        }

        // 4) Áp sát thương thực sự
        var result = e.victim.ApplyDamage(e);

        // 5) Log FINAL
        /*        Debug.Log($"[DMG-FINAL] applied={result.isApplied} fatal={result.isFatal} " +
                          $"final={result.finalDamage:0.##} remaining={result.remainingHealth:0.##}");*/

        // 6) Sự kiện hệ thống
    //    Debug.Log("[DMG] OnDamageApplied INVOKE: victim=" + e.victimGO);
        OnDamageApplied?.Invoke(e, result);
        OnAfterApplied?.Invoke(e, result);
        if (result.isFatal) OnDeath?.Invoke(e, result);
        return result;
    }
}
