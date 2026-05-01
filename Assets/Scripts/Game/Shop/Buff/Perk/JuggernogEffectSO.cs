using UnityEngine;

namespace TT
{
    [CreateAssetMenu(menuName = "TT/Perks/Effects/Juggernog (Max HP)", fileName = "PerkFx_Juggernog")]
    public class JuggernogEffectSO : PerkEffectSO
    {
        [Header("HP")]
        [Tooltip("Max HP mới khi có perk (COD style ~250).")]
        public float newMaxHp = 250f;
        public int healDeltaOnAcquire = 150;

        [Tooltip("Khi mua perk, có heal full lên max mới không?")]
        public bool healToFullOnAcquire = true;

        public override void OnAcquired(PerkContext ctx, int newStacks)
        {
            if (!TryGetPlayerHealth(ctx.owner, out var health))
                return;

            if (!HasAuthorityToMutate(ctx.owner))
                return;

            var cache = ctx.owner.GetComponent<PerkHealthCache>();
            if (cache == null) cache = ctx.owner.AddComponent<PerkHealthCache>();

            if (!cache.hasBaseMax)
            {
                cache.baseMax = health.maxHealth;
                cache.hasBaseMax = true;
            }

            // 1) set max
            health.maxHealth = Mathf.Max(1f, newMaxHp);

            // 2) heal +150 (clamp)
            int cur = Mathf.RoundToInt(health.currentHealth);
            int max = Mathf.RoundToInt(health.maxHealth);

            int targetCur = Mathf.Clamp(cur + healDeltaOnAcquire, 0, max);

            // IMPORTANT: dùng SetCurrentFromNet để bắn OnHpChanged (UI/Sync đang nghe)
            health.SetCurrentFromNet(targetCur);
        }

        public override void OnRemoved(PerkContext ctx, int oldStacks)
        {
            if (!TryGetPlayerHealth(ctx.owner, out var health))
                return;

            if (!HasAuthorityToMutate(ctx.owner))
                return;

            var cache = ctx.owner.GetComponent<PerkHealthCache>();
            if (cache == null || !cache.hasBaseMax)
                return;

            health.maxHealth = Mathf.Max(1f, cache.baseMax);

            int targetCur = Mathf.Min(Mathf.RoundToInt(health.currentHealth), Mathf.RoundToInt(health.maxHealth));
            health.SetCurrentFromNet(targetCur);
        }

        // ===== helpers =====

        static bool TryGetPlayerHealth(GameObject owner, out DamageableHealth health)
        {
            health = null;
            if (!owner) return false;

            // Quan trọng: lọc player để không dính zombie
            // Ưu tiên: có PlayerLifeController => chắc chắn player
            if (owner.GetComponentInChildren<PlayerLifeController>(true) == null &&
                owner.GetComponentInParent<PlayerLifeController>() == null)
                return false;

            health = owner.GetComponentInChildren<DamageableHealth>(true);
            return health != null;
        }

        static bool HasAuthorityToMutate(GameObject owner)
        {
#if FUSION_WEAVER
            var no = owner.GetComponentInChildren<Fusion.NetworkObject>(true);
            if (no != null)
            {
                // ✅ Chỉ enforce authority khi Fusion runner đang chạy (MP thật)
                var runner = no.Runner;
                if (runner != null && runner.IsRunning)
                {
                    if (!no.HasStateAuthority)
                        return false;
                }
                // SP (runner null / not running) -> cho phép mutate
            }
#endif
            return true;
        }


        /// <summary>Cache base max HP để perk có thể restore khi remove.</summary>
        public class PerkHealthCache : MonoBehaviour
        {
            public bool hasBaseMax;
            public float baseMax;
        }
    }
}
