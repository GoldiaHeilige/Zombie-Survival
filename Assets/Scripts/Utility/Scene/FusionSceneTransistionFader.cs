using Fusion;
using UnityEngine;
using TT.UI;
using Fusion.Sockets;
using System.Collections.Generic;

public class FusionSceneFadeCallbacks : MonoBehaviour, INetworkRunnerCallbacks
{
    private NetworkRunner _runner;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    public void Bind(NetworkRunner runner)
    {
        if (_runner == runner) return;

        if (_runner != null) _runner.RemoveCallbacks(this);
        _runner = runner;
        if (_runner != null) _runner.AddCallbacks(this);
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
        // Fade out ngay khi Fusion bắt đầu load scene
        SceneTransitionFader.Instance?.BeginNetworkFadeOut();
    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        // Fade in khi Fusion báo load xong
        SceneTransitionFader.Instance?.BeginNetworkFadeIn();
    }

    // ====== các callback còn lại để trống ======
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, System.Collections.Generic.List<SessionInfo> sessionList) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, System.ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
