using UnityEngine;

public sealed class LimbDetachProcessor : IDamageProcessor
{
    public bool Process(ref DamageEvent e)
    {
        if (e.victimGO == null) return true;

        var limb = e.victimGO.GetComponentInParent<ZombieLimbController>();
        if (limb == null) return true;

        float mul = 1f;

        // ---- NEW: weapon-based gating ----
        if (!string.IsNullOrEmpty(e.weaponId))
        {
            int key = WeaponIdRegistry.GetKey(e.weaponId);
            var def = WeaponIdRegistry.GetDef(key); // lazy init, cross SP/MP :contentReference[oaicite:3]{index=3}
            if (def != null)
            {
                mul = def.limbDetachMultiplier;
                if (mul <= 0f) return true; // pistol/weak gun => never detach
            }
        }

        limb.ProcessLimbDamage(ref e, mul);
        return true;
    }
}
