using UnityEngine;
using TT;

#if FUSION_WEAVER
using Fusion;
#endif

/// <summary>
/// Interactor cho việc mở cửa / khu bằng Points.
/// SP: gọi trực tiếp.
/// MP:
///   - Host (StateAuthority + InputAuthority): gọi trực tiếp.
///   - Client (chỉ InputAuthority): gửi RPC lên host.
/// </summary>
[DisallowMultipleComponent]
public class ZoneUnlockInteractor : MonoBehaviour
{
    [Header("Raycast")]
    [SerializeField] private Camera viewCamera;
    [SerializeField] private float range = 3f;
    [SerializeField] private LayerMask layerMask = ~0;

    [Header("Refs")]
    [SerializeField] private PlayerPoints playerPoints;
    [SerializeField] private ZoneUnlockUI zoneUnlockUI;

    [Header("Debug")]
    [SerializeField] private bool logRaycast = false;

    private ZoneUnlockablePoints _lookedZone;

#if FUSION_WEAVER
    private FusionNetBridge _netBridge;
#endif

    void Awake()
    {
        if (!viewCamera)
            viewCamera = Camera.main;

        if (!playerPoints)
            playerPoints = GetComponentInChildren<PlayerPoints>();

        if (!zoneUnlockUI)
        {
            var hud = Object.FindFirstObjectByType<HUDLocalBinder>();
            if (hud != null)
                zoneUnlockUI = hud.GetComponentInChildren<ZoneUnlockUI>(true);
        }

#if FUSION_WEAVER
        _netBridge = GetComponentInParent<FusionNetBridge>();
#endif
    }

    void Update()
    {
        if (!HasLocalControl())
            return;

        UpdateLookedZone();
        HandleInteractInput();
    }

    bool HasLocalControl()
    {
#if FUSION_WEAVER
        if (_netBridge != null && _netBridge.Object != null)
        {
            // Chỉ bản local (InputAuthority) mới đọc input
            return _netBridge.Object.HasInputAuthority;
        }
#endif
        // SP
        return true;
    }

    void UpdateLookedZone()
    {
        var newZone = RaycastZoneInFront();

        if (newZone != _lookedZone)
        {
            _lookedZone = newZone;

            if (zoneUnlockUI != null)
            {
                if (_lookedZone != null && !_lookedZone.IsUnlocked)
                    zoneUnlockUI.ShowFor(_lookedZone);
                else
                    zoneUnlockUI.Hide();
            }
        }
        else
        {
            if (_lookedZone != null && _lookedZone.IsUnlocked && zoneUnlockUI != null)
            {
                zoneUnlockUI.Hide();
                _lookedZone = null;
            }
        }
    }

    ZoneUnlockablePoints RaycastZoneInFront()
    {
        if (!viewCamera)
        {
            viewCamera = Camera.main;
            if (!viewCamera) return null;
        }

        Ray ray = new Ray(viewCamera.transform.position, viewCamera.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, range, layerMask, QueryTriggerInteraction.Ignore))
        {
            if (logRaycast)
                Debug.Log($"[ZoneUnlockInteractor] hit {hit.collider.name}", hit.collider);

            return hit.collider.GetComponentInParent<ZoneUnlockablePoints>();
        }

        return null;
    }

    void HandleInteractInput()
    {
        if (_lookedZone == null || _lookedZone.IsUnlocked)
            return;

        // 🔴 Thay vì InputHub, dùng FusionInputProvider – đây là nguồn input mà Fusion đang dùng
        bool interactDown = false;

        var fusionInput = FusionInputProvider.Instance;
        if (fusionInput != null)
        {
            interactDown = fusionInput.InteractDown;
        }
        else if (InputHub.Instance != null)
        {
            // fallback nếu vì lý do gì đó không có FusionInputProvider
            interactDown = InputHub.Instance.Current.InteractDown;
        }

        if (!interactDown)
            return;

#if FUSION_WEAVER
        if (_netBridge != null && _netBridge.Object != null)
        {
            // Host: có StateAuthority -> mở trực tiếp (và ZoneUnlockablePoints sẽ ping sync)
            if (_netBridge.Object.HasStateAuthority)
            {
                var no = _lookedZone.GetComponentInParent<NetworkObject>();
                if (no != null)
                {
                    _netBridge.RPC_RequestUnlockZone(no);
                }
            }

            else if (_netBridge.Object.HasInputAuthority)
            {
                // Client: gửi RPC yêu cầu host mở
                var no = _lookedZone.GetComponentInParent<NetworkObject>();
                if (no != null)
                {
                    _netBridge.RPC_RequestUnlockZone(no);
                }
            }

            return;
        }
#endif
        // SP (hoặc không có Fusion)
        TryUnlockLocal();
    }

    void TryUnlockLocal()
    {
        if (!playerPoints)
            playerPoints = GetComponentInChildren<PlayerPoints>();
        if (!playerPoints)
            return;

        if (_lookedZone == null)
            return;

        bool success = _lookedZone.TryUnlock(playerPoints);

        if (success)
        {
            // SP / offline: chỉ mình thấy
            EventFeed.Push($"You unlocked {_lookedZone.displayName}", EventFeedType.Action);
            var pickup = GetComponentInChildren<PlayerPickup>(true);
            if (pickup != null) pickup.PlayLocalBuySfx_Authoritative();
        }

        if (logRaycast)
        {
            Debug.Log(
                $"[ZoneUnlockInteractor] TryUnlock zone={_lookedZone.displayName} cost={_lookedZone.Cost} => {success}",
                this
            );
        }
    }
}
