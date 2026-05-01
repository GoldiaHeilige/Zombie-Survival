using UnityEngine;
using TT;
using UnityEngine.AI;


/// <summary>
/// Dùng points để unlock 1 khu vực / cửa / thùng block đường kiểu COD Zombie.
/// Gắn script này lên object đại diện cho cửa / thùng đó.
/// </summary>
public class ZoneUnlockablePoints : MonoBehaviour
{
    [Header("Basic")]
    [Tooltip("Tên hiển thị (ví dụ: 'MỞ LỐI ĐI', 'MỞ PHÒNG MỚI').")]
    public string displayName = "UNLOCK AREA";

    [Tooltip("Giá mở cửa (số points cần).")]
    public int cost = 750;

    [Tooltip("Đã mở chưa (debug).")]
    [SerializeField] private bool unlocked;

    [Header("Unlock Visual (Anim/VFX/Audio)")]
    [SerializeField] private ZoneUnlockVisual visual; 

    [Header("Objects sẽ disable / huỷ khi mở")]
    [Tooltip("Nếu để trống, sẽ disable chính GameObject này.")]
    public GameObject[] objectsToDisable;

    [Tooltip("Nếu true thì Destroy() object, nếu false thì chỉ SetActive(false).")]
    public bool destroyInsteadOfDisable = false;

    [Header("NavMesh (tắt obstacle khi unlock để mở đường lại)")]
    [Tooltip("Các NavMeshObstacle cần tắt sau khi unlock (thường là obstacle carving).")]
    [SerializeField] private NavMeshObstacle[] navMeshObstaclesToDisable;

    [SerializeField] private bool alsoDisableCarving = true;


    [Header("Debug")]
    public bool logPurchase = true;

    /// <summary>Zone này đã được mở chưa.</summary>
    public bool IsUnlocked => unlocked;

    /// <summary>Giá cần để mở.</summary>
    public int Cost => cost;

    /// <summary>
    /// Kiểm tra player này có đủ points để mở không.
    /// Không trừ điểm, chỉ check.
    /// </summary>
    public bool CanAfford(PlayerPoints wallet)
    {
        if (wallet == null) return false;
        if (unlocked) return false;
        if (cost <= 0) return true; // free
        return wallet.CanAfford(cost);
    }

    /// <summary>
    /// Thử mở bằng ví player (có trừ điểm).
    /// Trả về true nếu mua thành công.
    /// </summary>
    public bool TryUnlock(PlayerPoints wallet)
    {
        if (unlocked) return false;
        if (wallet == null) return false;

        // Cố gắng trừ điểm
        if (!wallet.TrySpend(cost, PointReason.Purchase, gameObject))
        {
            // Không đủ tiền
            if (logPurchase)
            {
                Debug.Log($"[ZoneUnlockable] {displayName} - NOT ENOUGH POINTS. Cost={cost}, Wallet={wallet.Current}");
            }
            return false;
        }

        // Mua thành công → mở cửa
        DoUnlock(wallet.gameObject);
        return true;
    }

    /// <summary>
    /// Force mở (không trừ điểm) – dùng cho debug / script khác.
    /// </summary>
    public void ForceUnlock(GameObject opener = null)
    {
        if (unlocked) return;
        DoUnlock(opener);
    }

    void DoUnlock(GameObject opener)
    {
        unlocked = true;

        // NEW: play visual (anim/audio/vfx) rồi visual sẽ tự disable/destroy đúng lúc
        if (visual != null)
        {
            visual.PlayUnlock(opener);
        }
        else
        {
            // Fallback: giữ y như cũ nếu bạn chưa gắn visual
            if (objectsToDisable == null || objectsToDisable.Length == 0)
                HandleDisableOrDestroy(gameObject);
            else
                foreach (var go in objectsToDisable)
                    if (go) HandleDisableOrDestroy(go);
        }

        DisableNavMeshObstaclesAfterUnlock();

        if (logPurchase)
        {
            string who = opener ? opener.name : "Unknown";
            Debug.Log($"[ZoneUnlockable] {displayName} UNLOCKED by {who}, cost={cost}");
        }

        // Notify ra Observer để HUD / audio / script khác nghe nếu cần
        TT.Observer.Instance?.NotifyWithData("zone.unlocked",
            (zoneGO: (GameObject)this.gameObject, openerGO: opener, price: cost));

#if FUSION_WEAVER
        // 🔴 QUAN TRỌNG: báo cho sync Fusion biết host đã mở
        var sync = GetComponent<ZoneUnlockSyncFusion>();
        if (sync != null && sync.Object != null && sync.Object.HasStateAuthority)
        {
            sync.SetUnlockedFromHost();
        }
#endif
    }


    void HandleDisableOrDestroy(GameObject go)
    {
        if (!go) return;

        if (destroyInsteadOfDisable)
        {
            Destroy(go);
        }
        else
        {
            go.SetActive(false);
        }
    }

    void DisableNavMeshObstaclesAfterUnlock()
    {
        // 1) Nếu bạn không set tay trong inspector, thử auto-pull từ objectsToDisable và bản thân object
        if (navMeshObstaclesToDisable == null || navMeshObstaclesToDisable.Length == 0)
        {
            // ưu tiên obstacle trên chính object zone
            var selfObs = GetComponentsInChildren<NavMeshObstacle>(true);

            // + obstacle trong objectsToDisable
            var list = new System.Collections.Generic.List<NavMeshObstacle>();
            if (selfObs != null && selfObs.Length > 0) list.AddRange(selfObs);

            if (objectsToDisable != null)
            {
                foreach (var go in objectsToDisable)
                {
                    if (!go) continue;
                    var obs = go.GetComponentsInChildren<NavMeshObstacle>(true);
                    if (obs != null && obs.Length > 0) list.AddRange(obs);
                }
            }

            navMeshObstaclesToDisable = list.ToArray();
        }

        if (navMeshObstaclesToDisable == null) return;

        // 2) Tắt obstacle để agent đi lại được
        foreach (var ob in navMeshObstaclesToDisable)
        {
            if (!ob) continue;

            if (alsoDisableCarving)
                ob.carving = false;

            ob.enabled = false;
        }
    }

}
