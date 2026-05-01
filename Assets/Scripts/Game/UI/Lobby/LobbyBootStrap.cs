// Assets/Scripts/Game/Lobby/LobbyBootStrap.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;

namespace TT
{
    [DisallowMultipleComponent]
    public class LobbyBootStrap : MonoBehaviour
    {
        [SerializeField] private NetworkRunner runnerPrefab;
        [SerializeField] private LobbyState lobbyStatePrefab;
        private NetworkRunner _runner;

        private async void Start()
        {
            // Nếu lỡ đi vào Lobby mà đang ở Single → chuyển thẳng sang map, KHÔNG khởi Runner
/*            if (GameSession.Mode == AppPlayMode.Single)
            {
                var map = string.IsNullOrWhiteSpace(GameSession.SelectedMap) ? "Gameplay_Map_01" : GameSession.SelectedMap;
                Debug.Log("[LobbyBootstrap] Single mode detected → loading map directly: " + map);
                UnityEngine.SceneManagement.SceneManager.LoadScene(map);
                return;
            }*/

            // MULTI: dọn runners cũ rồi khởi Runner mới
            await KillExistingRunnersAsync();
            await CreateAndStartRunner();
        }

        private async Task KillExistingRunnersAsync()
        {
            var runners = FindObjectsByType<NetworkRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (runners == null || runners.Length == 0) return;

            // Gửi shutdown trước
            foreach (var r in runners)
            {
                if (!r) continue;
                try { r.RemoveCallbacks(null); } catch { }
                try { await r.Shutdown(false); } catch { }
            }

            // Đợi thật sự xuống (tối đa 3s)
            float t = 0f;
            bool anyAlive;
            do
            {
                anyAlive = false;
                foreach (var r in runners)
                {
                    if (r && r.IsRunning) { anyAlive = true; break; }
                }
                t += Time.unscaledDeltaTime;
                await Task.Yield();
            } while (anyAlive && t < 3f);

            // Hủy GameObject để đảm bảo không còn runner nào lảng vảng
            foreach (var r in runners)
            {
                if (r && r.gameObject) Destroy(r.gameObject);
            }

            // đợi 1–2 frame cho chắc
            await Task.Yield();
            await Task.Yield();
        }

        private async Task CreateAndStartRunner()
        {
            if (LobbyParams.Mode == GameMode.Host || LobbyParams.Mode == GameMode.Server)
                GameSession.Mode = AppPlayMode.Host;
            else if (LobbyParams.Mode == GameMode.Client)
                GameSession.Mode = AppPlayMode.Client;
            else
                GameSession.Mode = AppPlayMode.Single;

            var go = runnerPrefab ? Instantiate(runnerPrefab).gameObject : new GameObject("NetworkRunner_Lobby");
            _runner = go.GetComponent<NetworkRunner>() ?? go.AddComponent<NetworkRunner>();
            DontDestroyOnLoad(go);
            _runner.ProvideInput = true;

            var fadeCb = _runner.GetComponent<FusionSceneFadeCallbacks>();
            if (fadeCb == null)
                fadeCb = _runner.gameObject.AddComponent<FusionSceneFadeCallbacks>();

            fadeCb.Bind(_runner);

            bool joiningAsClient = LobbyParams.Mode == GameMode.Client;

            if (!joiningAsClient)
            {
                var args = new StartGameArgs
                {
                    GameMode = LobbyParams.Mode,
                    SessionName = string.IsNullOrWhiteSpace(LobbyParams.SessionName)
                        ? $"Room_{Random.Range(1000, 9999)}"
                        : LobbyParams.SessionName,
                    SceneManager = _runner.GetComponent<INetworkSceneManager>(),
                    PlayerCount = LobbyParams.MaxPlayers,
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        ["Status"] = (SessionProperty)"Lobby",
                        ["MapName"] = (SessionProperty)(string.IsNullOrWhiteSpace(LobbyParams.SelectedMapSceneName)
                            ? "Unknown"
                            : LobbyParams.SelectedMapSceneName),
                        ["PingMs"] = (SessionProperty)0,
                    },
                };

                var res = await _runner.StartGame(args);
                if (!res.Ok)
                {
                    Debug.LogError($"[LobbyBootstrap] StartGame FAILED: {res.ShutdownReason}");
                    UnityEngine.SceneManagement.SceneManager.LoadScene("ServerBrowserScene");
                    return;
                }

                var si = _runner.SessionInfo;
                if (si != null) { si.IsVisible = true; si.IsOpen = true; }
                Debug.Log($"[LobbyBootstrap] Host READY | Name={si?.Name} Visible={si?.IsVisible} Open={si?.IsOpen}");
            }
            else
            {
                var argsJoin = new StartGameArgs
                {
                    GameMode = GameMode.Client,
                    SessionName = LobbyParams.SessionName,
                    SceneManager = _runner.GetComponent<INetworkSceneManager>(),
                    PlayerCount = LobbyParams.MaxPlayers
                };

                var res = await _runner.StartGame(argsJoin);
                if (!res.Ok)
                {
                    Debug.LogError($"[LobbyBootstrap] Join FAILED: {res.ShutdownReason}");
                    UnityEngine.SceneManagement.SceneManager.LoadScene("ServerBrowserScene");
                    return;
                }

                Debug.Log("[LobbyBootstrap] Client joined host session successfully.");
            }

            if (_runner.IsServer || _runner.IsSharedModeMasterClient)
            { // Host trong ClientServer
                _runner.Spawn(lobbyStatePrefab, Vector3.zero, Quaternion.identity, null);
            }
        }
    }
}
