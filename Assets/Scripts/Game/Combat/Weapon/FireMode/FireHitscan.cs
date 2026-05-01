using TT;
using UnityEngine;

public static class FireHitscan
{
    public static void Fire(WeaponController wc, WeaponDef def, Vector3 origin, Vector3 dir)
    {
        Fire(wc, def, origin, dir, spawnImpact: true);
    }

    public static void Fire(WeaponController wc, WeaponDef def, Vector3 origin, Vector3 dir, bool spawnImpact)
    {
        // ===== Penetration loop =====
        int maxPen = (def.enablePenetration) ? Mathf.Max(0, def.maxPenetrations) : 0;

        float remaining = def.maxDistance;
        Vector3 curOrigin = origin;
        int penCount = 0;

        // tránh hit lại cùng collider khi vừa xuyên qua
        var ignored = new System.Collections.Generic.HashSet<Collider>();
        // NEW: chặn multi-hitbox cùng 1 victim trong cùng 1 phát ray
        var hitVictimRoots = new System.Collections.Generic.HashSet<int>();

        while (remaining > 0.001f)
        {
            if (!Physics.Raycast(curOrigin, dir, out var hit, remaining, def.hitMask, QueryTriggerInteraction.Ignore))
                break;

            // Nếu collider đã bị xuyên qua rồi mà vẫn dính lại (góc kỳ), skip nhẹ
            if (hit.collider != null && ignored.Contains(hit.collider))
            {
                curOrigin = hit.point + dir * 0.02f;
                remaining -= hit.distance + 0.02f;
                continue;
            }

            // Hitbox → IDamageable
            var hb = hit.collider.GetComponent<Hitbox>();

            var armor = hit.collider.GetComponentInParent<ArmorPlate>();
            bool isHeadHit = hb != null && hb.hitboxId == HitboxId.Head;

            // Armor chặn damage => coi như "stop bullet" (không xuyên tiếp)
            if (armor != null && armor.blocksDamage && !(armor.allowHeadshot && isHeadHit))
            {
                if (spawnImpact)
                    SpawnImpact(wc, def, hit, victim: null);
                break;
            }

            IDamageable victim = hb ? hb.GetDamageable() : hit.collider.GetComponentInParent<IDamageable>();
            var victimGO = victim != null ? (victim as MonoBehaviour).gameObject : hit.collider.gameObject;

            // ✅ Spawn impact cho cả world hit
            if (spawnImpact)
                SpawnImpact(wc, def, hit, victim);

            // ✅ World hit thì dừng sau khi đã spawn impact
            if (victim == null)
                break;

            // (tuỳ bạn) nếu DamageSystem.Instance null thì cũng dừng ở đây,
            // nhưng impact đã spawn rồi nên không còn “mất impact” nữa
            if (DamageSystem.Instance == null)
                break;


            // NEW: victim root key (ưu tiên root của DamageableHealth / victimGO)
            int victimRootId = 0;
            if (victimGO != null)
            {
                var root = victimGO.transform.root;
                victimRootId = root != null ? root.gameObject.GetInstanceID() : victimGO.GetInstanceID();
            }

            // Nếu ray này đã từng tính damage lên victim này rồi => skip, không apply nữa
            if (victimRootId != 0 && hitVictimRoots.Contains(victimRootId))
            {
                // mark collider để khỏi dính lại + tiến origin lên tiếp để ray đi qua
                if (hit.collider != null) ignored.Add(hit.collider);

                curOrigin = hit.point + dir * 0.02f;
                remaining -= hit.distance + 0.02f;
                continue;
            }

            if (victimRootId != 0)
                hitVictimRoots.Add(victimRootId);


            // Impact FX/SFX mỗi lần chạm (tuỳ bạn, đang để ON)
            if (spawnImpact)
                SpawnImpact(wc, def, hit, victim);

            // Nếu hit vào world (không có victim) => stop
            if (victim == null || DamageSystem.Instance == null)
                break;

            // ===== Damage falloff theo số lần xuyên =====
            float mul = 1f;
            if (penCount > 0)
            {
                // penCount=1 => đã xuyên qua 1 thằng trước đó
                mul = Mathf.Pow(Mathf.Clamp01(def.damageMultiplierPerPenetration), penCount);
            }

            float scaledDamage = def.baseDamage * mul;
            if (def.minDamageAfterPenetration > 0f)
                scaledDamage = Mathf.Max(def.minDamageAfterPenetration, scaledDamage);

            GameObject attackerGO = null;
            if (wc != null)
                attackerGO = (wc.owner != null) ? wc.owner.gameObject : wc.gameObject;

            var e = new DamageEvent
            {
                attacker = attackerGO,
                victimGO = victimGO,
                victim = victim,
                weaponId = def.weaponId,

                // ✅ quan trọng: đưa damage đã scale vào baseDamage
                baseDamage = scaledDamage,

                damageType = def.damageType,
                distance = hit.distance,
                penetrationCount = penCount,
                hitPoint = hit.point,
                hitNormal = hit.normal,
                shotDirection = dir,
                hitCollider = hit.collider,
                hitboxId = hb ? hb.hitboxId : HitboxId.Default,
                time = Time.time
            };

            IDamageDriver driver = null;
            var attackerObj = e.attacker != null ? e.attacker : (wc != null && wc.owner != null ? wc.owner.gameObject : null);
            if (attackerObj != null)
                driver = attackerObj.GetComponentInChildren<IDamageDriver>(true);

            _ = (driver != null) ? driver.Apply(e) : DamageRouter.Apply(e);

            // ===== Có xuyên tiếp không? =====
            if (penCount >= maxPen)
                break;

            // Mark ignore collider vừa xuyên qua để khỏi hit lại
            if (hit.collider != null) ignored.Add(hit.collider);

            // Tiến origin lên chút để ray tiếp theo bắt đầu "sau" collider
            curOrigin = hit.point + dir * 0.02f;
            remaining -= hit.distance + 0.02f;

            penCount++;
        }

    }

    static void SpawnImpact(WeaponController wc, WeaponDef def, RaycastHit hit, IDamageable victim)
    {
        bool isFlesh = victim != null;
        SurfaceType surface = ImpactHelper.DetectSurface(hit, isFlesh);

#if FUSION_WEAVER
        if (wc != null && wc.owner != null)
        {
            var bridge = wc.owner.GetComponentInChildren<FusionNetBridge>(true);
            if (bridge != null && bridge.HasStateAuth)
            {
                int weaponKey = WeaponIdRegistry.GetKey(def.weaponId);
                bridge.RPC_SpawnImpact(hit.point, hit.normal, weaponKey, (int)surface);
                return;
            }
        }
#endif

        // SP or no authority → spawn local
        var fx = ImpactHelper.GetVFX(def, surface);
        if (fx != null)
            ImpactPool.Instance?.Spawn(fx, hit.point, hit.normal, -1f, 0.002f);

        var sfx = ImpactHelper.GetSFX(def, surface);
        if (sfx != null)
            AudioEvents.PlayWorld3D(sfx.eventId, hit.point);

        if (wc != null && wc.owner != null)
        {
            var worldView = wc.owner.GetComponentInChildren<PlayerWeaponWorldView>(true);
            if (worldView != null)
            {
                int weaponKey = WeaponIdRegistry.GetKey(def.weaponId);
                worldView.PlayWorldMuzzleForWeaponKey(weaponKey);
            }
        }

    }
}
