using UnityEngine;

namespace TT
{
    [CreateAssetMenu(menuName = "TT/Perks/Effects/Quick Revive (MP revive)")]
    public class PerkQuickReviveEffectSO : PerkEffectSO
    {
        [Tooltip("Base 3.0s -> 1.5s = 0.5")]
        [Range(0.05f, 2f)] public float reviveDurationMultiplier = 0.5f;

        public override float ModifyReviveDurationMultiplier(PerkContext ctx, float current)
            => current * reviveDurationMultiplier;
    }
}
