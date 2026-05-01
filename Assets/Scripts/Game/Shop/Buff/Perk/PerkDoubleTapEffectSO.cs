using UnityEngine;

namespace TT
{
    [CreateAssetMenu(menuName = "TT/Perks/Effects/Double Tap (ROF)")]
    public class PerkDoubleTapEffectSO : PerkEffectSO
    {
        [Min(0.01f)] public float fireRateMultiplier = 1.33f; // tuỳ bạn

        public override float ModifyFireRateMultiplier(PerkContext ctx, float current)
            => current * fireRateMultiplier;
    }
}
