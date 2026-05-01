using System;
using UnityEngine;

public enum MovStat
{
    WalkSpeedMult,       // % vào walk speed
    SprintSpeedMult,     // % vào sprint speed
    FlatSpeedAdd,        // +m/s vào tốc độ cuối cùng
    AccelMult,           // % vào acceleration
    AirControlMult,      // % vào airControl
    JumpHeightMult,      // % vào jumpHeight
    GravityMult,         // % vào |gravity|
    StaminaMaxAdd,       // + vào staminaMax
    StaminaRegenMult,    // % vào staminaRegen
    StaminaDrainMult     // % vào staminaDrainSprint
}

public enum ModOp { Add, Mul } // Add: cộng thẳng; Mul: nhân (1 + value)

[Serializable]
public class MovementModifier
{
    public string id = Guid.NewGuid().ToString();
    public string source = "default";
    public float duration = 3f;         // <0 nghĩa là vĩnh viễn
    public int priority = 0;            // ưu tiên cao sẽ “ghi đè” nếu cần (chưa dùng nâng cao)

    [Serializable]
    public struct Entry
    {
        public MovStat stat;
        public ModOp op;
        public float value;   // Mul: 0.2 = +20%; Add: +m/s hoặc +đơn vị trực tiếp
    }

    public Entry[] entries = Array.Empty<Entry>();
}