using UnityEngine;

public class PlayerLifeAnnouncer : MonoBehaviour
{
    [Header("Chỉ print log demo. Bạn có thể phát UI/SFX ở đây.")]
    public bool logToConsole = true;

/*    void OnEnable()
    {
        PlayerLifeController.OnDowned += Downed;
        PlayerLifeController.OnRevived += Revived;
        PlayerLifeController.OnRespawned += Respawned;
        PlayerLifeController.OnDead += Dead;
    }
    void OnDisable()
    {
        PlayerLifeController.OnDowned -= Downed;
        PlayerLifeController.OnRevived -= Revived;
        PlayerLifeController.OnRespawned -= Respawned;
        PlayerLifeController.OnDead -= Dead;
    }

    void Downed(PlayerLifeController p) { if (logToConsole) Debug.Log($"[Announce] {p.name} DOWNED"); }
    void Revived(PlayerLifeController p) { if (logToConsole) Debug.Log($"[Announce] {p.name} REVIVED"); }
    void Respawned(PlayerLifeController p) { if (logToConsole) Debug.Log($"[Announce] {p.name} RESPAWNED"); }
    void Dead(PlayerLifeController p) { if (logToConsole) Debug.Log($"[Announce] {p.name} DEAD"); }*/
}
