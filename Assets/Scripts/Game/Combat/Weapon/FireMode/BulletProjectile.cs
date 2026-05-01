using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BulletProjectile : MonoBehaviour
{
    Rigidbody _rb;
    GameObject _attacker;
    WeaponDef _def;
    float _spawnTime;

    public void Init(GameObject attacker, WeaponDef def)
    {
        _attacker = attacker;
        _def = def;
        _rb = GetComponent<Rigidbody>();
#if UNITY_6000_0_OR_NEWER
        _rb.linearVelocity = transform.forward * _def.projectileSpeed;
#else
        _rb.velocity = transform.forward * _def.projectileSpeed;
#endif
        _spawnTime = Time.time;
    }

    void FixedUpdate()
    {
        // tự huỷ sau 5s (an toàn)
        if (Time.time - _spawnTime > 10f) Destroy(gameObject);
    }

    void OnCollisionEnter(Collision c)
    {
        if (DamageSystem.Instance == null || _def == null) { Destroy(gameObject); return; }

        var col = c.collider;
        var hb = col.GetComponent<Hitbox>();
        IDamageable victim = hb ? hb.GetDamageable() : col.GetComponentInParent<IDamageable>();
        var victimGO = victim != null ? (victim as MonoBehaviour).gameObject : col.gameObject;

        var cp = c.GetContact(0);
        Vector3 hitPoint = cp.point;
        Vector3 normal = cp.normal;

/*        if (_def.impactFX_Default)
        {
            var go = Instantiate(_def.impactFX_Default, hitPoint, Quaternion.LookRotation(normal));
            Destroy(go, 3f);
        }*/

        if (victim != null)
        {
#if UNITY_6000_0_OR_NEWER
            Vector3 shotDir = _rb.linearVelocity.sqrMagnitude > 1e-4f ? _rb.linearVelocity.normalized : transform.forward;
#else
            Vector3 shotDir = _rb.velocity.sqrMagnitude > 1e-4f ? _rb.velocity.normalized : transform.forward;
#endif
            var e = new DamageEvent
            {
                attacker = _attacker,
                victimGO = victimGO,
                victim = victim,
                weaponId = _def.weaponId,
                baseDamage = _def.baseDamage,
                damageType = _def.damageType,
                distance = 0f,
                penetrationCount = 0,
                hitPoint = hitPoint,
                hitNormal = normal,
                shotDirection = shotDir,
                hitCollider = col,
                hitboxId = hb ? hb.hitboxId : HitboxId.Default,
                time = Time.time
            };
            DamageSystem.Instance.Apply(e);
        }

        Destroy(gameObject);
    }
}
