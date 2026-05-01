// PerkManager.cs
using System;
using System.Collections.Generic;
using UnityEngine;

#if FUSION_WEAVER
using Fusion;
#endif


namespace TT
{
    [DisallowMultipleComponent]
    public class PerkManager : MonoBehaviour
    {
        [Serializable]
        public class PerkEntry
        {
            public PerkDefinition def;
            public int stacks;
        }

        [Header("Debug")]
        [SerializeField] private List<PerkEntry> perks = new List<PerkEntry>();

#if FUSION_WEAVER
        private TT.PerkNetState _netState;
#endif

        public IReadOnlyList<PerkEntry> Perks => perks;

        public bool HasPerk(string perkId) => FindEntry(perkId) != null;

        public int GetStacks(string perkId)
        {
            var e = FindEntry(perkId);
            return e != null ? e.stacks : 0;
        }
        private void Awake()
        {
#if FUSION_WEAVER
            _netState = GetComponentInParent<TT.PerkNetState>(true);
#endif
        }

        /// <summary>
        /// Mua perk: tự trừ điểm bằng PlayerPoints nếu có.
        /// vendor: object máy perk để debug / feed.
        /// </summary>
        public bool TryPurchase(PerkDefinition def, PlayerPoints points, GameObject vendor = null)
        {
          //  Debug.Log($"[PerkMgr] TryPurchase perk={(def ? def.perkId : "null")} cost={(def ? def.cost : 0)} points={(points ? points.Current : 0)}");

            if (def == null || !def.IsValid)
            {
                NotifyPurchaseFailed(def, points, vendor, PerkPurchaseFailReason.InvalidDefinition);
             //   Debug.Log($"[PerkMgr] PurchaseFailed reason=No SO perk={(def ? def.perkId : "null")}  cost={(def ? def.cost : 0)} points={(points ? points.Current : 0)}");

                return false;
            }

            var entry = FindEntry(def.perkId);
            int currentStacks = entry != null ? entry.stacks : 0;

            if (currentStacks >= Mathf.Max(1, def.maxStacks))
            {
                NotifyPurchaseFailed(def, points, vendor, PerkPurchaseFailReason.MaxStacksReached);
             //   Debug.Log($"[PerkMgr] PurchaseFailed reason=MaxStack perk={(def ? def.perkId : "null")} stacks={currentStacks} cost={(def ? def.cost : 0)} points={(points ? points.Current : 0)}");

                return false;
            }

            // Nếu có points thì check/spend
            if (points != null)
            {
                if (!points.CanAfford(def.cost))
                {
                    NotifyPurchaseFailed(def, points, vendor, PerkPurchaseFailReason.NotEnoughPoints);
                 //   Debug.Log($"[PerkMgr] PurchaseFailed reason= Not Enought PT perk={(def ? def.perkId : "null")} stacks={currentStacks} cost={(def ? def.cost : 0)} points={(points ? points.Current : 0)}");

                    return false;
                }

                // Trừ điểm (PointReason.Purchase đang có trong PlayerPoints.cs)
                if (!points.TrySpend(def.cost, PointReason.Purchase, vendor))
                {
                    NotifyPurchaseFailed(def, points, vendor, PerkPurchaseFailReason.SpendFailed);
                 //   Debug.Log($"[PerkMgr] PurchaseFailed reason=Cant Spend Point? perk={(def ? def.perkId : "null")} stacks={currentStacks} cost={(def ? def.cost : 0)} points={(points ? points.Current : 0)}");

                    return false;
                }
            }

            // Apply
            int newStacks = currentStacks + 1;

            if (entry == null)
            {
                entry = new PerkEntry { def = def, stacks = 0 };
                perks.Add(entry);
            }

            entry.stacks = newStacks;

            var ctx = new PerkContext(gameObject, this, points);
            ApplyOnAcquired(def, ctx, newStacks);

            NotifyChanged(PerkTopics.Acquired, def, newStacks, vendor);

            // Nếu stack > 1 thì cũng coi là updated
            if (newStacks > 1)
                NotifyChanged(PerkTopics.Updated, def, newStacks, vendor);

            return true;
        }

        public bool RemovePerk(string perkId, PlayerPoints points = null, GameObject reasonObj = null)
        {
            var entry = FindEntry(perkId);
            if (entry == null) return false;

            int oldStacks = entry.stacks;
            var def = entry.def;

            perks.Remove(entry);

            var ctx = new PerkContext(gameObject, this, points);
            ApplyOnRemoved(def, ctx, oldStacks);

            NotifyChanged(PerkTopics.Removed, def, 0, reasonObj);
            return true;
        }

        // ====== Modify Aggregation (tạm thời gọi effect pass-through) ======

        public float GetReloadSpeedMultiplier(PlayerPoints points = null)
        {
            var ctx = new PerkContext(gameObject, this, points);
            float v = 1f;
            foreach (var e in perks)
                v = ApplyModify(e.def, eff => eff.ModifyReloadSpeedMultiplier(ctx, v), v);
            return v;
        }

