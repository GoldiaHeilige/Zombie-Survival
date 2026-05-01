using UnityEngine;

public class SpawnPoint : MonoBehaviour
{
    [Header("M0 config")]
    [Min(0f)] public float minPlayerDistance = 18f;
    [Min(0f)] public float localCooldown = 5f;
    [Min(0f)] public float weight = 1f;

    float _cooldownUntil;

    public bool IsOnCooldown => Time.time < _cooldownUntil;
    public void PunchCooldown() => _cooldownUntil = Time.time + localCooldown;

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, minPlayerDistance);
    }
}
