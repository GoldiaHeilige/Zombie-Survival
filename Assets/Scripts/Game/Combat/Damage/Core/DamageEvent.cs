using UnityEngine;

public struct DamageEvent
{
    // Who & what
    public GameObject attacker;           // có thể null (bẫy, môi trường)
    public GameObject victimGO;           // để tiện truy cập transform, team…
    public IDamageable victim;            // cache nếu có
    public string weaponId;               // "pistol_9mm", "zombie_bite"...

    // Numbers
    public float baseDamage;              // raw trước pipeline
    public DamageType damageType;
    public DamageSource source;
    public float distance;                // mét (nếu có)
    public int penetrationCount;          // số mục tiêu/vật thể xuyên trước đó

    // Hit info (vật thể trúng & điểm chạm)
    public Vector3 hitPoint;
    public Vector3 hitNormal;
    public Vector3 shotDirection;         // hướng đạn/hitscan
    public Collider hitCollider;
    public HitboxId hitboxId;

    // Impact (NEW): dùng cho knockback/ragdoll/anim reactive
    public Vector3 impactDirection;       // chuẩn hóa nếu có thể
    public float impactForce;           // lực tương đối, tuỳ hệ thống tiêu thụ

    // Flags gợi ý
    public bool isCritical;               // ví dụ headshot
    public bool friendlyFireIgnored;      // nếu pipeline bỏ qua vì cùng team

    // Timestamp / RNG
    public float time;
}
