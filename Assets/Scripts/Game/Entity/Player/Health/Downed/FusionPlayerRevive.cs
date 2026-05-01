#if FUSION_WEAVER
using Fusion;
using UnityEngine;
using System;

/// <summary>
/// State-authority giữ lock revive cho 1 player bị Downed.
/// - Bắt đầu bằng RPC từ rescuer
/// - Host duy trì điều kiện (held, range, LOS nếu muốn)
/// - Hết thời gian → SignalRevived()
/// </summary>
[DisallowMultipleComponent]
public class FusionPlayerRevive : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private float reviveDuration = 3.0f;     // có thể chỉnh trong Inspector
    [SerializeField] private float reviveRange = 2.2f;
    [SerializeField] private bool requireLineOfSight = false;
    [SerializeField] private int reviveHpOnComplete = 30;     // 0 = không đụng vào HP

    [Header("Refs (optional)")]
    [SerializeField] private PlayerLifeController life;        // tự tìm nếu null
    [SerializeField] private Transform reviveAnchor;           // điểm để đo khoảng cách (mặc định = transform)
    public Transform ReviveAnchor => reviveAnchor ? reviveAnchor : transform;

    [Networked] public NetworkObject Rescuer { get; set; }
    [Networked] public NetworkBool Active { get; set; }
    [Networked] public TickTimer ReviveTimer { get; set; }
    [Networked] public float ActiveReviveDuration { get; set; }

    public float ReviveDuration => reviveDuration;


    public bool IsBeingRevived
    {
        get
        {
            // Không bao giờ đọc Networked property khi behaviour chưa Spawned / đã Despawn
            if (Object == null || !Object.IsValid || Runner == null || !Runner.IsRunning)
                return false;

            return Active;
        }
    }


    void Awake()
    {
        if (!life) life = GetComponent<PlayerLifeController>();
        if (!reviveAnchor) reviveAnchor = transform;
    }

    // NEW: cho phép PlayerAppearance gán lại anchor theo skin
    public void SetReviveAnchor(Transform anchor)
    {
        reviveAnchor = anchor;
    }

    public override void Spawned()
    {
        // nothing
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestRevive(NetworkObject rescuerNO, RpcInfo info = default)
    {
        if (!Object || !Object.HasStateAuthority) return;
        if (!life) return;

        // chỉ cho revive khi đang Downed
        if (life.state != LifeState.Downed) return;

        // nếu đang có người khác revive thì bỏ
        if (Active)
        {
            // đang được thằng khác revive
            if (Rescuer && Rescuer != rescuerNO)
                return;

            // chính thằng này đã revive rồi → đừng reset timer nữa
            if (Rescuer == rescuerNO)
                return;
        }

        // validate rescuer
        if (!rescuerNO) return;
        var rescuerBridge = rescuerNO.GetComponent<FusionNetBridge>();
        if (!rescuerBridge) return;

        // khoảng cách
        var rescuerPos = rescuerBridge.transform.position;
        if (Vector3.Distance(rescuerPos, reviveAnchor.position) > reviveRange) return;

        // LOS (optional) ...
        // phải đang GIỮ phím (held) ở tick server

        // set lock (lúc này chắc chắn chưa Active với rescuer này)
        Rescuer = rescuerNO;
        Active = true;

        float dur = reviveDuration;

        // perk multiplier từ rescuer (MP-only)
        float mult = 1f;
        var rescuerPerk = rescuerNO.GetComponentInChildren<TT.PerkManager>(true)
                      ?? rescuerNO.GetComponentInParent<TT.PerkManager>(true);
        if (rescuerPerk != null)
            mult = rescuerPerk.GetReviveDurationMultiplier();

        dur = Mathf.Max(0.05f, reviveDuration * mult);

        ActiveReviveDuration = dur;
        ReviveTimer = TickTimer.CreateFromSeconds(Runner, dur);

        TT.Observer.Instance?.NotifyWithData("revive.started", (gameObject, rescuerNO.gameObject, dur));
        Debug.Log($"[Revive] START by {rescuerNO.name} → target {name}, base={reviveDuration:0.00}s mult={mult:0.00} dur={dur:0.00}s");

    }


    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_CancelRevive(NetworkObject rescuerNO, RpcInfo info = default)
    {
        if (!Object || !Object.HasStateAuthority) return;
        if (!Active) return;

        // chỉ đúng thằng đang revive mới được hủy
        if (!Rescuer || Rescuer != rescuerNO)
            return;

        CancelRevive("rescuer lost target / cancel RPC");
    }


    public override void FixedUpdateNetwork()
    {
        if (!Object || !Object.HasStateAuthority) return;
        if (!Active) return;

        // target vẫn còn downed?
        if (!life || life.state != LifeState.Downed)
        {
            CancelRevive("target state changed");
            return;
        }

        // rescuer còn hợp lệ?
        var rescuerOK = Rescuer != null;
        var rescuerBridge = rescuerOK ? Rescuer.GetComponent<FusionNetBridge>() : null;
        if (!rescuerOK || !rescuerBridge)
        {
            CancelRevive("rescuer missing");
            return;
        }

        // rescuer phải tiếp tục giữ
        if (!rescuerBridge.LastInput.reviveHeld)
        {
            CancelRevive("rescuer released");
            return;
        }

        // còn trong tầm?
        if (Vector3.Distance(rescuerBridge.transform.position, reviveAnchor.position) > reviveRange)
        {
            CancelRevive("out of range");
            return;
        }

        // hoàn tất?
        if (ReviveTimer.Expired(Runner))
        {
            CompleteRevive(rescuerBridge.gameObject);
        }
    }

    void CompleteRevive(GameObject rescuerGO)
    {
        Active = false;
        var targetGO = this.gameObject;

        // Đứng dậy
        if (life) life.SignalRevived();

        // Optional: set HP sau khi đứng dậy
        if (reviveHpOnComplete > 0)
        {
            // Dùng thẳng DamageableHealth của bạn
            var health = targetGO.GetComponentInChildren<DamageableHealth>();
            if (health != null)
            {
                // Host đặt số rồi HealthSyncFusion sẽ đẩy NetHP
                health.SetCurrentFromNet(reviveHpOnComplete);
            }
        }

        TT.Observer.Instance?.NotifyWithData("revive.completed", (targetGO, rescuerGO));
        Debug.Log($"[Revive] COMPLETE for {targetGO.name} by {rescuerGO.name}");

        // 🔴 NEW: RPC thông báo cho tất cả player (host + mọi client)
        NetworkObject rescuerNO = Rescuer; // Networked field
        if (rescuerNO == null && rescuerGO != null)
            rescuerNO = rescuerGO.GetComponent<NetworkObject>();

        RPC_AnnounceRevive(rescuerNO);

        Rescuer = null;
        ReviveTimer = TickTimer.None;
        ActiveReviveDuration = 0f;
    }

    void CancelRevive(string reason)
    {
        Debug.Log($"[Revive] CANCEL ({reason}) on {name}");
        Active = false;
        TT.Observer.Instance?.NotifyWithData("revive.canceled", (gameObject, Rescuer ? Rescuer.gameObject : null, reason));
        Rescuer = null;
        ActiveReviveDuration = 0f;
        ReviveTimer = TickTimer.None;
    }

    // RPC chạy trên TẤT CẢ máy → gọi EventFeed.Push local
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_AnnounceRevive(NetworkObject rescuerNO, RpcInfo info = default)
    {
        string targetName = GetPlayerName(gameObject);
        string rescuerName = GetPlayerName(rescuerNO ? rescuerNO.gameObject : null);

        // Nếu vì lý do nào đó không có tên, fallback ra "Unknown"
        if (string.IsNullOrWhiteSpace(targetName)) targetName = "Unknown";
        if (string.IsNullOrWhiteSpace(rescuerName)) rescuerName = "Unknown";

        EventFeed.Push($"{rescuerName} revived {targetName}", EventFeedType.Success);
    }

    private static string GetPlayerName(GameObject go)
    {
        if (!go) return null;

        // Ưu tiên lấy DisplayName từ FusionNetBridge
        var bridge = go.GetComponentInChildren<FusionNetBridge>();
        if (bridge != null)
        {
            // Tuyệt đối không đọc Networked khi chưa Spawned / đã Despawn
            if (bridge.Object != null && bridge.Object.IsValid && bridge.Runner != null && bridge.Runner.IsRunning)
            {
                try
                {
                    var s = bridge.DisplayName.ToString();
                    if (!string.IsNullOrWhiteSpace(s))
                        return s;
                }
                catch (System.InvalidOperationException) { }
            }

            // fallback an toàn (không đụng Networked)
            return bridge.gameObject != null ? bridge.gameObject.name : "Player";
        }


        // Fallback: tên GameObject trong Hierarchy
        return go.name;
    }

}
#endif
