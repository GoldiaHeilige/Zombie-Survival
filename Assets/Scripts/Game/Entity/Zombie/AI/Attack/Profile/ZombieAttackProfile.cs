// Assets/Scripts/AI/Zombie/Attack/ZombieAttackProfile.cs
using System;
using UnityEngine;

public enum ZombieHitShape { Sphere, Capsule, Box }
public enum AttackSelectMode { HighestPriority, FirstValid, WeightedRandom }

[CreateAssetMenu(menuName = "AI/Zombie/Attack Profile", fileName = "AP_NewAttack")]
public class ZombieAttackProfile : ScriptableObject
{
    [Header("Meta")]
    public string displayName = "Light Bite";
    [Tooltip("Ưu tiên chọn khi có nhiều chiêu cùng hợp lệ. Lớn hơn = ưu tiên hơn.")]
    public int priority = 0;
    [Range(0f, 1f)] public float weight = 1f; // dùng khi chọn WeightedRandom
    public int animationIndex;

    [Header("Damage")]
    public float damage = 25f;
    public float knockback = 3.5f;
    public string damageTag = "melee";

    [Header("Gating (distance, facing, LOS)")]
    [Min(0f)] public float minRange = 0f;
    [Min(0f)] public float maxRange = 2.0f;  
    [Range(0f, 180f)] public float facingAngle = 120f;
    public bool requireLOS = true;

    [Header("Hit Shape")]
    public ZombieHitShape shape = ZombieHitShape.Sphere;
    [Min(0f)] public float radius = 0.6f;
    [Min(0f)] public float height = 1.0f;
    public Vector3 boxHalfExtents = new Vector3(0.5f, 0.5f, 0.6f);
    public Vector3 shapeLocalOffset = new Vector3(0f, 0f, 0.35f);

    [Header("Timing")]
    [Min(0f)] public float windupTime = 0.25f;
    [Min(0f)] public float activeTime = 0.10f;
    [Min(0f)] public float recoveryTime = 0.35f;
    [Min(0f)] public float cooldown = 0.60f;

    [Tooltip("Nếu muốn nhiều cửa sổ gây dmg (VD multi-swipe), khai báo thêm các active window bổ sung (bắt đầu tính sau windup).")]
    public Vector2[] extraActiveWindows; // (start, duration)

    public bool allowMoveDuringAttack = false;

    [Header("Selector Hints")]
    public bool onlyWhenTargetCentered = false; // ví dụ yêu cầu facingAngle chặt chẽ
}
