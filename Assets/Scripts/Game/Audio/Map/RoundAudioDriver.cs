using UnityEngine;
using TT;


#if FUSION_WEAVER
using Fusion;
#endif

public class RoundAudioDriver : MonoBehaviour
{
    [Header("Optional override (leave null = auto-find)")]
    [SerializeField] private RoundDirector director; // đọc config audio từ đây

#if FUSION_WEAVER
    private RoundStateNet net;
#endif

    private void Awake()
    {
        if (!director)
            director = FindFirstObjectByType<RoundDirector>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        Hook();
    }

    private void OnDisable()
    {
        Unhook();
    }

    void Hook()
    {
        Unhook();

        // SP -> nghe RoundDirector
        if (GameSession.Mode == AppPlayMode.Single)
        {
            if (director != null)
            {
                director.OnRoundChanged += OnRoundChanged;
                director.OnRoundEnded += OnRoundEnded;
            }
            return;
        }

        // MP -> nghe RoundStateNet
#if FUSION_WEAVER
        net = RoundStateNet.Instance ?? FindFirstObjectByType<RoundStateNet>(FindObjectsInactive.Include);
        if (net != null)
        {
            net.OnRoundChanged += OnRoundChanged;
            net.OnRoundEnded += OnRoundEnded;
        }
#endif
    }

    void Unhook()
    {
        if (director != null)
        {
            director.OnRoundChanged -= OnRoundChanged;
            director.OnRoundEnded -= OnRoundEnded;
        }

#if FUSION_WEAVER
        if (net != null)
        {
            net.OnRoundChanged -= OnRoundChanged;
            net.OnRoundEnded -= OnRoundEnded;
            net = null;
        }
#endif
    }

    void OnRoundChanged(int round)
    {
        if (round <= 0) return;
        PlayRoundStart(round);
    }

    void OnRoundEnded(int round)
    {
        if (round <= 0) return;
        PlayRoundEnd(round);
    }

    void PlayRoundStart(int round)
    {
        if (director == null) return;

        // 1) Round 1 special
        if (round == 1 && !string.IsNullOrEmpty(director.roundFirstStartUIEvent))
        {
            AudioEvents.PlayUiGlobal(director.roundFirstStartUIEvent);
            return;
        }

        // 2) Milestone
        if (director.milestoneEveryNRounds > 0 &&
            (round % director.milestoneEveryNRounds) == 0 &&
            !string.IsNullOrEmpty(director.milestoneRoundStartUIEvent))
        {
            AudioEvents.PlayUiGlobal(director.milestoneRoundStartUIEvent);
            return;
        }

        // 3) Default
        if (!string.IsNullOrEmpty(director.roundStartUIEvent))
            AudioEvents.PlayUiGlobal(director.roundStartUIEvent);
    }

    void PlayRoundEnd(int round)
    {
        if (director == null) return;

        // 1) Milestone end
        if (director.milestoneEveryNRounds > 0 &&
            (round % director.milestoneEveryNRounds) == 0 &&
            !string.IsNullOrEmpty(director.milestoneRoundEndUIEvent))
        {
            AudioEvents.PlayUiGlobal(director.milestoneRoundEndUIEvent);
            return;
        }

        // 2) Default
        if (!string.IsNullOrEmpty(director.roundEndUIEvent))
            AudioEvents.PlayUiGlobal(director.roundEndUIEvent);
    }
}
