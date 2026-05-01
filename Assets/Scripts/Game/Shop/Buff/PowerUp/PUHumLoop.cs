using UnityEngine;
using TT;

[DisallowMultipleComponent]
public class PowerUpPickupHumLoop : MonoBehaviour
{
    [Tooltip("3D attached LOOP hum while pickup exists (local-per-client).")]
    [SerializeField] private int humLoopEventId;

    [Tooltip("Fade out time when pickup is collected/despawned.")]
    [SerializeField] private float fadeOut = 0.15f;

    private AudioHandle _humHandle;
    private bool _started;

    void OnEnable()
    {
        // Pool-friendly: OnEnable sẽ chạy mỗi lần object được reuse.
        StartHumIfNeeded();
    }

    void OnDisable()
    {
        // Pool-friendly: when despawned/disabled -> stop hum.
        StopHum();
        _started = false;
    }

    void OnDestroy()
    {
        StopHum();
    }

    void StartHumIfNeeded()
    {
        if (_started) return;
        _started = true;

        if (humLoopEventId == 0) return;

        // IMPORTANT:
        // Dùng AudioManager local per client để tránh double RPC.
        // Và dùng Handle để stop/fade khi pickup biến mất.
        _humHandle = AudioManager.Instance
            ? AudioManager.Instance.Play3DAttachedHandle(humLoopEventId, transform)
            : default;
    }

    void StopHum()
    {
        if (!AudioManager.Instance) return;
        if (!_humHandle.IsValid) return;

        if (fadeOut > 0f)
            AudioManager.Instance.FadeOutAndStop(_humHandle, fadeOut);
        else
            AudioManager.Instance.Stop(_humHandle);

        _humHandle = default;
    }
}
