using System.Collections;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using TT;

public class FusionPlayerSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [Header("Đăng ký prefab trong NetworkProjectConfig → Prefabs, kéo vào đây")]
    [SerializeField] private NetworkPrefabRef playerPrefabRef;

    private NetworkRunner _runner;
    private readonly Dictionary<PlayerRef, NetworkObject> _spawned = new();
    private readonly HashSet<PlayerRef> _pending = new();

    private bool _sceneReady;
    private Coroutine _watchdogCo;

    private bool _handledDisconnect;

    void Awake()
    {
        _runner = Object.FindFirstObjectByType<NetworkRunner>();
        if (_runner != null) _runner.AddCallbacks(this);
    }

    void OnEnable()
    {
        if (_runner == null)
        {
            _runner = Object.FindFirstObjectByType<NetworkRunner>();
            if (_runner != null) _runner.AddCallbacks(this);
        }

        // 🔧 NEW: gameplay scene đã ở trạng thái ready → đánh dấu luôn
        if (_runner != null && _runner.IsRunning && _runner.SceneManager != null)
            _sceneReady = true;

        if (_watchdogCo == null)
            _watchdogCo = StartCoroutine(Co_WatchdogSpawn());
    }


    void OnDisable()
    {
        if (_runner != null) _runner.RemoveCallbacks(this);
        if (_watchdogCo != null) { StopCoroutine(_watchdogCo); _watchdogCo = null; }
    }

    IEnumerator Co_WatchdogSpawn()
    {
        // Đợi runner xuất hiện & chạy
        float t = 0f;
        while (_runner == null || !_runner.IsRunning)
        {
            _runner = _runner ?? Object.FindFirstObjectByType<NetworkRunner>();
            yield return null;
        }

        // Nếu không dùng SceneManager, coi như sẵn sàng ngay
        if (_runner.SceneManager == null) _sceneReady = true;

        // Đợi tối đa 1 giây cho OnSceneLoadDone. Nếu không đến → coi như ready
        while (!_sceneReady && t < 1.0f)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
/*        if (!_sceneReady)
        {
            _sceneReady = true;
            Debug.LogWarning("[FusionPlayerSpawner] Scene never signaled ready; forcing ready after 1s.");
        }*/

        TrySpawnAllIfReady();

        // Tiếp tục canh players để spawn bù nếu spawner được bật trễ
        // Tiếp tục canh players để spawn bù nếu spawner được bật trễ
        while (enabled && _runner != null && _runner.IsRunning)
        {
            // 1) Spawn bù (giữ nguyên)
            foreach (var p in _runner.ActivePlayers)
                if (!_spawned.ContainsKey(p)) TrySpawnFor(p);

            // 2) ✅ Despawn orphan: player crash/Alt+F4 làm OnPlayerLeft đến trễ/không đúng nhịp
            if (_runner.IsServer && _spawned.Count > 0)
            {
                // build set active để check nhanh
                var active = new HashSet<PlayerRef>();
                foreach (var p in _runner.ActivePlayers) active.Add(p);

                List<PlayerRef> toDespawn = null;

                foreach (var kv in _spawned)
                {
                    var player = kv.Key;
                    if (!active.Contains(player))
                    {
                        toDespawn ??= new List<PlayerRef>(4);
                        toDespawn.Add(player);
                    }
                }

                if (toDespawn != null)
                {
                    foreach (var player in toDespawn)
                    {
                        Debug.LogWarning($"[FusionPlayerSpawner] Orphan player detected -> despawn [{player}]");
                        DespawnFor(player);
                        _pending.Remove(player);
                    }
                }
            }

            yield return new WaitForSeconds(0.25f);
        }

    }

    // ===== Core =====
    private void TrySpawnAllIfReady()
    {
        if (_runner == null || !_runner.IsRunning || !_sceneReady) return;
        if (!_runner.IsServer) return;                // ← CHỈ SERVER ĐƯỢC SPAWN

        foreach (var p in _runner.ActivePlayers)
            TrySpawnFor(p);
    }

    private void TrySpawnFor(PlayerRef player)
    {
        if (_runner == null || !_runner.IsRunning) return;
        if (!_runner.IsServer) return;

        if (!_sceneReady) { _pending.Add(player); return; }
        if (_spawned.ContainsKey(player)) return;

        if (playerPrefabRef.Equals(default(NetworkPrefabRef)))
        {
            Debug.LogError("[FusionPlayerSpawner] playerPrefabRef is EMPTY.");
            return;
        }

        // Lấy cả position và rotation
        var spawnTransform = GetSpawnTransformFor(player);
        var obj = _runner.Spawn(playerPrefabRef, spawnTransform.position, spawnTransform.rotation, inputAuthority: player);
        _spawned[player] = obj;

        var en = obj.GetComponent<LocalOnlyEnabler>();
        if (en != null)
            en.Apply(obj, obj.transform);

        Debug.Log($"[FusionPlayerSpawner] Spawned for [{player}] at {spawnTransform.position}");
    }

    // FusionPlayerSpawner.cs (đã sửa - chỉ phần GetSpawnPosFor)
    private Vector3 GetSpawnPosFor(PlayerRef p)
    {
        // Thay vì spawn cố định theo index, dùng spawn point ngẫu nhiên
        Transform spawnPoint = null;

        // Cố gắng lấy spawn point từ manager
        if (PlayerSpawnManager.Instance != null)
        {
            spawnPoint = PlayerSpawnManager.Instance.GetRandomSpawnPoint();
        }

        if (spawnPoint != null)
        {
            return spawnPoint.position;
        }
        else
        {
            // Fallback: dùng logic cũ nếu không có spawn point
            Debug.LogWarning("[FusionPlayerSpawner] No spawn points available, using default positions");
            int i = p.RawEncoded % 8;
            return new Vector3(1f * i, 1f, 0f);
        }
    }

    // Có thể thêm method để lấy spawn point với rotation
    private (Vector3 position, Quaternion rotation) GetSpawnTransformFor(PlayerRef p)
    {
        Transform spawnPoint = null;

        if (PlayerSpawnManager.Instance != null)
        {
            spawnPoint = PlayerSpawnManager.Instance.GetRandomSpawnPoint();
        }

        if (spawnPoint != null)
        {
            return (spawnPoint.position, spawnPoint.rotation);
        }
        else
        {
            int i = p.RawEncoded % 8;
            return (new Vector3(1f * i, 1f, 0f), Quaternion.identity);
        }
    }

    private void DespawnFor(PlayerRef player)
    {
        if (_spawned.TryGetValue(player, out var obj))
        {
            if (obj && obj.Runner) _runner.Despawn(obj);
            _spawned.Remove(player);
        }
    }

    private void HandleDisconnect(NetworkRunner runner, NetDisconnectReason? netReason, ShutdownReason? shutdownReason)
    {
        if (_handledDisconnect) return;
        _handledDisconnect = true;

        var info = new NetDisconnectInfo
        {
            kind = NetDisconnectKind.Unknown,
            reasonText = null
        };

        // Nếu Fusion cho mình 1 lý do disconnect cụ thể (NetDisconnectReason),
        // ta coi như "mất kết nối" và hiển thị kèm lý do đó.
        if (netReason.HasValue)
        {
            info.kind = NetDisconnectKind.ConnectionLost;
            info.reasonText = $"Disconnected: {netReason.Value}\nReturning to menu...";
        }

        // Nếu runner shutdown (thường là host đóng game / session),
        // ở phía client ta coi như "host rời".
        if (shutdownReason.HasValue && info.kind == NetDisconnectKind.Unknown)
        {
            info.kind = NetDisconnectKind.HostLeft;
            info.reasonText ??= "Host has left the game.\nReturning to menu...";
        }

        // Fallback chung chung
        if (string.IsNullOrWhiteSpace(info.reasonText))
        {
            info.reasonText = "Disconnected from server.\nReturning to menu...";
        }

        Debug.Log($"[FusionPlayerSpawner] HandleDisconnect → {info.kind} | {info.reasonText}");

        // Bắn event cho overlay trong scene
        Observer.Instance?.NotifyWithData(NetworkTopics.Disconnected, info);
    }


    // ===== INetworkRunnerCallbacks =====
    public void OnSceneLoadStart(NetworkRunner runner) { _sceneReady = false; }
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        _sceneReady = true;
        if (_runner.IsServer)
        {                    // ← thêm
            TrySpawnAllIfReady();
            if (_pending.Count > 0) { foreach (var p in _pending) TrySpawnFor(p); _pending.Clear(); }
        }
    }


    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (!_runner.IsServer) return;
        if (_sceneReady) TrySpawnFor(player); else _pending.Add(player);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        // 🔹 Lấy NetworkObject của player vừa rời
        if (_spawned.TryGetValue(player, out var obj) && obj != null)
        {
            var bridge = obj.GetComponentInChildren<FusionNetBridge>();
            if (bridge != null)
            {
                // Lấy tên từ DisplayName (nếu rỗng thì fallback PlayerId)
                string name = bridge.DisplayName.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"Player{player.PlayerId}";
                }

                // Host (StateAuthority) broadcast "left game" trước khi Despawn
                bridge.RPC_AnnounceLeft(name);
            }
        }

        DespawnFor(player);
        _pending.Remove(player);
    }

    // Các callback còn lại (tuỳ version Fusion – để trống cho đủ interface)
    public void OnConnectedToServer(NetworkRunner runner)
    {
// disable input untill scene fade transistion is done (provideInput = true is placed there)
        runner.ProvideInput = true;
        Debug.Log($"[Spawner] ProvideInput=TRUE, IsServer={runner.IsServer}, IsClient={runner.IsClient}");
    }

    public void OnDisconnectedFromServer(NetworkRunner r, NetDisconnectReason reason)
    {
        Debug.Log($"[FusionPlayerSpawner] OnDisconnectedFromServer: {reason}");
        HandleDisconnect(r, reason, null);
    }

    public void OnConnectRequest(NetworkRunner r, NetworkRunnerCallbackArgs.ConnectRequest req, byte[] token) { }
    public void OnConnectFailed(NetworkRunner r, NetAddress remote, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner r, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner r, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner r, Dictionary<string, object> data) { }
    public void OnReliableDataReceived(NetworkRunner r, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner r, PlayerRef player, ReliableKey key, float progress) { }
    public void OnObjectEnterAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
    public void OnShutdown(NetworkRunner r, ShutdownReason reason)
    {
        Debug.Log($"[FusionPlayerSpawner] OnShutdown: {reason}");
        HandleDisconnect(r, null, reason);
    }
    public void OnHostMigration(NetworkRunner r, HostMigrationToken token) { }
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        // Lấy input từ FusionInputProvider (bạn đã có sẵn)
        var fp = FusionInputProvider.Instance;
        if (fp != null && fp.isActiveAndEnabled && fp.IsValid)
        {
            var data = fp.GetInputData();
            input.Set(data);
            return;
        }

        // Fallback: nếu bạn dùng InputHub
        var bus = InputHub.Instance;
        if (bus != null)
        {
            var s = bus.GetSnapshotForTick();
            PlayerInputData d = new PlayerInputData
            {
                move = s.Move,
                look = s.Look,
                jump = s.JumpDown,
                sprint = s.Sprint,
                crouch = s.Crouch,
                fire = s.Fire,
                reload = s.ReloadDown,
                ads = s.ADS,
                interact = s.InteractDown,
                viewYaw = s.ViewYaw,
            };
            input.Set(d);
        }
    }
    public void OnInputMissing(NetworkRunner r, PlayerRef p, NetworkInput i) { }
}
