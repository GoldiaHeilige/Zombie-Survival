// KillsSyncFusion.cs
using UnityEngine;
using Fusion;

/// <summary>Đồng bộ số kill của Player qua Fusion (host tính, client hiển thị).</summary>
public class KillsSyncFusion : NetworkBehaviour
{
    [SerializeField, Tooltip("Kéo PlayerKillStats vào đây.")]
    private PlayerKillStats killStats;

    [Networked] public int NetKills { get; set; }

    int _lastNetKills = int.MinValue;

    public override void Spawned()
    {
        if (killStats == null)
        {
            killStats = GetComponentInParent<PlayerKillStats>();
        }

        if (killStats == null)
        {
            Debug.LogError("[KillsSyncFusion] Không tìm thấy PlayerKillStats", this);
            enabled = false;
            return;
        }

        if (Object.HasStateAuthority)
        {
            // Host: đẩy kill hiện tại lên net + listen local
            NetKills = killStats.CurrentKills;
            _lastNetKills = NetKills;
            killStats.OnKillsChanged += OnHostKillsChanged;
        }
        else
        {
            // Client: init từ net (silent)
            _lastNetKills = NetKills;
            killStats.SetKillsSilent(NetKills);
        }
    }

    void OnHostKillsChanged(int oldValue, int newValue)
    {
        if (!Object || !Object.HasStateAuthority) return;
        if (newValue == NetKills) return;

        NetKills = newValue;
        _lastNetKills = newValue;
    }

    public override void Render()
    {
        if (!Object || Object.HasStateAuthority) return;

        if (NetKills == _lastNetKills) return;
        _lastNetKills = NetKills;

        if (killStats != null)
        {
            killStats.SetKillsFromNet(NetKills);
        }
    }

    void OnDisable()
    {
        if (killStats != null && Object != null && Object.HasStateAuthority)
        {
            killStats.OnKillsChanged -= OnHostKillsChanged;
        }
    }
}
