using UnityEngine;
#if FUSION_WEAVER
using Fusion;
#endif

public class PlayerStateProvider : MonoBehaviour
{
    public IMovementState Movement { get; private set; }
    public ILoadoutState Loadout { get; private set; }
    public IHealthState Health { get; private set; }

    void Awake()
    {
#if FUSION_WEAVER
        var runner = FindAnyObjectByType<NetworkRunner>();
        bool isMultiplayer = runner != null && runner.IsRunning;
        if (isMultiplayer)
        {
            // Movement
            var mmp = GetComponent<PlayerMovementStateMP>();
            if (mmp != null) Movement = mmp;

            var hmp = GetComponentInChildren<MPHealthState>(true);
            if (hmp != null) Health = hmp;

            // Loadout
            var lmp = GetComponent<PlayerLoadoutStateMP>();
            if (lmp != null)
            {
                Loadout = lmp;
            }

            var no = GetComponent<NetworkObject>();
            Debug.Log($"[PlayerStateProvider] Using MP Providers - StateAuth:{no?.HasStateAuthority} InputAuth:{no?.HasInputAuthority}");
            return;
        }
#endif
        // Singleplayer fallback
        Movement = GetComponentInChildren<PlayerMovementStateSP>(true);
        Loadout = GetComponentInChildren<PlayerLoadoutStateSP>(true) as ILoadoutState;
        Health = GetComponentInChildren<SPHealthState>(true) as IHealthState;

    //  Debug.Log("[PlayerStateProvider] Using SP Providers (no active NetworkRunner)");
    }
}
