// MatchClock.cs
using UnityEngine;

/// <summary>
/// Đồng hồ đơn giản để đếm "Time Survived".
/// Mỗi máy tự đếm bằng Time.time – đủ ổn vì SP và MP đều đã sync state chính.
/// </summary>
public static class MatchClock
{
    static bool _running;
    static float _startTime;
    static float _finalDuration;

    /// <summary>Bắt đầu đếm. Gọi khi RoundDirector bắt đầu vòng loop wave.</summary>
    public static void Begin()
    {
        _running = true;
        _startTime = Time.time;
        _finalDuration = 0f;
        Debug.Log("[MatchClock] BEGIN");
    }

    /// <summary>Dừng đếm và chốt kết quả. Gọi một lần khi GameOver.</summary>
    public static void Stop()
    {
        if (!_running) return;
        _running = false;
        _finalDuration = Mathf.Max(0f, Time.time - _startTime);
        Debug.Log($"[MatchClock] STOP -> {_finalDuration:0.0}s");
    }

    /// <summary>Thời gian chốt cuối cùng (giây).</summary>
    public static float FinalSeconds => _finalDuration;

    /// <summary>Helper format mm:ss (01:23).</summary>
    public static string FormatMMSS(float seconds)
    {
        int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int m = total / 60;
        int s = total % 60;
        return $"{m:00}:{s:00}";
    }
}
