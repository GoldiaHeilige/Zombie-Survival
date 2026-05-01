// GameResultSnapshot.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Lý do kết thúc trận – tạm thời để Unknown, sau này bạn muốn phân loại thì mở rộng.</summary>
public enum GameOverReason
{
    Unknown = 0,
    AllPlayersDead = 1,
    HostLeft = 2,
    ManualQuit = 3,
}

/// <summary>Stat của từng player để hiển thị ở GameOverPanel.</summary>
[System.Serializable]
public class PlayerResultData
{
    public string playerName;
    public int points;
    public int kills;
    public int revives;
    public int downs;
}

/// <summary>Snapshot kết quả trận.</summary>
[System.Serializable]
public class GameResultSnapshot
{
    public string mapName;
    public int roundsSurvived;
    public float timeSurvivedSeconds;
    public GameOverReason reason;

    public List<PlayerResultData> players = new List<PlayerResultData>();
}

/// <summary>Static holder cho kết quả trận gần nhất.</summary>
public static class LastGameResult
{
    public static GameResultSnapshot Data { get; private set; }

    /// <summary>
    /// Build snapshot từ runtime hiện tại.
    /// Gọi khi GameOver (sau khi RoundDirector đã stop, tất cả stat dừng).
    /// </summary>
    public static void BuildFromRuntime(RoundDirector director, GameOverReason reason = GameOverReason.Unknown)
    {
        // Chốt thời gian
        MatchClock.Stop();

        var snap = new GameResultSnapshot();

        // Map name – ưu tiên GameSession.SelectedMap, fallback sang tên scene
        string mapName = GameSession.SelectedMapDisplayName;
        if (string.IsNullOrWhiteSpace(mapName))
            mapName = SceneManager.GetActiveScene().name;

        snap.mapName = mapName;
        snap.roundsSurvived = director ? Mathf.Max(0, director.roundIndex) : 0;
        snap.timeSurvivedSeconds = MatchClock.FinalSeconds;
        snap.reason = reason;

#if FUSION_WEAVER
        {
            // Nếu đang MP: client sẽ lấy header từ host (GameOverNet) để khỏi default do RoundDirector disabled
            var runner = Object.FindFirstObjectByType<Fusion.NetworkRunner>(FindObjectsInactive.Include);
            if (runner != null && runner.IsRunning)
            {
                var net = Object.FindFirstObjectByType<GameOverNet>(FindObjectsInactive.Include);
                if (net != null && net.TryGetHeader(out var m, out var r, out var t))
                {
                    if (!string.IsNullOrWhiteSpace(m)) snap.mapName = m;
                    snap.roundsSurvived = r;
                    snap.timeSurvivedSeconds = t;
                }
            }
        }
#endif


        // Lấy player từ PlayerRegistry để đúng thứ tự lobby
        var players = PlayerRegistry.GetAllValidPlayers();
        foreach (var refs in players)
        {
            if (!refs) continue;

            var root = refs.gameObject;
            if (!root) continue;

            var result = new PlayerResultData();

            // ƯU TIÊN: tên từ FusionNetBridge (MP)
            string displayName = null;

#if FUSION_WEAVER
            var bridge = root.GetComponentInChildren<FusionNetBridge>();
            if (bridge != null)
            {
                try
                {
                    // Chỉ đọc nếu đã spawn, nếu chưa Fusion sẽ ném InvalidOperation
                    string netName = bridge.DisplayName.ToString();
                    if (!string.IsNullOrWhiteSpace(netName))
                        displayName = netName;
                }
                catch (System.InvalidOperationException)
                {
                    // SP / chưa spawn → bỏ qua, dùng fallback khác
                }
            }
#endif

            // Fallback: PlayerProfileManager (SP dùng chung profile này)
            if (string.IsNullOrWhiteSpace(displayName))
            {
                try
                {
                    if (PlayerProfileManager.Data != null &&
                        !string.IsNullOrWhiteSpace(PlayerProfileManager.Data.playerName))
                    {
                        displayName = PlayerProfileManager.Data.playerName;
                    }
                }
                catch { /* nếu chưa có class này thì bỏ qua */ }
            }

            // Fallback cuối: tên object
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = root.name;

            result.playerName = displayName;


            // Points – trên player có PlayerPoints (implement IPointsSyncPort)
            var wallet = root.GetComponentInChildren<PlayerPoints>();
            var port = wallet as IPointsSyncPort;
            result.points = port != null ? port.Current : 0;

            // Kills – PlayerKillStats
            var killStats = root.GetComponentInChildren<PlayerKillStats>();
            result.kills = killStats ? killStats.CurrentKills : 0;

            // Revive / Downed – tạm 0, khi làm hệ thống revive mình sẽ set vào đây
            result.revives = 0;
            result.downs = 0;

            snap.players.Add(result);
        }

        // Sắp xếp theo điểm giảm dần cho đẹp
        snap.players.Sort((a, b) => b.points.CompareTo(a.points));

        Data = snap;

        Debug.Log($"[LastGameResult] Snapshot built. Map={snap.mapName}, " +
                  $"Rounds={snap.roundsSurvived}, Time={MatchClock.FormatMMSS(snap.timeSurvivedSeconds)}, " +
                  $"Players={snap.players.Count}");
    }


    public static void Clear()
    {
        Data = null;
    }

}
