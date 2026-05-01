using System;
using Fusion;
using UnityEngine;

public class RoundStateNet : NetworkBehaviour
{
    public static RoundStateNet Instance { get; private set; }

    public event Action<int> OnRoundChanged;
    public event Action<int> OnRoundEnded;

    [Networked] public int RoundIndex { get; private set; }

    // counter để đảm bảo EndRound luôn fire (kể cả end cùng round nhiều lần)
    [Networked] private int RoundEndCounter { get; set; }
    [Networked] private int LastEndedRound { get; set; }

    // local cache để detect change trong Render()
    private int _lastRoundIndex;
    private int _lastRoundEndCounter;

    public override void Spawned()
    {
        Instance = this;

        _lastRoundIndex = RoundIndex;
        _lastRoundEndCounter = RoundEndCounter;

        // join giữa chừng -> sync UI ngay
        if (RoundIndex > 0)
            OnRoundChanged?.Invoke(RoundIndex);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    public override void Render()
    {
        // Round change
        if (RoundIndex != _lastRoundIndex)
        {
            _lastRoundIndex = RoundIndex;
            OnRoundChanged?.Invoke(RoundIndex);
        }

        // End round pulse
        if (RoundEndCounter != _lastRoundEndCounter)
        {
            _lastRoundEndCounter = RoundEndCounter;
            OnRoundEnded?.Invoke(LastEndedRound);
        }
    }

    // ===== Host API =====
    public void Host_SetRound(int round)
    {
        if (!Object.HasStateAuthority) return;
        RoundIndex = round;
    }

    public void Host_EndRound(int round)
    {
        if (!Object.HasStateAuthority) return;
        LastEndedRound = round;
        RoundEndCounter++;
    }
}
