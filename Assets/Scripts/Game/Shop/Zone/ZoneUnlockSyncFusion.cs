#if FUSION_WEAVER
using Fusion;
using UnityEngine;

/// <summary>
/// Đồng bộ trạng thái mở/đóng của ZoneUnlockablePoints qua Fusion (V2 style).
/// Gắn cùng GameObject với NetworkObject + ZoneUnlockablePoints.
/// </summary>
[DisallowMultipleComponent]
public class ZoneUnlockSyncFusion : NetworkBehaviour
{
    [SerializeField] private ZoneUnlockablePoints target;

    // Chỉ là cờ networked, không gắn OnChanged nữa
    [Networked]
    public bool NetUnlocked { get; set; }

    // Theo dõi trạng thái đã apply local chưa
    private bool _appliedUnlocked;

    void Awake()
    {
        if (!target)
            target = GetComponent<ZoneUnlockablePoints>();
    }

    public override void Spawned()
    {
        if (!target) return;

        if (Object.HasStateAuthority)
        {
            // Host khởi tạo trạng thái
            NetUnlocked = target.IsUnlocked;
            _appliedUnlocked = target.IsUnlocked;
        }
        else
        {
            // Client: nếu snapshot ban đầu đã mở -> apply ngay
            if (NetUnlocked && !target.IsUnlocked)
            {
                target.ForceUnlock();
                _appliedUnlocked = true;
            }
        }
    }

    /// <summary>
    /// Được gọi từ ZoneUnlockablePoints khi host đã mở cửa.
    /// </summary>
    public void SetUnlockedFromHost()
    {
        if (Object != null && Object.HasStateAuthority)
        {
            NetUnlocked = true;
            _appliedUnlocked = true; // host đã apply local trong ZoneUnlockablePoints rồi
        }
    }

    /// <summary>
    /// V2 dùng Render() để apply các thay đổi visual theo snapshot.
    /// </summary>
    public override void Render()
    {
        if (!target)
            return;

        // Client: khi nhận được NetUnlocked = true mà chưa apply local → mở cửa
        if (!_appliedUnlocked && NetUnlocked)
        {
            target.ForceUnlock();
            _appliedUnlocked = true;
        }
    }
}
#endif
