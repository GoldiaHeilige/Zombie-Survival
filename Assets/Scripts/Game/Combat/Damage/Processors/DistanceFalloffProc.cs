using UnityEngine;

public class DistanceFalloffProcessor : IDamageProcessor
{
    public float start = 15f; // bắt đầu rớt
    public float end = 40f;   // hết tầm sát thương
    public float minFactor = 0.5f; // còn lại bao nhiêu ở cực xa

    public bool Process(ref DamageEvent e)
    {
        if (e.damageType != DamageType.Bullet) return true;
        if (e.distance <= start) return true;
        float t = Mathf.InverseLerp(start, end, e.distance);
        float f = Mathf.Lerp(1f, minFactor, t);
        e.baseDamage *= f;
        return true;
    }
}
