// Assets/Scripts/Combat/Damage/Processors/HitboxMultiplierProcessor.cs
using UnityEngine;

public class HitboxMultiplierProcessor : IDamageProcessor
{
    public bool Process(ref DamageEvent e)
    {
        // Lấy WeaponDef từ weaponId
        int key = WeaponIdRegistry.GetKey(e.weaponId);
        var def = WeaponIdRegistry.GetDef(key);

        float m = 1f;
        if (def != null)
            m = def.GetHitboxMultiplier(e.hitboxId);

        e.baseDamage *= m;

        // ❗ Không set isCritical ở đây nữa
        return true;
    }
}
