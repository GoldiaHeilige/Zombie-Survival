using UnityEngine;

namespace TT
{
    /// <summary>
    /// “Máy perk” kiểu COD. Chỉ chứa data: perk def, range, cost override.
    /// Logic mua nằm ở PerkManager + PlayerPickup.
    /// </summary>
    public class PerkMachineSpot : MonoBehaviour
    {
        [Header("Perk")]
        public PerkDefinition perk;

        [Tooltip("Nếu > 0 thì override perk.cost")]
        public int costOverride = 0;

        [Header("Interact")]
        public float interactRange = 2.5f;

        public int Cost => costOverride > 0 ? costOverride : (perk ? perk.cost : 0);

        public bool CanUse()
        {
            return perk != null && perk.IsValid;
        }

        public string DisplayName => perk ? perk.displayName : "Perk";
    }
}
