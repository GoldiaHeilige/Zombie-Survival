#if FUSION_WEAVER
using Fusion;
using UnityEngine;

namespace TT
{
    public enum PerkId : int
    {
        DoubleTap = 0,
        SpeedCola = 1,
        Juggernog = 2,
        QuickRevive = 3,
    }

    [RequireComponent(typeof(NetworkObject))]
    public class PerkNetState : NetworkBehaviour
    {
        [Header("Definitions (index must match PerkId enum)")]
        [SerializeField] private PerkDefinition[] defsById;

        [Networked] public uint PerkMask { get; set; }

        private uint _lastMask;
        private bool _inited;

        public bool Has(PerkId id) => (PerkMask & (1u << (int)id)) != 0;

        public void GrantOnServer(PerkId id)
        {
            if (!Object || !Object.HasStateAuthority) return;
            PerkMask |= (1u << (int)id);
        }

        public float FireRateMult => Has(PerkId.DoubleTap) ? 2f : 1f;
        public float ReloadMult => Has(PerkId.SpeedCola) ? 0.5f : 1f;

        private System.Action<object> _onAcquired;

        public override void Spawned()
        {
            // Chỉ Host/StateAuthority mới được set Networked property
            if (Object != null && Object.HasStateAuthority)
            {
                _onAcquired = OnPerkAcquired;
                Observer.Instance?.AddObserver(PerkTopics.Acquired, _onAcquired);
            }
        }

        private void OnDestroy()
        {
            if (_onAcquired != null)
                Observer.Instance?.RemoveObserver(PerkTopics.Acquired, _onAcquired);
        }


        public PerkDefinition GetDef(PerkId id)
        {
            int idx = (int)id;
            if (defsById == null || idx < 0 || idx >= defsById.Length) return null;
            return defsById[idx];
        }

        // Map string perkId -> enum (dùng cho BuyUI)
        public static bool TryMapPerkString(string perkIdString, out PerkId id)
        {
            id = default;
            if (string.IsNullOrWhiteSpace(perkIdString)) return false;

            switch (perkIdString)
            {
                case "perk_doubletap": id = PerkId.DoubleTap; return true;
                case "perk_speedcola": id = PerkId.SpeedCola; return true;
                case "perk_juggernog": id = PerkId.Juggernog; return true;
                case "perk_quickrevive": id = PerkId.QuickRevive; return true;
            }
            return false;
        }

        // Render() chạy ở cả host + client, phù hợp để detect thay đổi state replication
        public override void Render()
        {
            if (!Object) return;

            if (!_inited)
            {
                _inited = true;
                _lastMask = 0;            // để join trễ cũng bắn event cho perk đã có
            }

            uint newMask = PerkMask;
            uint added = newMask & ~_lastMask;
            if (added != 0)
            {
                FireAcquiredEvents(added);
            }

            _lastMask = newMask;
        }

        private void FireAcquiredEvents(uint addedMask)
        {
            if (Object != null && Object.HasStateAuthority)
            {
                return;
            }

            for (int bit = 0; bit < 32; bit++)
            {
                uint flag = 1u << bit;
                if ((addedMask & flag) == 0) continue;

                var pid = (PerkId)bit;
                var def = GetDef(pid);
                if (def == null) continue; // đừng check IsValid ở đây, vì nhiều perk def của mày "valid" theo rule khác


                var payload = new PerkChangedEventData
                {
                    owner = gameObject,
                    def = def,
                    perkId = def.perkId,
                    displayName = def.displayName,
                    icon = def.icon,
                    stacks = 1,
                    cost = def.cost,
                    source = null
                };

                Observer.Instance?.NotifyWithData(PerkTopics.Acquired, payload);
            }
        }

        private void OnPerkAcquired(object data)
        {
            if (!Object || !Object.HasStateAuthority) return;

            if (data is not PerkChangedEventData ev) return;
            if (ev.owner != gameObject) return; // chỉ ăn event của chính player này

            if (TryMapByDefs(ev.perkId, out var pid))
                GrantOnServer(pid);
        }

        private bool TryMapByDefs(string perkIdString, out PerkId id)
        {
            id = default;
            if (string.IsNullOrWhiteSpace(perkIdString) || defsById == null) return false;

            // match perkId theo defsById đang gán ở inspector (index = bit)
            for (int i = 0; i < defsById.Length && i < 32; i++)
            {
                var d = defsById[i];
                if (d != null && d.perkId == perkIdString)
                {
                    id = (PerkId)i;
                    return true;
                }
            }
            return false;
        }

    }
}
#endif
