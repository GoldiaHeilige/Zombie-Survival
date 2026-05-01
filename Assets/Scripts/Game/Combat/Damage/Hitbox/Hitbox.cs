using UnityEngine;

[DisallowMultipleComponent]
public class Hitbox : MonoBehaviour
{
    public HitboxId hitboxId = HitboxId.Default;
    [Tooltip("Nếu null, tìm IDamageable ở parent")]
    public MonoBehaviour damageableOverride;

    public IDamageable GetDamageable()
    {
        if (damageableOverride != null) return damageableOverride as IDamageable;
        return GetComponentInParent<IDamageable>();
    }
}
