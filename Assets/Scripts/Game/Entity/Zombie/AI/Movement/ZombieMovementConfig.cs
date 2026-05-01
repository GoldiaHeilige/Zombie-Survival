using UnityEngine;

[CreateAssetMenu(menuName = "TT/AI/Zombie Movement Config")]
public class ZombieMovementConfig : ScriptableObject
{
    [Header("Speeds")]
    public float walkSpeed = 2.5f;
    public float chaseSpeed = 3.5f;
    public float attackStopDistance = 1.5f;

    [Header("Rotation")]
    public float rotationSpeed = 360f;

    [Header("Misc")]
    public float stuckWarpDistance = 0.6f;  // tránh kẹt (optional)
}
