// PlayerPoints.cs
using System;
using UnityEngine;
using TT;

/// <summary>Lý do thay đổi điểm — để debug / logic sau này.</summary>
public enum PointReason
{
    Unknown = 0,
    ZombieHit = 1,
    ZombieKill = 2,
    Purchase = 3,
    Refund = 4,
    BarricadeRepair = 5,
    PowerUp = 6
}

/// <summary>Payload gửi qua Observer khi điểm đổi.</summary>
public struct PointsChangedEventData
{
    public GameObject owner;    // player nào
    public int oldValue;
    public int newValue;
    public int delta;
    public PointReason reason;
    public GameObject target;   // nạn nhân / shop / cửa… liên quan
}

[DisallowMultipleComponent]
public class PlayerPoints : MonoBehaviour, IPointsSyncPort
{
    [Header("Points Settings")]
    [Tooltip("Điểm khởi điểm khi spawn (giống COD).")]
    public int startPoints = 500;

    [Tooltip("Giới hạn max để tránh overflow / số quá to.")]
    public int maxPoints = 999999;

    [SerializeField, Tooltip("Điểm hiện tại (debug).")]
    private int currentPoints;

    bool _initialized;

    // IPointsSyncPort
    public int Current => currentPoints;
    public event Action<int, int> OnPointsChanged;

    void Awake()
    {
        EnsureInitialized();
    }

    void OnEnable()
    {
        EnsureInitialized();
    }

    void EnsureInitialized()
    {
        if (_initialized) return;
        currentPoints = Mathf.Clamp(startPoints, 0, maxPoints);
        _initialized = true;
    }

    /// <summary>Cộng/trừ point. amount âm = bị trừ.</summary>
    public void Add(int amount, PointReason reason = PointReason.Unknown, GameObject target = null)
    {
        if (amount == 0) return;

        // Double Points (WaW/BO style): only multiply positive gains, never multiply spending.
        if (amount > 0 &&
            PowerUpManager.PointsMultiplier > 1f &&
            reason != PointReason.PowerUp)
        {
            amount = Mathf.RoundToInt(amount * PowerUpManager.PointsMultiplier);
        }

        int old = currentPoints;
        long raw = (long)currentPoints + amount;
        int clamped = (int)Mathf.Clamp(raw, 0, maxPoints);

        if (clamped == old) return;

        currentPoints = clamped;
        OnPointsChanged?.Invoke(old, currentPoints);

        var payload = new PointsChangedEventData
        {
            owner = gameObject,
            oldValue = old,
            newValue = currentPoints,
            delta = currentPoints - old,
            reason = reason,
            target = target
        };

        // HUD nghe Changed là đủ
        Observer.Instance?.NotifyWithData(PointsTopics.Changed, payload);

        if (amount > 0)
            Observer.Instance?.NotifyWithData(PointsTopics.Gained, payload);
        else
            Observer.Instance?.NotifyWithData(PointsTopics.Spent, payload);
    }

    /// <summary>Kiểm tra đủ tiền để mua không.</summary>
    public bool CanAfford(int cost)
    {
        return cost >= 0 && currentPoints >= cost;
    }

    /// <summary>Thử trừ điểm (mua đồ, mở cửa…). Trả về true nếu thành công.</summary>
    public bool TrySpend(int cost, PointReason reason = PointReason.Purchase, GameObject target = null)
    {
        if (cost < 0) return false;
        if (!CanAfford(cost)) return false;

        Add(-cost, reason, target);
        return true;
    }

    // === IPointsSyncPort implement ===

    public void SetCurrentSilent(int value)
    {
        int clamped = Mathf.Clamp(value, 0, maxPoints);
        currentPoints = clamped;
        // Không bắn event ở đây — dùng khi init từ net.
    }

    public void SetCurrentFromNet(int value)
    {
        int old = currentPoints;
        int clamped = Mathf.Clamp(value, 0, maxPoints);
        if (old == clamped) return;

        currentPoints = clamped;
        OnPointsChanged?.Invoke(old, currentPoints);

        var payload = new PointsChangedEventData
        {
            owner = gameObject,
            oldValue = old,
            newValue = currentPoints,
            delta = currentPoints - old,
            reason = PointReason.Unknown,
            target = null
        };

        Observer.Instance?.NotifyWithData(PointsTopics.Changed, payload);
    }
}
