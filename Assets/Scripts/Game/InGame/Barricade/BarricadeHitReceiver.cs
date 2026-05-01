using UnityEngine;
using TT;

#if FUSION_WEAVER
using Fusion;
#endif

/// <summary>
/// Gắn lên collider ở cửa/barricade để nhận hit từ zombie.
/// ZombieMeleeExecutor sẽ tìm component này và gọi OnHitByZombie().
/// </summary>
[DisallowMultipleComponent]
public class BarricadeHitReceiver : MonoBehaviour
{
    [SerializeField] private BarricadeWindow window;
#if FUSION_WEAVER
    [SerializeField] private BarricadeWindowNet windowNet;
#endif

    public BarricadeWindow Window => window;

    private void Awake()
    {
        if (!window)
            window = GetComponentInParent<BarricadeWindow>(true);
#if FUSION_WEAVER
        if (!windowNet)
            windowNet = GetComponentInParent<BarricadeWindowNet>(true);
#endif
    }

    /// <summary>Zombie gọi hàm này khi chém/cắn trúng collider của barricade.</summary>
    /// <summary>Zombie gọi hàm này khi chém/cắn trúng collider của barricade.</summary>
    public void OnHitByZombie()
    {
        if (window == null)
        {
            //UnityEngine.Debug.LogWarning($"[BarricadeHitReceiver] {name} hit but window is NULL", this);
            return;
        }
        /*
            UnityEngine.Debug.Log(
                $"[BarricadeHitReceiver] {name} hit by zombie. Before: CanTakeHit={window.CanTakeZombieHit()}",
                this);*/
#if FUSION_WEAVER
        // Nếu có Net: chỉ StateAuthority mới được phép phá cửa
        if (windowNet != null && windowNet.Object != null && windowNet.Runner != null)
        {
            if (!windowNet.Object.HasStateAuthority)
                return;

            // Host xử lý hit + vỡ ván như cũ
            bool broke = window.ApplyZombieHit(out int brokenIndex);

            // Gửi hiệu ứng rung cho tất cả client (kể cả host)
            windowNet.RPC_OnHit();

            if (broke && brokenIndex >= 0)
            {
                windowNet.RPC_BoardBroken(brokenIndex);
            }
            return;
        }
#endif

        // ───────── SINGLEPLAYER / KHÔNG CÓ FUSION ─────────

        // 1) Tính damage & check có vỡ ván không
        bool brokeSp = window.ApplyZombieHit(out int brokenIndexSp);

        // 2) Rung cửa local
        window.PlayHitShake();

        // 3) Bắn topic HIT cho BarricadeAudioDriver
        var hitEvent = new BarricadeRepairEvent
        {
            player = null,
            window = window.gameObject,
            slotIndex = -1              // hit chung, không care slot cụ thể
        };
        Observer.Instance?.NotifyWithData(BarricadeTopics.Hit, hitEvent);

        // 4) Nếu có ván vỡ → bắn thêm BoardBroken
        if (brokeSp && brokenIndexSp >= 0)
        {
            var breakEvent = new BarricadeRepairEvent
            {
                player = null,
                window = window.gameObject,
                slotIndex = brokenIndexSp
            };
            Observer.Instance?.NotifyWithData(BarricadeTopics.BoardBroken, breakEvent);
        }

        /*    UnityEngine.Debug.Log(
                $"[BarricadeHitReceiver] After: CanTakeHit={window.CanTakeZombieHit()}, topIndex={window.GetTopIntactBoardIndex()}",
                this);*/
    }
}
