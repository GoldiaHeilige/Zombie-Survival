#if FUSION_WEAVER
using Fusion;
using TMPro;
using UnityEngine;
using System;
using UnityEngine.UI;

/// <summary>
/// Local-only: raycast tìm player Downed, hiển thị prompt,
/// và gửi RPC_RequestRevive vào target khi người chơi giữ phím Revive.
/// </summary>
public class PlayerReviveUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Camera cam;
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private float maxDistance = 3.0f;
    [SerializeField] private TMP_Text promptText;          // 1 dòng TMP
    [SerializeField] private CanvasGroup promptGroup;

    [Header("Revive Progress UI (optional)")]
    [SerializeField] private CanvasGroup progressGroup;
    [SerializeField] private Image progressFill;          // Image type = Filled, fillAmount 0..1
    [SerializeField] private string reviveKeyLabel = "F"; // để đúng style [F]

    [Header("Debug")]
    [SerializeField] private bool logRaycast = false;

    private FusionNetBridge _bridge;
    private PlayerLifeController _localLife;
    FusionPlayerRevive _activeReviveTarget;

    float _nextRequestTime;
    const float REQUEST_INTERVAL = 0.15f;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        _bridge = GetComponentInParent<FusionNetBridge>();

        if (!_localLife)
            _localLife = GetComponentInParent<PlayerLifeController>();
    }

    void Update()
    {
        // Chỉ local owner mới có UI revive
        if (_bridge == null || !_bridge.IsLocalOwner)
        {
            Hide();
            return;
        }

        if (!cam)
        {
            Debug.Log("[ReviveUI] No camera");
            Hide();
            return;
        }

        if (_localLife == null)
            _localLife = GetComponentInParent<PlayerLifeController>();

        // ====== CASE: MÌNH ĐANG DOWNED / DEAD → chỉ hiển thị "being revived" ======
        if (_localLife != null && _localLife.state != LifeState.Alive)
        {
            HandleDownedUI();
            return;
        }

        // ====== Từ đây trở xuống: mình đang Alive (có thể là rescuer) ======
        var input = _bridge.LastInput;
        bool reviveHeld = input.reviveHeld;

        // Nếu không còn giữ phím mà vẫn có target đang revive → hủy
        if (!reviveHeld && _activeReviveTarget != null)
        {
            CancelActiveRevive("button released");
        }

        FusionPlayerRevive target;
        PlayerLifeController targetLife;

        if (!FindBestTarget(out target, out targetLife))
        {
            // Mất raycast trong khi vẫn giữ phím → hủy luôn
            if (reviveHeld && _activeReviveTarget != null)
            {
                CancelActiveRevive("lost LOS / no target");
            }

            Hide();
            return;
        }

        // Chỉ xử lý khi target đang Downed
        if (targetLife.state != LifeState.Downed)
        {
            Hide();
            return;
        }

        // Nếu đang revive 1 thằng khác mà bây giờ aim sang thằng mới
        if (reviveHeld && _activeReviveTarget != null && _activeReviveTarget != target)
        {
            CancelActiveRevive("switched target");
        }

        // Nếu đã có người khác revive rồi → mình không được hiển thị prompt
        if (target.IsBeingRevived && target.Rescuer && target.Rescuer != _bridge.Object)
        {
            Hide();
            return;
        }

        // Nếu CHÍNH MÌNH đang là người revive (theo state từ host)
        if (target.IsBeingRevived && target.Rescuer == _bridge.Object)
        {
            float remain = 0f;
            if (target.ReviveTimer.IsRunning)
            {
                var t = target.ReviveTimer.RemainingTime(_bridge.Runner);
                if (t.HasValue) remain = t.Value;
            }

            string targetName = GetPlayerNameFromLife(targetLife);
            string nameFmt = $"\"{Bold(targetName)}\"";
            Show($"Reviving {nameFmt}");
            SetProgressVisible(true);

            float dur = target.ActiveReviveDuration > 0.01f ? target.ActiveReviveDuration
                     : (target.ReviveDuration > 0.01f ? target.ReviveDuration : 3f);

            float progress01 = 1f - (remain / dur);


            SetProgress01(progress01);


            // đảm bảo _activeReviveTarget sync đúng
            _activeReviveTarget = target;
            return;
        }

        // Chưa ai revive → hiển thị "Revive XXX"
        {
            string targetName = GetPlayerNameFromLife(targetLife);
            string key = Bold(reviveKeyLabel);
            string nameFmt = $"\"{Bold(targetName)}\"";

            Show($"Hold [{key}] to revive {nameFmt}");
            SetProgressVisible(false);
            SetProgress01(0f);

        }

        // BẮT ĐẦU revive mới: chỉ gọi khi đang giữ phím và chưa có target active
        if (reviveHeld)
        {
            // Nếu chưa Active (host chưa lock) thì cứ ping lại mỗi 0.15s
            if (_activeReviveTarget == null || (_activeReviveTarget == target && !target.IsBeingRevived))
            {
                if (Time.time >= _nextRequestTime)
                {
                    target.RPC_RequestRevive(_bridge.Object);
                    _activeReviveTarget = target;
                    _nextRequestTime = Time.time + REQUEST_INTERVAL;
                }
            }
        }

    }

    void HandleDownedUI()
    {
        if (_localLife == null)
        {
            Hide();
            return;
        }

        // Chỉ quan tâm khi mình đang Downed
        if (_localLife.state != LifeState.Downed)
        {
            Hide();
            return;
        }

        var myRevive = _localLife.GetComponentInParent<FusionPlayerRevive>();
        if (myRevive == null)
        {
            Hide();
            return;
        }

        // Phải đang được người khác revive
        if (!myRevive.IsBeingRevived || !myRevive.Rescuer || myRevive.Rescuer == _bridge.Object)
        {
            Hide();
            return;
        }

        float remain = 0f;
        if (myRevive.ReviveTimer.IsRunning)
        {
            var t = myRevive.ReviveTimer.RemainingTime(_bridge.Runner);
            if (t.HasValue) remain = t.Value;
        }

        string rescuerName = GetPlayerNameFromNetworkObject(myRevive.Rescuer);
        ShowBeingRevived(rescuerName, remain);
    }

    bool FindBestTarget(out FusionPlayerRevive target, out PlayerLifeController targetLife)
    {
        target = null;
        targetLife = null;

        var camTr = cam.transform;
        Vector3 camPos = camTr.position;
        Vector3 camFwd = camTr.forward;

        // Tìm tất cả collider player trong bán kính maxDistance
        var hits = Physics.OverlapSphere(camPos, maxDistance, playerMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

        float bestScore = float.NegativeInfinity;

        foreach (var col in hits)
        {
            if (!col) continue;

            var revive = col.GetComponentInParent<FusionPlayerRevive>();
            var life = col.GetComponentInParent<PlayerLifeController>();
            if (!revive || !life)
                continue;

            Transform anchor = revive.ReviveAnchor;
            if (!anchor) anchor = revive.transform;

            Vector3 toAnchor = anchor.position - camPos;
            float dist = toAnchor.magnitude;
            if (dist > maxDistance || dist < 0.001f)
                continue;

            toAnchor /= dist;
            float dot = Vector3.Dot(camFwd, toAnchor);

            // chỉ lấy mục tiêu nằm trong hình nón phía trước
            const float minDot = 0.9f; // ~25 độ
            if (dot < minDot)
                continue;

            // Score: ưu tiên thẳng hướng hơn và gần hơn
            float score = dot - dist * 0.01f;
            if (score > bestScore)
            {
                bestScore = score;
                target = revive;
                targetLife = life;
            }
        }

        if (logRaycast && target != null)
        {
            Debug.DrawLine(camPos, target.ReviveAnchor.position, Color.green, 0.05f);
        }

        return target != null;
    }


    public void Bind(FusionNetBridge bridge)
    {
        _bridge = bridge;
    }

    // ===== UI helper =====

    void Show(string text)
    {
        if (promptText) promptText.text = text;

        if (promptGroup)
        {
            promptGroup.alpha = 1f;
            promptGroup.interactable = false;
            promptGroup.blocksRaycasts = false;
        }
    }

    public void ShowBeingRevived(string rescuerName, float secondsLeft)
    {
        if (string.IsNullOrWhiteSpace(rescuerName))
            rescuerName = "Someone";

        if (promptText)
            promptText.text = $"{rescuerName} is reviving you... {Mathf.CeilToInt(secondsLeft)}s";

        if (promptGroup)
        {
            promptGroup.alpha = 1f;
            promptGroup.interactable = false;
            promptGroup.blocksRaycasts = false;
        }
    }

    void Hide()
    {
        if (promptGroup) promptGroup.alpha = 0f;
        SetProgressVisible(false);
    }


    // ===== Name helpers =====

    private static string GetPlayerNameFromLife(PlayerLifeController life)
    {
        if (!life) return "Unknown";

        var bridge = life.GetComponentInChildren<FusionNetBridge>(true)
                 ?? life.GetComponentInParent<FusionNetBridge>(true);

        if (bridge != null)
        {
            // chỉ đọc Networked khi behaviour còn Spawned/Valid
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

            // fallback an toàn
            return bridge.gameObject != null ? bridge.gameObject.name : "Player";
        }

        return life.gameObject != null ? life.gameObject.name : "Player";
    }


    private static string GetPlayerNameFromNetworkObject(NetworkObject no)
    {
        if (!no) return "Unknown";

        var bridge = no.GetComponentInChildren<FusionNetBridge>(true)
                 ?? no.GetComponent<FusionNetBridge>();

        if (bridge != null)
        {
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

            return bridge.gameObject != null ? bridge.gameObject.name : "Player";
        }

        return no.gameObject != null ? no.gameObject.name : "Player";
    }


    void CancelActiveRevive(string reason = null)
    {
        if (_activeReviveTarget != null && _bridge != null && _bridge.Object != null)
        {
            _activeReviveTarget.RPC_CancelRevive(_bridge.Object);
            if (!string.IsNullOrEmpty(reason))
                Debug.Log($"[ReviveUI] Cancel local revive: {reason}");
        }

        _activeReviveTarget = null;
    }

    private static string Bold(string s) => $"<b>{s}</b>";

    void SetProgressVisible(bool visible)
    {
        if (!progressGroup) return;
        progressGroup.alpha = visible ? 1f : 0f;
        progressGroup.interactable = false;
        progressGroup.blocksRaycasts = false;
    }

    void SetProgress01(float t01)
    {
        if (!progressFill) return;
        progressFill.fillAmount = Mathf.Clamp01(t01);
    }

}
#endif
