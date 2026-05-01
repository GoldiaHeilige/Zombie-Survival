using UnityEngine;

[CreateAssetMenu(fileName = "MovementConfig_Default", menuName = "Game/Movement Config")]
public class MovementConfig : ScriptableObject
{
    [Header("Base Speeds (m/s)")]
    public float baseWalkSpeed = 4.5f;
    public float baseSprintSpeed = 7.5f;

    [Header("Control & Physics")]
    [Range(0f, 1f)] public float airControl = 0.6f;
    public float acceleration = 18f;       // tăng tốc m/s²
    public float gravity = -9.81f * 2f;    // cảm giác nặng hơn một chút
    public float jumpHeight = 1.2f;

    [Header("Stamina")]
    public float staminaMax = 100f;
    public float staminaDrainSprint = 18f; // /s khi sprint
    public float staminaRegen = 22f;       // /s khi không sprint
    public float staminaRegenDelay = 0.6f; // đợi trước khi hồi
    public float minSprintToStart = 10f;   // stamina tối thiểu để bắt đầu sprint
}
