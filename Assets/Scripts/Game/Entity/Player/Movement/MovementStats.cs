using UnityEngine;
using System.Collections.Generic;

public class MovementStats
{
    // bản sao giá trị base từ Config
    public float baseWalkSpeed, baseSprintSpeed, airControl, acceleration, gravity, jumpHeight;
    public float staminaMax, staminaDrainSprint, staminaRegen;

    // modifiers cộng dồn
    float walkMult = 1f, sprintMult = 1f, flatSpeedAdd = 0f;
    float accelMult = 1f, airCtrlMult = 1f, jumpMult = 1f, gravMult = 1f;
    float stamMaxAdd = 0f, stamDrainMult = 1f, stamRegenMult = 1f;

    public void LoadFromConfig(MovementConfig cfg)
    {
        baseWalkSpeed = cfg.baseWalkSpeed;
        baseSprintSpeed = cfg.baseSprintSpeed;
        airControl = cfg.airControl;
        acceleration = cfg.acceleration;
        gravity = cfg.gravity;
        jumpHeight = cfg.jumpHeight;
        staminaMax = cfg.staminaMax;
        staminaDrainSprint = cfg.staminaDrainSprint;
        staminaRegen = cfg.staminaRegen;

        // reset accumulators
        walkMult = sprintMult = accelMult = airCtrlMult = jumpMult = gravMult = stamRegenMult = 1f;
        flatSpeedAdd = stamMaxAdd = 0f; stamDrainMult = 1f;
    }

    public void Apply(MovementModifier.Entry e)
    {
        switch (e.stat)
        {
            case MovStat.WalkSpeedMult: MulOrAdd(ref walkMult, e); break;
            case MovStat.SprintSpeedMult: MulOrAdd(ref sprintMult, e); break;
            case MovStat.FlatSpeedAdd: flatSpeedAdd += e.value; break;
            case MovStat.AccelMult: MulOrAdd(ref accelMult, e); break;
            case MovStat.AirControlMult: MulOrAdd(ref airCtrlMult, e); break;
            case MovStat.JumpHeightMult: MulOrAdd(ref jumpMult, e); break;
            case MovStat.GravityMult: MulOrAdd(ref gravMult, e); break;
            case MovStat.StaminaMaxAdd: stamMaxAdd += e.value; break;
            case MovStat.StaminaRegenMult: MulOrAdd(ref stamRegenMult, e); break;
            case MovStat.StaminaDrainMult: MulOrAdd(ref stamDrainMult, e); break;
        }
    }

    void MulOrAdd(ref float target, MovementModifier.Entry e)
    {
        if (e.op == ModOp.Mul) target *= (1f + e.value);
        else target += e.value;
    }

    // trả về tốc độ mục tiêu (base đã chọn) sau modifiers
    public float ResolveSpeed(bool sprinting)
    {
        float baseSpd = sprinting ? baseSprintSpeed * sprintMult : baseWalkSpeed * walkMult;
        return Mathf.Max(0f, baseSpd + flatSpeedAdd);
    }

    public float ResolveAccel() => Mathf.Max(0f, acceleration * accelMult);
    public float ResolveAirControl() => Mathf.Clamp01(airControl * airCtrlMult);
    public float ResolveJumpHeight() => Mathf.Max(0f, jumpHeight * jumpMult);
    public float ResolveGravity() => gravity * gravMult; // gravity là số âm, nhân hệ số
    public float ResolveStaminaMax() => Mathf.Max(1f, staminaMax + stamMaxAdd);
    public float ResolveStaminaRegen() => Mathf.Max(0f, staminaRegen * stamRegenMult);
    public float ResolveStaminaDrain() => Mathf.Max(0f, staminaDrainSprint * stamDrainMult);
}
