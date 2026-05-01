using UnityEngine;

public class CritChanceProcessor : IDamageProcessor
{
    public bool Process(ref DamageEvent e)
    {
        // Nếu đã crit từ nguồn khác (perk / script khác) thì giữ nguyên
        if (e.isCritical) return true;

        int key = WeaponIdRegistry.GetKey(e.weaponId);
        var def = WeaponIdRegistry.GetDef(key);
        if (def == null) return true;

        float chance = Mathf.Clamp01(def.critChance);
        if (chance <= 0f) return true;

        // Roll crit (áp dụng cho mọi hitbox)
        if (Random.value < chance)
        {
            e.isCritical = true;
            e.baseDamage *= Mathf.Max(1f, def.critMultiplier);
        }

        return true;
    }
}
