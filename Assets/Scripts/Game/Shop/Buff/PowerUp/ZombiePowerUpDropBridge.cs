using UnityEngine;

/// <summary>
/// Attach to Zombie prefab (or add dynamically) to roll power-up drops when it dies.
/// Uses DamageableHealth.OnDeathLocal, similar to your DamageableDeathBridge.
/// </summary>
[DisallowMultipleComponent]
public class ZombiePowerUpDropBridge : MonoBehaviour
{
    DamageableHealth _hp;
    PowerUpDropSystem _drop;

    void Awake()
    {
        _hp = GetComponent<DamageableHealth>();
        if (_hp != null) _hp.OnDeathLocal += OnDeathLocal;

        _drop = FindFirstObjectByType<PowerUpDropSystem>(FindObjectsInactive.Include);
    }

    void OnDestroy()
    {
        if (_hp != null) _hp.OnDeathLocal -= OnDeathLocal;
    }

    void OnDeathLocal(DamageEvent e, DamageResult r)
    {
        if (_drop == null)
            _drop = FindFirstObjectByType<PowerUpDropSystem>(FindObjectsInactive.Include);

        if (_drop == null) return;

        _drop.TryRollDrop(transform.position);
    }
}
