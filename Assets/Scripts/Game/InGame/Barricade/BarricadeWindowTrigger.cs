using UnityEngine;

[RequireComponent(typeof(Collider))]
public class BarricadeWindowTrigger : MonoBehaviour
{
    [SerializeField] private BarricadeWindow window;

    // Local player có đang đứng trong trigger này không?
    bool _localPlayerInside;

    private void Awake()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;

        if (!window)
            window = GetComponentInParent<BarricadeWindow>();
    }

    private void OnEnable()
    {
        if (window != null)
            window.OnWindowStateChanged += HandleWindowStateChanged;
    }

    private void OnDisable()
    {
        if (window != null)
            window.OnWindowStateChanged -= HandleWindowStateChanged;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsLocalPlayer(other)) return;
        if (!window) return;

        _localPlayerInside = true;

        if (BarricadeRepairUI.Instance != null)
        {
            // Chỉ hiện nếu còn slot trống
            if (window.HasEmptySlot())
                BarricadeRepairUI.Instance.ShowFor(window);
            else
                BarricadeRepairUI.Instance.HideFor(window);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsLocalPlayer(other)) return;
        if (!window) return;

        _localPlayerInside = false;

        if (BarricadeRepairUI.Instance != null)
        {
            BarricadeRepairUI.Instance.HideFor(window);
        }
    }

    // Gọi khi BarricadeWindow báo state changed (build xong, ván bị phá...)
    void HandleWindowStateChanged(BarricadeWindow changedWindow)
    {
        if (!window || changedWindow != window) return;
        if (!_localPlayerInside) return; // player không đứng trong trigger → khỏi đụng UI

        if (BarricadeRepairUI.Instance == null) return;

        if (window.HasEmptySlot())
        {
            BarricadeRepairUI.Instance.ShowFor(window);
        }
        else
        {
            BarricadeRepairUI.Instance.HideFor(window);
        }
    }

    private bool IsLocalPlayer(Collider other)
    {
#if FUSION_WEAVER
        // MP: chỉ player có InputAuthority trên MÁY NÀY mới được coi là local
        var net = other.GetComponentInParent<FusionNetBridge>();
        if (net != null && net.Object != null)
        {
            return net.Object.HasInputAuthority;
        }
        // Nếu không có FusionNetBridge (ví dụ SP scene) → fall back tag
#endif
        // SP fallback
        return other.CompareTag("Player");
    }
}
