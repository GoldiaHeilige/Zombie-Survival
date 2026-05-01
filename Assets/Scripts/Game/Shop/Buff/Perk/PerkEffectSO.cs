// PerkEffectSO.cs
using UnityEngine;

namespace TT
{
    /// <summary>
    /// Base class cho logic perk. Mỗi perk có thể có 1 hoặc nhiều EffectSO.
    /// Tạm thời Modify* để pass-through (trống).
    /// </summary>
    public abstract class PerkEffectSO : ScriptableObject
    {
        public virtual void OnAcquired(PerkContext ctx, int newStacks) { }
        public virtual void OnRemoved(PerkContext ctx, int oldStacks) { }

        // ====== Placeholder Modify hooks (tạm để trống / pass-through) ======
        public virtual float ModifyReloadSpeedMultiplier(PerkContext ctx, float current) => current;
        public virtual float ModifyDamageMultiplier(PerkContext ctx, float current) => current;
        public virtual int ModifyMaxHealth(PerkContext ctx, int current) => current;
        public virtual float ModifyMoveSpeedMultiplier(PerkContext ctx, float current) => current;
        public virtual float ModifyFireRateMultiplier(PerkContext ctx, float current) => current;
        public virtual float ModifyReloadDurationMultiplier(PerkContext ctx, float current) => current;
        public virtual float ModifyReviveDurationMultiplier(PerkContext ctx, float current) => current;


        // (sau này cần gì add tiếp: OnKill, OnHit, ModifySpread, ModifyRecoil,...)
    }

    /// <summary>Context truyền cho perk thao tác trên player hiện tại.</summary>
    public readonly struct PerkContext
    {
        public readonly GameObject owner;
        public readonly Component perkManager; // để effect có thể query/notify nếu cần
        public readonly PlayerPoints points;   // optional (có thể null)

        public PerkContext(GameObject owner, Component perkManager, PlayerPoints points)
        {
            this.owner = owner;
            this.perkManager = perkManager;
            this.points = points;
        }
    }
}
