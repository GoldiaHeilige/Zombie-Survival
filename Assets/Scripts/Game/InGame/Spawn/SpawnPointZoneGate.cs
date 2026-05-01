using UnityEngine;

/// <summary>
/// Gắn lên cùng GameObject với SpawnPoint để giới hạn việc spawn theo zone.
/// Nếu không gán zoneRequired thì spawnpoint luôn hoạt động.
/// Nếu gán zoneRequired thì chỉ hoạt động khi zone đó đã được mở.
/// </summary>
[DisallowMultipleComponent]
public class SpawnPointZoneGate : MonoBehaviour
{
    [Tooltip("Zone (cửa) cần phải được mở để spawnpoint này hoạt động. Để trống = luôn hoạt động.")]
    public ZoneUnlockablePoints zoneRequired;

    [Tooltip("Nếu false thì tạm thời disable spawnpoint này (ngoài IsOnCooldown của chính SpawnPoint).")]
    public bool enabledForSpawning = true;

    /// <summary>
    /// SpawnManager sẽ gọi property này để quyết định có dùng spawnpoint hay không.
    /// </summary>
    public bool IsZoneOpenForSpawn
    {
        get
        {
            if (!enabledForSpawning)
                return false;

            // Không gán zone => luôn cho phép
            if (!zoneRequired)
                return true;

            // Nếu zoneRequired đã bị Destroy sau khi mở cửa,
            // reference sẽ trở thành null → coi như mở (đúng ý mình).
            return zoneRequired.IsUnlocked;
        }
    }
}
