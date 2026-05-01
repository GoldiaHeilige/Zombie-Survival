#if FUSION_WEAVER
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameOverNet : NetworkBehaviour
{
    public static GameOverNet Instance { get; private set; }

    [Networked] public NetworkBool Finalized { get; private set; }

    // Header cần sync
    [Networked] public NetworkString<_32> MapName { get; private set; }
    [Networked] public int RoundsSurvived { get; private set; }

    // time sync dạng ms để tránh float drift
    [Networked] public int TimeSurvivedMs { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void Spawned()
    {
        // đảm bảo singleton even when scene objects spawn
        if (Instance != this) Instance = this;
    }

    /// <summary>
    /// Chỉ host/state authority gọi.
    /// Chốt header stats của match và replicate cho client.
    /// </summary>
    public void FinalizeOnHost(RoundDirector director)
    {
        if (!Object || !Object.HasStateAuthority) return;
        if (Finalized) return;

        // Map name – ưu tiên GameSession.SelectedMap, fallback scene name (y như snapshot cũ)
        string mapName = GameSession.SelectedMapDisplayName;
        if (string.IsNullOrWhiteSpace(mapName))
            mapName = SceneManager.GetActiveScene().name;

        // Chốt clock (host)
        MatchClock.Stop();
        float seconds = MatchClock.FinalSeconds;

        MapName = mapName;
        RoundsSurvived = director ? Mathf.Max(0, director.roundIndex) : 0;
        TimeSurvivedMs = Mathf.Max(0, Mathf.RoundToInt(seconds * 1000f));

        Finalized = true;

        // Debug nhẹ
        // Debug.Log($"[GameOverNet] Finalized: map={MapName} rounds={RoundsSurvived} timeMs={TimeSurvivedMs}");
    }

    public bool TryGetHeader(out string map, out int rounds, out float seconds)
    {
        map = default;
        rounds = 0;
        seconds = 0f;

        if (!Finalized) return false;

        map = MapName.ToString();
        rounds = RoundsSurvived;
        seconds = TimeSurvivedMs / 1000f;
        return true;
    }
}
#endif
