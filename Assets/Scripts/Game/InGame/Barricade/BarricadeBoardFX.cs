using UnityEngine;
using TT;

public class BarricadeBoardFX : MonoBehaviour
{
    [Header("Barricade Link")]
    public BarricadeWindow window;
    public int slotIndex;

    [Header("Nail VFX (using ImpactPool)")]
    [SerializeField] private GameObject nailImpactPrefab;
    [SerializeField] private Transform nailLeftPoint;
    [SerializeField] private Transform nailRightPoint;

    // ===== Animation Events =====

    public void Anim_VFX_ImpactNailLeft()
    {
        if (!ImpactPool.Instance)
        {
            Debug.LogWarning("[BarricadeBoardFX] ImpactPool missing.");
            return;
        }

        if (nailImpactPrefab && nailLeftPoint)
        {
            // normal có thể là -forward của anchor / board
            var normal = -nailLeftPoint.forward; // tạm dùng vậy, sau muốn thì chỉnh theo tường
            ImpactPool.Instance.Spawn(nailImpactPrefab, nailLeftPoint.position, normal);
        }
    }

    public void Anim_VFX_ImpactNailRight()
    {
        if (!ImpactPool.Instance)
        {
            Debug.LogWarning("[BarricadeBoardFX] ImpactPool missing.");
            return;
        }

        if (nailImpactPrefab && nailRightPoint)
        {
            var normal = -nailRightPoint.forward;
            ImpactPool.Instance.Spawn(nailImpactPrefab, nailRightPoint.position, normal);
        }
    }

    public void Anim_OnBuildFinished()
    {
        if (window != null)
        {
            window.OnBoardBuildFinished(slotIndex);

            // Bắn topic cho audio snap/repair
            var evt = new BarricadeRepairEvent
            {
                player = null,
                window = window.gameObject,
                slotIndex = slotIndex
            };
            Observer.Instance?.NotifyWithData(BarricadeTopics.BoardBuilt, evt);
        }
        else
        {
            Debug.LogWarning("[BarricadeBoardFX] Missing window reference.");
        }
    }
}
