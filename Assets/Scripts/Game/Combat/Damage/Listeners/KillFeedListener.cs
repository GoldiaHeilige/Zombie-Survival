using UnityEngine;

public class KillFeedListener : MonoBehaviour
{
    void OnEnable()
    {
        if (DamageSystem.Instance != null)
            DamageSystem.Instance.OnDeath += HandleDeath;
    }
    void OnDisable()
    {
        if (DamageSystem.Instance != null)
            DamageSystem.Instance.OnDeath -= HandleDeath;
    }
    void HandleDeath(DamageEvent e, DamageResult r)
    {
        string victimName = e.victimGO ? e.victimGO.name : "Unknown";
        string atk = e.attacker ? e.attacker.name : "ENV";
        string tag = e.isCritical ? " (HEADSHOT)" : "";
  //      Debug.Log($"[KillFeed] {atk} killed {victimName} with {e.weaponId}{tag}");
        // TODO: gửi lên UI queue thay vì Debug.Log
    }
}
