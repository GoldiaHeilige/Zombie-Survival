using UnityEngine;
using Fusion;

public class HealthSyncFusion : NetworkBehaviour
{
    [SerializeField] MonoBehaviour healthTarget; // kéo DamageableHealth vào
    [SerializeField] private PlayerLifeController life;  // auto-find nếu để trống
    [SerializeField] private MPHealthState mp;

    private LifeState _lastNetLife;
    IHealthSyncPort port;

    [Networked] public int NetHP { get; set; }
    int _lastNetHp = int.MinValue;

    // NEW: cache HP local để poll
    int _lastLocalHp = int.MinValue;

    void Awake()
    {
        if (!life) life = GetComponentInParent<PlayerLifeController>();
        if (!mp) mp = GetComponent<MPHealthState>();

        // Auto-bind đúng instance
        if (healthTarget == null)
            healthTarget = GetComponentInParent<DamageableHealth>(true);

        // Nếu bị kéo nhầm scene reference: ép lại theo cùng root với life
        if (life != null && healthTarget != null && healthTarget.transform.root != life.transform.root)
            healthTarget = life.GetComponentInParent<DamageableHealth>(true);

        port = healthTarget as IHealthSyncPort;
        if (port == null) { enabled = false; return; }
    }

    public override void Spawned()
    {
        if (!life) life = GetComponentInParent<PlayerLifeController>();
        if (!mp) mp = GetComponent<MPHealthState>();
        port = healthTarget as IHealthSyncPort;

        if (port == null)
        {
            Debug.LogError("[HealthSyncFusion] healthTarget không implement IHealthSyncPort");
            enabled = false;
            return;
        }

        _lastLocalHp = Mathf.RoundToInt(port.Current);

        if (Object.HasStateAuthority)
        {
            NetHP = port.Current;
            _lastNetHp = NetHP;

            // ĐỒNG BỘ BAN ĐẦU: Set LifeState từ PlayerLifeController
            if (life && mp)
            {
                mp.NetLife = life.state;
                _lastNetLife = life.state;
             //   Debug.Log($"[HealthSync] Host initial LifeState: {life.state}");
            }

            port.OnHpChanged += OnHostHpChanged;
        }
        else
        {
            // CLIENT: đổ số ban đầu
            _lastNetHp = NetHP;
            port.SetCurrentFromNet(NetHP);

            // QUAN TR�ọNG: Client phải áp dụng LifeState ban đầu từ host
            if (mp && life)
            {
                _lastNetLife = mp.NetLife;
                ApplyFromNet(mp.NetLife);
             //   Debug.Log($"[HealthSync] Client initial LifeState: {mp.NetLife}");
            }
        }
    }

    void Update()
    {
        // Host: đồng bộ LifeState thay đổi lên Network
        if (Object && Object.HasStateAuthority && life && mp)
        {
            if (life.state != _lastNetLife)
            {
            //    Debug.Log($"[HealthSync] Host LifeState changed: {_lastNetLife} -> {life.state}");
                mp.NetLife = life.state;
                _lastNetLife = life.state;
            }
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Object.HasStateAuthority && port != null)
            port.OnHpChanged -= OnHostHpChanged;
    }

    void OnHostHpChanged(int before, int after)
    {
        if (Object && Object.HasStateAuthority)
        {
            NetHP = after; // host ghi lên mạng
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!Object || !Object.HasStateAuthority || port == null) return;

        int cur = Mathf.RoundToInt(port.Current);
        if (cur != _lastLocalHp || cur != NetHP)
        {
            _lastLocalHp = cur;
            NetHP = cur; // đẩy netvar
        }
    }

    public override void Render()
    {
        // CLIENT kéo từ NetHP về local
        if (!Object || Object.HasStateAuthority || port == null) return;

        if (NetHP != _lastNetHp)
        {
            _lastNetHp = NetHP;
            port.SetCurrentFromNet(NetHP);
        }

        // CLIENT: áp dụng LifeState từ host (SỬA QUAN TRỌNG)
        if (!Object.HasStateAuthority && mp != null)
        {
            var netLife = mp.NetLife;
            if (netLife != _lastNetLife)
            {
          //      Debug.Log($"[HealthSync] Client LifeState sync: {_lastNetLife} -> {netLife}");
                _lastNetLife = netLife;
                ApplyFromNet(netLife);
            }
        }
    }


    // THÊM DEBUG VÀO ApplyFromNet
    private void ApplyFromNet(LifeState s)
    {
        if (!life) return;

  //      Debug.Log($"[HealthSync] ApplyFromNet: {s}, current state: {life.state}, HasStateAuth: {Object?.HasStateAuthority}");

        // CHỈ áp dụng nếu state khác nhau
        if (life.state == s) return;

        // QUAN TRỌNG: Cập nhật state trước khi gọi signal
        life.state = s;

        switch (s)
        {
            case LifeState.Downed:
                Debug.Log("[HealthSync] Client -> SignalDowned");
                life.SignalDowned(); // Đảm bảo DownedCtrl được kích hoạt
                break;
            case LifeState.Dead:
                Debug.Log("[HealthSync] Client -> SignalDead");
                life.SignalDead();
                break;
            case LifeState.Alive:
                Debug.Log("[HealthSync] Client -> SignalRevived");
                life.SignalRevived();
                break;
        }
    }

    public void ServerSetLife(LifeState s)
    {
        if (!Object || !Object.HasStateAuthority) return;
        var mp = GetComponent<MPHealthState>();
        if (mp) mp.NetLife = s;
    }
}
