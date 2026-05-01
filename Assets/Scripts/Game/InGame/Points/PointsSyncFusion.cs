// PointsSyncFusion.cs
using UnityEngine;
using Fusion;
using TT;

/// <summary>Đồng bộ point của Player qua Fusion (pattern giống HealthSyncFusion nhưng đơn giản hơn).</summary>
public class PointsSyncFusion : NetworkBehaviour
{
    [SerializeField, Tooltip("Kéo PlayerPoints (hoặc component implement IPointsSyncPort) vào đây.")]
    MonoBehaviour pointsTarget;

    IPointsSyncPort port;

    [Networked] public int NetPoints { get; set; }

    int _lastNetPoints = int.MinValue;

    public override void Spawned()
    {
        port = pointsTarget as IPointsSyncPort;
        if (port == null)
        {
            Debug.LogError("[PointsSyncFusion] pointsTarget không implement IPointsSyncPort", this);
            enabled = false;
            return;
        }

        if (Object.HasStateAuthority)
        {
            // Host: đẩy giá trị hiện tại lên net và nghe event local để sync
            NetPoints = port.Current;
            _lastNetPoints = NetPoints;
            port.OnPointsChanged += OnHostPointsChanged;
        }
        else
        {
            // Client: nhận NetPoints hiện tại từ host, set silent
            _lastNetPoints = NetPoints;
            port.SetCurrentSilent(NetPoints);
        }
    }

    void OnHostPointsChanged(int oldValue, int newValue)
    {
        if (Object == null || !Object.HasStateAuthority) return;
        if (newValue == NetPoints) return;

        NetPoints = newValue;
        _lastNetPoints = newValue;
    }

    public override void Render()
    {
        // Client: khi NetPoints đổi -> apply về local
        if (Object == null || Object.HasStateAuthority) return;

        if (NetPoints == _lastNetPoints) return;

        int oldValue = _lastNetPoints;
        int newValue = NetPoints;
        int delta = newValue - oldValue; // Tính delta cho client

        _lastNetPoints = NetPoints;

        if (port != null)
        {
            // 1. Cập nhật điểm
            port.SetCurrentFromNet(newValue);

            // 2. 🔴 QUAN TRỌNG: Trigger event LOCAL cho UI animation
            var payload = new PointsChangedEventData
            {
                owner = gameObject,
                oldValue = oldValue,
                newValue = newValue,
                delta = delta,
                reason = PointReason.Unknown, // Không biết lý do trên client
                target = null
            };

            // Gửi event local cho UI trên client
            if (delta > 0)
                Observer.Instance?.NotifyWithData(PointsTopics.Gained, payload);
            else if (delta < 0)
                Observer.Instance?.NotifyWithData(PointsTopics.Spent, payload);

            // Luôn gửi Changed
            Observer.Instance?.NotifyWithData(PointsTopics.Changed, payload);
        }
    }

    void OnDisable()
    {
        if (port != null && Object != null && Object.HasStateAuthority)
        {
            port.OnPointsChanged -= OnHostPointsChanged;
        }
    }
}
