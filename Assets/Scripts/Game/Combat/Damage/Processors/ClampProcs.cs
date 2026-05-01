using UnityEngine;

public class ClampProcessor : IDamageProcessor
{
    public float minDamage = 0f;
    public float maxDamage = 10000f;
    public bool Process(ref DamageEvent e)
    {
        e.baseDamage = Mathf.Clamp(e.baseDamage, minDamage, maxDamage);
        return true;
    }
}