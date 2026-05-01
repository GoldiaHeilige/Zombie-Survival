using UnityEngine;
using System.Collections.Generic;

#if FUSION_WEAVER
using Fusion;
#endif

public class HUDLocalBinder : MonoBehaviour
{
    [Header("UI References (optional)")]
    [SerializeField] private PlayerHealthUI healthUI;
    [SerializeField] private PlayerStaminaUI staminaUI;
    [SerializeField] private WeaponStatUI weaponStatUI;
    [SerializeField] private PickupUI pickupUI;    
    [SerializeField] private PlayerReviveUI reviveUI;
    [SerializeField] private HealthUIVisibility healthUIVisibility;
    [SerializeField] private HurtOverlayUI hurtOverlayUI;
    [SerializeField] private TT.PerkUI perkUI;

    //  [SerializeField] private PointsUI pointsUI;
    [SerializeField] private KillsUI killsUI;
    [SerializeField] private RoundUI roundUI;
    [SerializeField] private ZoneUnlockUI zoneUnlockUI;
    [SerializeField] private BuyUI buyUI;
    [SerializeField] private DeadOverlayUI deadOverlayUI;
    [SerializeField] private string pickupBindMethodName = "Bind";

    void OnEnable()
    {
        SpawnManager.OnLocalPlayerBound += HandleLocalPlayerBound; 
    }
    void OnDisable() => SpawnManager.OnLocalPlayerBound -= HandleLocalPlayerBound;

    private void HandleLocalPlayerBound(Transform player) => BindAll(player);

    private void BindAll(Transform playerRoot)
    {
        if (!playerRoot) return;

        // HEALTH - chọn theo runner (MP vs SP)
        if (healthUI != null)
        {
            var healthState = PickStateByRunner<IHealthState>(playerRoot);
            if (healthState != null)
            {
                healthUI.Bind(healthState);
            }

            // NEW: bind LifeController cho việc ẩn/hiện CanvasGroup
            var life = playerRoot.GetComponentInChildren<PlayerLifeController>(true);
            if (life != null && healthUIVisibility != null)
                healthUIVisibility.Bind(life);

            if (hurtOverlayUI != null && healthState != null && life != null)
                hurtOverlayUI.Bind(healthState, life);
        }

        if (staminaUI != null)
        {
            var moveState = PickStateByRunner<IMovementState>(playerRoot);
            if (moveState != null)
                staminaUI.Bind(moveState);
        }

        // AMMO - chọn theo runner (MP vs SP)
        if (weaponStatUI != null)
        {
            var loadout = PickStateByRunner<ILoadoutState>(playerRoot);
            if (loadout != null) weaponStatUI.Bind(loadout);
        }

        // PICKUP (optional)
        if (pickupUI != null)
        {
            var pp = playerRoot.GetComponentInChildren<PlayerPickup>(true);
            if (pp != null) pickupUI.SetSource(pp);
        }

        if (reviveUI != null)
        {
            var bridge = playerRoot.GetComponentInChildren<FusionNetBridge>(true);
            reviveUI.Bind(bridge); // thêm 1 hàm Bind trong ReviveUI, chỉ cần gán _bridge
        }

/*        {
            pointsUI.Bind(playerRoot);
        }*/

        if (killsUI != null)
        {
            killsUI.Bind(playerRoot);
        }

        if (roundUI != null)
        {
            roundUI.Bind(playerRoot);
        }

        if (zoneUnlockUI != null)
        {
            zoneUnlockUI.Bind(playerRoot);
        }

        if (buyUI != null)
            buyUI.Bind(playerRoot);

        if (perkUI != null)
            perkUI.Bind(playerRoot);

    }

    /// <summary>
    /// Chọn component implement T theo chế độ: có Runner → ưu tiên lớp kết thúc bằng "MP", ngược lại "SP".
    /// Rồi trong nhóm đó lại ưu tiên cái đang hoạt động (isActiveAndEnabled), sau đó activeInHierarchy, cuối cùng bất kỳ.
    /// Nếu nhóm trống, fallback sang nhóm còn lại. Nếu vẫn trống → null.
    /// </summary>
    private T PickStateByRunner<T>(Transform root) where T : class
    {
        if (root == null) return null;

        bool hasRunner = HasRunningRunner();
    //    Debug.Log($"[PickState] HasRunningRunner: {hasRunner}");

        var comps = root.GetComponentsInChildren<Component>(includeInactive: true);

        // Khi có runner, CỐ ý tìm component MP
        if (hasRunner)
        {
            foreach (var c in comps)
            {
                if (c is T t && c.GetType().Name.Contains("MP"))
                {
                //    Debug.Log($"[PickState] ✅ FORCE SELECTED MP: {c.GetType().Name}");
                    return t;
                }
            }
        }

        // Khi không có runner, CỐ ý tìm component SP  
        else
        {
            foreach (var c in comps)
            {
                if (c is T t && c.GetType().Name.Contains("SP"))
                {
                //    Debug.Log($"[PickState] ✅ FORCE SELECTED SP: {c.GetType().Name}");
                    return t;
                }
            }
        }

        // Fallback: bất kỳ component nào
        foreach (var c in comps)
        {
            if (c is T t)
            {
            //    Debug.Log($"[PickState] ⚠️ SELECTED FALLBACK: {c.GetType().Name}");
                return t;
            }
        }

        return null;
    }

    private bool HasRunningRunner()
    {
#if FUSION_WEAVER
        var runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        bool hasRunner = runner != null && runner.IsRunning;
    //    Debug.Log($"[HasRunningRunner] Runner found: {runner != null}, IsRunning: {runner?.IsRunning}, Result: {hasRunner}");
        return hasRunner;
#else
    Debug.Log($"[HasRunningRunner] No FUSION_WEAVER, returning false");
    return false;
#endif
    }

    private void TryInvokePickupBind(MonoBehaviour ui, Transform playerRoot)
    {
        var m = ui.GetType().GetMethod(
            pickupBindMethodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        if (m != null)
        {
            var ps = m.GetParameters();
            if (ps.Length == 1 && typeof(Transform).IsAssignableFrom(ps[0].ParameterType))
                m.Invoke(ui, new object[] { playerRoot });
        }
    }

    // ... giữ nguyên TryBindExistingLocal() của bạn (HasInputAuthority ở MP, Camera.main ở SP) ...
}
