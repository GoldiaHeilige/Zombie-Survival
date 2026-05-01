// PlayerKillStats.cs
using System;
using UnityEngine;
using TT;

/// <summary>Payload gửi qua Observer khi số kill đổi.</summary>
public struct KillsChangedEventData
{
    public GameObject owner;    // player
    public int oldValue;
    public int newValue;
    public int delta;           // +1, +2...
}

[DisallowMultipleComponent]
public class PlayerKillStats : MonoBehaviour
{
    [SerializeField, Tooltip("Kill ban đầu (thường để 0).")]
    private int currentKills;

    public int CurrentKills => currentKills;

    /// <summary>Bắn khi kill đổi (old, new).</summary>
    public event Action<int, int> OnKillsChanged;

    /// <summary>Reset khi spawn mới, respawn, v.v.</summary>
    public void ResetKills(int value = 0)
    {
        SetKillsInternal(value, notify: true);
    }

    /// <summary>Cộng thêm kill.</summary>
    public void AddKill(int amount = 1)
    {
        if (amount <= 0) return;
        SetKillsInternal(currentKills + amount, notify: true);
    }

    /// <summary>Set kills từ net mà không bắn Observer (dùng cho init).</summary>
    public void SetKillsSilent(int value)
    {
        SetKillsInternal(value, notify: false);
    }

    /// <summary>Set kills từ net và có bắn Observer/HUD.</summary>
    public void SetKillsFromNet(int value)
    {
        SetKillsInternal(value, notify: true);
    }

    void SetKillsInternal(int value, bool notify)
    {
        int clamped = Mathf.Max(0, value);
        if (clamped == currentKills)
            return;

        int old = currentKills;
        currentKills = clamped;

        OnKillsChanged?.Invoke(old, currentKills);

        if (!notify)
            return;

        var payload = new KillsChangedEventData
        {
            owner = gameObject,
            oldValue = old,
            newValue = currentKills,
            delta = currentKills - old
        };

        Observer.Instance?.NotifyWithData(KillTopics.Changed, payload);
    }
}
