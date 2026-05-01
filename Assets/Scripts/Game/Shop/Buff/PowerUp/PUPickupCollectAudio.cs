using UnityEngine;
using TT;

[DisallowMultipleComponent]
public class PowerUpPickupCollectAudio : MonoBehaviour
{
    [Tooltip("2D global SFX when collected (everyone hears).")]
    [SerializeField] private int collectSfxEventId;

    public void PlayCollectSfx()
    {
        if (collectSfxEventId == 0) return;

        // Global 2D: SP local, MP host -> broadcast
        AudioEvents.PlayUiGlobal(collectSfxEventId);
    }
}
