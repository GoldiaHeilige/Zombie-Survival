using UnityEngine;

#if FUSION_WEAVER
using Fusion;
#endif

/// <summary>
/// World pickup for a power-up. On trigger it calls PowerUpManager (authority) to apply.
///
/// Notes:
/// - MP: put this prefab in Fusion Prefab Table with NetworkObject if you want it synced.
/// - If you keep it non-networked, only Host will see it (not recommended).
/// </summary>
[DisallowMultipleComponent]
public class PowerUpPickup : MonoBehaviour
{
    [Header("Type")]
    public PowerUpType type;

    [Header("Lifetime")]
    [Tooltip("Seconds until despawn if nobody collects.")]
    public float lifetime = 30f;

    [Header("FX")]
    public Transform rotateVisual;
    public float rotateSpeed = 90f;

    bool _consumed;
    float _dieAt;

#if FUSION_WEAVER
    NetworkRunner _runner;
    NetworkObject _no;
#endif

    void Awake()
    {
        _dieAt = Time.time + Mathf.Max(1f, lifetime);

#if FUSION_WEAVER
        _runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
        _no = GetComponent<NetworkObject>();
#endif
    }

    void Update()
    {
        if (rotateVisual != null && rotateSpeed != 0f)
            rotateVisual.Rotate(0f, rotateSpeed * Time.deltaTime, 0f, Space.Self);

#if FUSION_WEAVER
        if (_runner != null && _runner.IsRunning)
        {
            if (_runner.IsServer && Time.time >= _dieAt)
                Despawn();
            return;
        }
#endif

        if (Time.time >= _dieAt)
            Destroy(gameObject);
    }

    void OnTriggerEnter(Collider other)
    {
        if (_consumed) return;

        var prov = other.GetComponentInParent<PlayerStateProvider>();
        if (prov == null) return;

#if FUSION_WEAVER
        // MP: chỉ Host/Server xử lý pickup để tránh double
        if (_runner != null && _runner.IsRunning && !_runner.IsServer)
            return;
#endif

        var mgr = PowerUpManager.Instance;
        if (mgr == null) return;

        _consumed = true;

        // ✅ Per-type collect SFX (global) – chỉ host gọi (AudioEvents sẽ broadcast)
        // (script này là cái bạn vừa thêm vào prefab)
        var collectAudio = GetComponent<PowerUpPickupCollectAudio>();
        collectAudio?.PlayCollectSfx();

        // ✅ Apply gameplay effect (authority)
        mgr.Collect(type, prov.gameObject);

        Despawn();
    }


    void Despawn()
    {
#if FUSION_WEAVER
        if (_runner != null && _runner.IsRunning && _runner.IsServer && _no != null)
        {
            _runner.Despawn(_no);
            return;
        }
#endif
        Destroy(gameObject);
    }
}
