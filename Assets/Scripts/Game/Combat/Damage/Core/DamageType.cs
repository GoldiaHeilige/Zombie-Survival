using UnityEngine;

public enum DamageType { Bullet, Melee, Explosive, Fire, Electric }

public enum DamageSource { Unknown = 0, Player = 1, AI = 2, Environment = 3, Trap = 4 , PowerUp = 5}

public enum TeamId { Neutral = 0, Player = 1, Enemy = 2 }
// DamageType.cs
public enum HitboxId
{
    Default, Head, Chest, Stomach, Arm, Leg,

    // NEW (append only - don't reorder existing)
    LeftArm,
    RightArm
}

