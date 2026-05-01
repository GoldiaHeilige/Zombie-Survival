// Assets/Scripts/Game/UI/ServerBrowser/BrowserBootstrapSB.cs
using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace TT
{
    /// <summary>
    /// Bootstrap cho Server Browser:
    /// - Tạo NetworkRunner "trần" (KHÔNG StartGame).
    /// - Không reconnect, không join lobby ở đây (UI sẽ làm).
    /// - Đảm bảo duy nhất trong scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class BrowserBootstrapSB : MonoBehaviour, INetworkRunnerCallbacks
    {
        [SerializeField] private NetworkRunner runnerPrefab;

        private static BrowserBootstrapSB _singleton;
        private NetworkRunner _runner;

        private async void Awake()
        {
            if (_singleton && _singleton != this) { Destroy(gameObject); return; }
            _singleton = this;

            // Tạo runner “trần”
            var go = runnerPrefab ? Instantiate(runnerPrefab).gameObject
                                  : new GameObject("NetworkRunner_Browser");
            _runner = go.GetComponent<NetworkRunner>() ?? go.AddComponent<NetworkRunner>();
            DontDestroyOnLoad(go);

            _runner.ProvideInput = false;     // chỉ duyệt lobby, không cần input
            _runner.AddCallbacks(this);

            Debug.Log("[BrowserBootstrap] Runner created (no StartGame). Ready for UI to join lobby.");
            await System.Threading.Tasks.Task.CompletedTask;
        }

        private void OnDestroy()
        {
            if (_singleton == this) _singleton = null;
            if (_runner) _runner.RemoveCallbacks(this);
        }

        // --- Callbacks chỉ để log/trống, không can thiệp runner ---
        public void OnShutdown(NetworkRunner r, ShutdownReason reason)
            => Debug.LogWarning($"[BrowserBootstrap] OnShutdown → {reason}");
        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner r, NetDisconnectReason reason)
            => Debug.LogWarning($"[BrowserBootstrap] OnDisconnected → {reason}");

        // Unused (giữ trống cho đủ interface)
        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner r) { }
        public void OnPlayerJoined(NetworkRunner r, PlayerRef player) { }
        public void OnPlayerLeft(NetworkRunner r, PlayerRef player) { }
        public void OnInput(NetworkRunner r, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner r, PlayerRef player, NetworkInput input) { }
        public void OnConnectRequest(NetworkRunner r, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner r, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner r, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner r, System.Collections.Generic.List<SessionInfo> sessionList) { }
        public void OnHostMigration(NetworkRunner r, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner r, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner r, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadStart(NetworkRunner r) { }
        public void OnSceneLoadDone(NetworkRunner r) { }
        public void OnObjectExitAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
        public void OnCustomAuthenticationResponse(NetworkRunner r, System.Collections.Generic.Dictionary<string, object> data) { }
        public void OnCustomAuthenticationResponse(NetworkRunner r, System.Collections.Generic.IReadOnlyDictionary<string, object> data) { }
    }
}
