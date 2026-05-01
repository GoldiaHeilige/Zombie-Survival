using UnityEngine;
using TT;

[DisallowMultipleComponent]
public class BarricadeAudioDriver : MonoBehaviour
{
    [Header("Link")]
    [SerializeField] private BarricadeWindow window;
    [SerializeField] private Transform audioOrigin;

    [Header("3D SFX")]
    [SerializeField] private AudioEventSO hitSfx;
    [SerializeField] private AudioEventSO breakSfx;
    [SerializeField] private AudioEventSO buildSnapSfx;
    [SerializeField] private AudioEventSO repairSfx;   // repair thêm cùng lúc snap

    void Awake()
    {
        if (!window)
            window = GetComponent<BarricadeWindow>();

        if (!audioOrigin && window)
            audioOrigin = window.transform;
    }

    void OnEnable()
    {
        if (Observer.Instance == null) return;

        Observer.Instance.AddObserver(BarricadeTopics.Hit, OnHitEvent);
        Observer.Instance.AddObserver(BarricadeTopics.BoardBroken, OnBoardBrokenEvent);
        Observer.Instance.AddObserver(BarricadeTopics.BoardBuilt, OnBoardBuiltEvent);
    }

    void OnDisable()
    {
        if (Observer.Instance == null) return;

        Observer.Instance.RemoveObserver(BarricadeTopics.Hit, OnHitEvent);
        Observer.Instance.RemoveObserver(BarricadeTopics.BoardBroken, OnBoardBrokenEvent);
        Observer.Instance.RemoveObserver(BarricadeTopics.BoardBuilt, OnBoardBuiltEvent);
    }

    bool IsMyWindow(BarricadeRepairEvent evt)
    {
        if (!window || !evt.window) return false;
        return evt.window == window.gameObject;
    }

    void OnHitEvent(object payload)
    {
        if (!(payload is BarricadeRepairEvent evt)) return;
        if (!IsMyWindow(evt)) return;
        if (!hitSfx || !audioOrigin) return;

        AudioEvents.PlayWorld3D(hitSfx.eventId, audioOrigin.position);
    }

    void OnBoardBrokenEvent(object payload)
    {
        if (!(payload is BarricadeRepairEvent evt)) return;
        if (!IsMyWindow(evt)) return;
        if (!breakSfx || !audioOrigin) return;

        AudioEvents.PlayWorld3D(breakSfx.eventId, audioOrigin.position);
    }

    void OnBoardBuiltEvent(object payload)
    {
        if (!(payload is BarricadeRepairEvent evt)) return;
        if (!IsMyWindow(evt)) return;
        if (!audioOrigin) return;

        // 1) Tiếng board snap vào
        if (buildSnapSfx)
        {
            AudioEvents.PlayWorld3D(buildSnapSfx.eventId, audioOrigin.position);
        }

        // 2) Thêm tiếng repair extra như anh yêu cầu
        if (repairSfx)
        {
            AudioEvents.PlayWorld3D(repairSfx.eventId, audioOrigin.position);
        }
    }
}
