#if FUSION_WEAVER
using Fusion;
using UnityEngine;
using TT;

[RequireComponent(typeof(NetworkObject))]
public class BarricadeWindowNet : NetworkBehaviour
{
    [SerializeField] private BarricadeWindow window;

    private void Awake()
    {
        if (!window) window = GetComponent<BarricadeWindow>();
    }

    // ─────────── Player repair ───────────

    // Client → Host: yêu cầu repair 1 ván
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestRepair(RpcInfo info = default)
    {
        if (!window) return;

        var source = info.Source;
        if (source == PlayerRef.None) return;

        var bridges = FindObjectsOfType<FusionNetBridge>();
        foreach (var b in bridges)
        {
            if (b.Object != null && b.Object.InputAuthority == source)
            {
                var pickup = b.GetComponentInChildren<PlayerPickup>(true);
                if (pickup != null)
                {
                    pickup.TryInteractBarricade_FromNet(window);
                }
                break;
            }
        }
    }


    // Host → All: mọi máy đều start build đúng slot
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_StartRebuildClient(int slotIndex, RpcInfo info = default)
    {
        if (!window) return;
        window.StartRebuildAtIndex(slotIndex);
    }

    // ─────────── Zombie phá ván ───────────

    /// <summary>
    /// Host gọi khi xác định 1 slot vỡ.  
    /// Client chỉ nhận RPC này để update state local.
    /// </summary>
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_BoardBroken(int slotIndex, RpcInfo info = default)
    {
        if (!window) return;

        window.BreakBoard(slotIndex);

        var evt = new BarricadeRepairEvent
        {
            player = null,
            window = window.gameObject,
            slotIndex = slotIndex
        };

        TT.Observer.Instance?.NotifyWithData(BarricadeTopics.BoardBroken, evt);
    }


    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_OnHit(RpcInfo info = default)
    {
        if (!window) return;

        // Vẫn shake như cũ
        window.PlayHitShake();

        // Bắn topic cho audio driver
        var evt = new BarricadeRepairEvent
        {
            player = null,                // Không cần cho audio
            window = window.gameObject,
            slotIndex = -1                // Hit chung, không quan tâm ván nào
        };

        TT.Observer.Instance?.NotifyWithData(BarricadeTopics.Hit, evt);
    }
}
#endif
