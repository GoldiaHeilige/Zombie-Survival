using UnityEngine;

namespace TT
{
    [CreateAssetMenu(menuName = "TT/Perks/Effects/Speed Cola (Reload)")]
    public class PerkSpeedColaEffectSO : PerkEffectSO
    {
        [Tooltip("0.4 = giảm 60% thời gian reload")]
        [Range(0.05f, 2f)] public float reloadDurationMultiplier = 0.4f;

        public override float ModifyReloadDurationMultiplier(PerkContext ctx, float current)
            => current * reloadDurationMultiplier;
    }
}
