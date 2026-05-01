// TriggerDamageZone.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gắn lên một Collider (isTrigger = true). Khi một GameObject có IDamageable đi vào,
/// sẽ tạo DamageEvent và gửi vào DamageRouter.Apply(...) để test hệ thống damage.
/// </summary>
[DisallowMultipleComponent]
public class TriggerDamageZone : MonoBehaviour
{
    public enum Mode { OnEnter, OnStay }

    [Header("Mode")]
    public Mode mode = Mode.OnEnter;

    [Header("Damage")]
    [Tooltip("Damage base (per hit for OnEnter, per second for OnStay)")]
    public float damage = 25f;
    public DamageType damageType = DamageType.Melee;
    public DamageSource damageSource = DamageSource.Trap;
    public string weaponId = "env.trigger";

    [Header("OnStay settings")]
    [Tooltip("Ticks per second to apply damage when in OnStay mode")]
    public float tickRate = 4f; // 4 times per second

    [Header("Filtering")]
    [Tooltip("Optional: only affect these layers (leave None to affect all)")]
    public LayerMask layerMask = ~0;

    [Tooltip("Allow damaging same object repeatedly on re-enter? (OnEnter only)")]
    public bool allowRepeatOnEnter = false;

    // track who is inside (for OnStay) and cooldown for per-object ticks
    readonly Dictionary<GameObject, float> _nextTick = new();

    // track which objects already hit for OnEnter once-per-entry behavior
    readonly HashSet<GameObject> _enteredOnce = new();

    void Reset()
    {
        // ensure Collider exists
        var c = GetComponent<Collider>();
        if (c == null) Debug.LogWarning("TriggerDamageZone requires a Collider (isTrigger=true).");
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsInLayerMask(other.gameObject)) return;

        var go = other.gameObject;
        if (mode == Mode.OnEnter)
        {
            if (!allowRepeatOnEnter && _enteredOnce.Contains(go)) return;

            ApplyDamageTo(go, attacker: gameObject);
            _enteredOnce.Add(go);
            // For safety, schedule removal of _enteredOnce when they leave
        }
        else if (mode == Mode.OnStay)
        {
            // set immediate tick
            _nextTick[go] = Time.time;
        }
    }

    void OnTriggerExit(Collider other)
    {
        var go = other.gameObject;
        if (_nextTick.ContainsKey(go)) _nextTick.Remove(go);
        if (_enteredOnce.Contains(go)) _enteredOnce.Remove(go);
    }

    void Update()
    {
        if (mode != Mode.OnStay) return;
        if (_nextTick.Count == 0) return;

        float now = Time.time;
        float interval = 1f / Mathf.Max(0.001f, tickRate);

        // collect keys to avoid modifying dictionary during iteration
        var keys = new List<GameObject>(_nextTick.Keys);
        foreach (var go in keys)
        {
            if (go == null)
            {
                _nextTick.Remove(go);
                continue;
            }

            if (now >= _nextTick[go])
            {
                ApplyDamageTo(go, attacker: gameObject, perSecond: true, tickInterval: interval);
                _nextTick[go] = now + interval;
            }
        }
    }

    bool IsInLayerMask(GameObject go)
    {
        if (layerMask == (LayerMask)(-1)) return true;
        return (layerMask & (1 << go.layer)) != 0;
    }

    void ApplyDamageTo(GameObject victimGO, GameObject attacker = null, bool perSecond = false, float tickInterval = 0.25f)
    {
        // ƯU TIÊN PlayerLifeController để trap đánh player vẫn đi qua logic revive
        IDamageable dmgComp = null;
        GameObject victimRoot = victimGO;

        // 1) Thử tìm PlayerLifeController trong nhánh cha
        var playerLife = victimGO.GetComponentInParent<PlayerLifeController>(true);
        if (playerLife != null)
        {
            dmgComp = playerLife;
            victimRoot = playerLife.gameObject;
        }
        else
        {
            // 2) Fallback sang IDamageable chung
            dmgComp = victimGO.GetComponent<IDamageable>();
            if (dmgComp == null)
            {
                dmgComp = victimGO.GetComponentInParent<IDamageable>();
            }

            if (dmgComp is Component comp)
                victimRoot = comp.gameObject;
        }

        if (dmgComp == null)
        {
            Debug.Log($"[TriggerDamageZone] '{victimGO.name}' not IDamageable, skip.");
            return;
        }

        // build DamageEvent
        DamageEvent e = new DamageEvent
        {
            attacker = attacker,
            victimGO = victimRoot,   // dùng root đúng của component IDamageable
            victim = dmgComp,
            weaponId = weaponId,
            damageType = damageType,
            source = damageSource,
            baseDamage = perSecond ? damage * tickInterval : damage,
            distance = 0f,
            hitPoint = victimRoot.transform.position,
            hitCollider = null,
            time = Time.time,
            isCritical = false,
            friendlyFireIgnored = true
        };

        var result = DamageRouter.Apply(e);
        Debug.Log($"[TriggerDamageZone] Applied {e.baseDamage} to '{victimRoot.name}' -> {result}");
    }


    // Optional gizmo for editor visualization
    void OnDrawGizmosSelected()
    {
        var c = GetComponent<Collider>();
        if (c == null) return;
        Gizmos.color = (mode == Mode.OnEnter) ? Color.yellow : Color.red;
#if UNITY_6000_0_OR_NEWER
        // collider bounds drawing
#endif
        Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
    }
}