        public float GetDamageMultiplier(PlayerPoints points = null)
        {
            var ctx = new PerkContext(gameObject, this, points);
            float v = 1f;
            foreach (var e in perks)
                v = ApplyModify(e.def, eff => eff.ModifyDamageMultiplier(ctx, v), v);
            return v;
        }

        public int GetMaxHealth(PlayerPoints points = null, int baseHealth = 100)
        {
            var ctx = new PerkContext(gameObject, this, points);
            int v = baseHealth;
            foreach (var e in perks)
                v = ApplyModify(e.def, eff => eff.ModifyMaxHealth(ctx, v), v);
            return v;
        }

        public float GetMoveSpeedMultiplier(PlayerPoints points = null)
        {
            var ctx = new PerkContext(gameObject, this, points);
            float v = 1f;
            foreach (var e in perks)
                v = ApplyModify(e.def, eff => eff.ModifyMoveSpeedMultiplier(ctx, v), v);
            return v;
        }

        public float GetFireRateMultiplier(PlayerPoints points = null)
        {
#if FUSION_WEAVER
            // Nếu đang ở Fusion và có net state => client cũng đọc được
            if (_netState != null && _netState.Object != null)
                return _netState.FireRateMult;
#endif

            // fallback SP / non-fusion: dùng list perks local
            var ctx = new PerkContext(gameObject, this, points);
            float v = 1f;
            foreach (var e in perks)
                v = ApplyModify(e.def, eff => eff.ModifyFireRateMultiplier(ctx, v), v);
            return v;
        }

        public float GetReloadDurationMultiplier(PlayerPoints points = null)
        {
            var ctx = new PerkContext(gameObject, this, points);
            float v = 1f;
            foreach (var e in perks)
                v = ApplyModify(e.def, eff => eff.ModifyReloadDurationMultiplier(ctx, v), v);

            // clamp tránh bug timer = 0
            return Mathf.Clamp(v, 0.05f, 10f);
        }

        public float GetReviveDurationMultiplier(PlayerPoints points = null)
        {
            var ctx = new PerkContext(gameObject, this, points);
            float v = 1f;
            foreach (var e in perks)
                v = ApplyModify(e.def, eff => eff.ModifyReviveDurationMultiplier(ctx, v), v);

            return Mathf.Clamp(v, 0.05f, 10f);
        }


        // ====== Internals ======

        PerkEntry FindEntry(string perkId)
        {
            if (string.IsNullOrWhiteSpace(perkId)) return null;
            for (int i = 0; i < perks.Count; i++)
            {
                var e = perks[i];
                if (e != null && e.def != null && e.def.perkId == perkId) return e;
            }
            return null;
        }

        void ApplyOnAcquired(PerkDefinition def, PerkContext ctx, int newStacks)
        {
            if (def.effects == null) return;
            foreach (var eff in def.effects)
                if (eff != null) eff.OnAcquired(ctx, newStacks);
        }

        void ApplyOnRemoved(PerkDefinition def, PerkContext ctx, int oldStacks)
        {
            if (def.effects == null) return;
            foreach (var eff in def.effects)
                if (eff != null) eff.OnRemoved(ctx, oldStacks);
        }

        T ApplyModify<T>(PerkDefinition def, Func<PerkEffectSO, T> apply, T fallback)
        {
            if (def == null || def.effects == null) return fallback;
            T v = fallback;
            foreach (var eff in def.effects)
            {
                if (eff == null) continue;
                v = apply(eff);
            }
            return v;
        }

        void NotifyChanged(string topic, PerkDefinition def, int stacks, GameObject source)
        {
            var payload = new PerkChangedEventData
            {
                owner = gameObject,
                def = def,
                perkId = def != null ? def.perkId : null,
                displayName = def != null ? def.displayName : null,
                icon = def != null ? def.icon : null,
                stacks = stacks,
                cost = def != null ? def.cost : 0,
                source = source
            };

            Observer.Instance?.NotifyWithData(topic, payload);
        }

        void NotifyPurchaseFailed(PerkDefinition def, PlayerPoints points, GameObject source, PerkPurchaseFailReason reason)
        {
            var payload = new PerkPurchaseFailedEventData
            {
                owner = gameObject,
                def = def,
                perkId = def != null ? def.perkId : null,
                cost = def != null ? def.cost : 0,
                currentPoints = points != null ? points.Current : 0,
                reason = reason,
                source = source
            };

            Observer.Instance?.NotifyWithData(PerkTopics.PurchaseFailed, payload);
        }
    }

    public enum PerkPurchaseFailReason
    {
        Unknown = 0,
        InvalidDefinition = 1,
        NotEnoughPoints = 2,
        MaxStacksReached = 3,
        SpendFailed = 4
    }

    public struct PerkChangedEventData
    {
        public GameObject owner;
        public PerkDefinition def;
        public string perkId;
        public string displayName;
        public Sprite icon;
        public int stacks;
        public int cost;
        public GameObject source; // perk machine / vendor / reason obj
    }

    public struct PerkPurchaseFailedEventData
    {
        public GameObject owner;
        public PerkDefinition def;
        public string perkId;
        public int cost;
        public int currentPoints;
        public PerkPurchaseFailReason reason;
        public GameObject source;
    }
}
