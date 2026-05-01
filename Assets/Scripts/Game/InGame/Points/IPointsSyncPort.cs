// IPointsSyncPort.cs
using System;

/// <summary>Cổng sync cực mỏng cho PointsSyncFusion dùng (y hệt IHealthSyncPort style).</summary>
public interface IPointsSyncPort
{
    /// <summary>Điểm hiện tại.</summary>
    int Current { get; }

    /// <summary>Bắn khi điểm đổi (before, after) — host dùng để đẩy NetPoints.</summary>
    event Action<int, int> OnPointsChanged;

    /// <summary>Client set local mà không bắn Observer (dùng cho init).</summary>
    void SetCurrentSilent(int value);

    /// <summary>Client set từ NetPoints, có bắn event/UI.</summary>
    void SetCurrentFromNet(int value);
}
