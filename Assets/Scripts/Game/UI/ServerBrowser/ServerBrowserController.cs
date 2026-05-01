// Assets/Scripts/Game/UI/ServerBrowser/ServerBrowserController.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Fusion.Sockets;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DG.Tweening;
using System.Net.NetworkInformation;

namespace TT.UI
{
    /// <summary>
    /// UI duyệt lobby:
    /// - Tự tìm runner do Bootstrap tạo.
    /// - Join lobby đúng 1 lần (cold connect).
    /// - Refresh = gọi JoinSessionLobby lại.
    /// - Join selected → set LobbyParams rồi load LobbyScene (không tắt runner Shared tại Browser).
    /// </summary>
    public class ServerBrowserController : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Runner (auto-find if null)")]
        [SerializeField] private NetworkRunner runner;

        [Header("Overlay")]
        [SerializeField] private CanvasGroup overlayStatus;
        [SerializeField] private TMP_Text txtStatus;
        [SerializeField] private TMP_Text txtSub;
        [SerializeField] private CanvasGroup panelRoot;
        [SerializeField] private Button btnCancel;   // nút Cancel trên overlay

        [Header("List")]
        [SerializeField] private Transform content;
        [SerializeField] private GameObject serverItemPrefab;
        [SerializeField] private Button btnJoin;
        [SerializeField] private Button btnRefresh;
        [SerializeField] private Button btnBack;

        [Header("Intro Tween")]
        [SerializeField] private RectTransform panelBounds; // kéo Canvas root / safe area vào
        [SerializeField] private float introDuration = 0.35f;
        [SerializeField] private float introOvershoot = 35f;
        [SerializeField] private Ease introEase = Ease.OutCubic;

        private RectTransform _panelRt;
        private Vector2 _panelHome;
        private bool _introPlayed;

        // cache
        private readonly List<GameObject> _pool = new();
        private readonly List<SessionInfo> _sessions = new();
        private SessionInfo _selected;
        private bool _hasSelected;
        private float _overlayElapsed;               // mm:ss
        private Coroutine _overlayTimer;
        private bool _cancelRequested;               // người dùng đã bấm Cancel?
        private int _requestSerial;

        // state
        private bool _gotList;
        private bool _joinedOnce;
        private bool _joiningOnce;

        private float _lastCancelAt;
        private Coroutine _autoBind;

        private enum OverlayState { Connecting, Fetching, Found, Refreshing, Joining, Failed, Disconnecting, Hidden }

        // ---------- Lifecycle ----------
        private void Awake()
        {
            // wire buttons
            if (btnJoin) { btnJoin.onClick.RemoveAllListeners(); btnJoin.onClick.AddListener(OnJoinClick); btnJoin.interactable = false; }
            if (btnRefresh) { btnRefresh.onClick.RemoveAllListeners(); btnRefresh.onClick.AddListener(OnRefreshClick); }
            if (btnBack) { btnBack.onClick.RemoveAllListeners(); btnBack.onClick.AddListener(OnBackClick); }
            if (btnCancel)
            {
                btnCancel.onClick.RemoveAllListeners();
                btnCancel.onClick.AddListener(OnCancelOverlay);
                btnCancel.gameObject.SetActive(false);
            }

            SetOverlay(OverlayState.Connecting, "Connecting to server…", "Please wait");
            SetPanelRoot(false);
            ClearList();

            _panelRt = panelRoot ? panelRoot.transform as RectTransform : null;
            if (_panelRt) _panelHome = _panelRt.anchoredPosition;
        }

        private IEnumerator Start()
        {
            // 1) chờ Bootstrap tạo runner
            while (runner == null)
            {
                runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
                if (runner == null) { SetOverlay(OverlayState.Connecting, "Connecting to Photon…", ""); SetPanelRoot(false); yield return null; }
            }
            runner.AddCallbacks(this);

            // 2) Join lobby một lần (cold connect)
            _ = JoinLobbyOnceAsync();

            SetOverlay(OverlayState.Fetching, "Fetching available lobbies…", "");
            SetPanelRoot(false);

            // 3) Auto-bind dự phòng (nếu runner bị recreate)
            _autoBind = StartCoroutine(AutoBindRunnerLoop());

            // 4) Fallback mở UI sau 5s nếu chưa nhận list
            _gotList = false;
            float t = 0f;
            while (!_gotList && t < 5f) { t += Time.unscaledDeltaTime; yield return null; }
            if (!_gotList) { RenderList(new List<SessionInfo>()); SetOverlay(OverlayState.Found, "No lobbies found", "Press Refresh to try again"); SetPanelRoot(true); }
        }

        private void OnDestroy()
        {
            btnJoin?.onClick.RemoveAllListeners();
            btnRefresh?.onClick.RemoveAllListeners();
            btnBack?.onClick.RemoveAllListeners();
            if (runner) runner.RemoveCallbacks(this);
        }

        // ---------- Core ----------
        private async System.Threading.Tasks.Task JoinLobbyOnceAsync()
        {
            if (_joinedOnce || _joiningOnce) return;
            _joiningOnce = true;

            SetOverlay(OverlayState.Fetching, "Fetching available lobbies…", "");
            try
            {
                _joinedOnce = true;
                Debug.Log("[BrowserCtrl] JoinSessionLobby (once) – cold connect");
                await runner.JoinSessionLobby(SessionLobby.ClientServer);
                Debug.Log("[BrowserCtrl] JoinSessionLobby (once) – lobby=ClientServer");
                Debug.Log("[BrowserCtrl] Joined Photon lobby successfully.");
            }
            catch (Exception ex)
            {
                _joinedOnce = false;
                Debug.LogError($"[BrowserCtrl] JoinSessionLobby FAILED: {ex.Message}\n{ex}");
                SetOverlay(OverlayState.Failed, "Join lobby failed", ex.Message);
                SetPanelRoot(true);
            }
            finally { _joiningOnce = false; }
        }

        private IEnumerator AutoBindRunnerLoop()
        {
            var wait = new WaitForSecondsRealtime(0.5f);
            while (true)
            {
                if (runner == null)
                {
                    var found = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
                    if (found != null)
                    {
                        runner = found;
                        runner.AddCallbacks(this);
                    }
                }
                yield return wait;
            }
        }

        // ---------- Buttons ----------
        private async void OnRefreshClick()
        {
            try
            {
                SetOverlay(OverlayState.Refreshing, "Refreshing list…", "Fetching from server");
                if (runner != null)
                    Debug.Log("[BrowserCtrl] Refresh -> JoinSessionLobby(ClientServer)");

                await runner.JoinSessionLobby(SessionLobby.ClientServer);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Browser] Refresh failed: {e.Message}");
                SetOverlay(OverlayState.Failed, "Refresh failed", e.Message);
            }
            finally
            {
                SetPanelRoot(true);
            }
        }

        private async void OnJoinClick()
        {
            if (Time.unscaledTime - _lastCancelAt < 1.0f)
            {
                SetOverlay(OverlayState.Fetching, "Please wait…", "Cooling down 1s");
                await System.Threading.Tasks.Task.Delay(1000);
            }

            if (!_hasSelected) return;
            var s = _selected;

            // block: phòng đóng/đầy/đang in-game
            string status = s.IsOpen ? "Lobby" : "InGame";
            if (s.Properties != null && s.Properties.TryGetValue("Status", out var stProp))
                status = stProp.ToString();
            bool isFull = s.PlayerCount >= s.MaxPlayers;
            if (!s.IsOpen || status == "InGame" || isFull)
            {
                string reason = !s.IsOpen ? "Room closed" : isFull ? "Room full" : "In Game";
                SetOverlay(OverlayState.Failed, "Join blocked", reason);
                return;
            }

            SetOverlay(OverlayState.Joining, "Joining lobby…", "Handshaking with host…");
            SetPanelRoot(false);

            LobbyParams.Mode = GameMode.Client;
            GameSession.Mode = AppPlayMode.Client;

            LobbyParams.SessionName = s.Name;

            var existing = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
            if (existing)
            {
                try { await existing.Shutdown(false); } catch { }
                try { Destroy(existing.gameObject); } catch { }
            }
            SceneTransitionFader.LoadScene("LobbyScene");

        }

        private void OnBackClick()
        {
            SetOverlay(OverlayState.Disconnecting, "Disconnecting…", "");
            StartCoroutine(Co_BackToMenu());
        }

        private void OnCancelOverlay()
        {
            _lastCancelAt = Time.unscaledTime;
            _ = RefreshAfterAsync(1.0f);
            _cancelRequested = true;
            StopOverlayTimer();
            // Ẩn overlay & mở lại UI
            SetOverlay(OverlayState.Hidden, "", "");
            SetPanelRoot(true);
        }

        private async System.Threading.Tasks.Task RefreshAfterAsync(float delay)
        {
            await System.Threading.Tasks.Task.Delay(System.TimeSpan.FromSeconds(delay));
            try
            {
                SetOverlay(OverlayState.Refreshing, "Refreshing list…", "");
                if (runner != null)
                    await runner.JoinSessionLobby(SessionLobby.ClientServer);
            }
            catch
            {
                // ignore
            }
            finally
            {
                SetPanelRoot(true);
            }
        }


        private void StartOverlayTimer(string label)
        {
            if (_overlayTimer != null) StopCoroutine(_overlayTimer);
            _overlayElapsed = 0f;
            _overlayTimer = StartCoroutine(Co_OverlayTimer(label));
        }

        private void StopOverlayTimer()
        {
            if (_overlayTimer != null) StopCoroutine(_overlayTimer);
            _overlayTimer = null;
        }

        private IEnumerator Co_OverlayTimer(string label)
        {
            while (true)
            {
                _overlayElapsed += Time.unscaledDeltaTime;        // realtime, không dính pause
                int mm = Mathf.FloorToInt(_overlayElapsed / 60f);
                int ss = Mathf.FloorToInt(_overlayElapsed % 60f);
                if (txtSub) txtSub.text = $"{label}  —  {mm:00}:{ss:00}";
                yield return null;
            }
        }

        private IEnumerator Co_BackToMenu()
        {
            // Hiển thị overlay đang ngắt kết nối (bạn đã gọi SetOverlay ở OnBackClick)
            // 1) Tìm runner đang tồn tại (kể cả Inactive)
            var existing = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
            if (existing)
            {
                // 2) Shutdown runner trước khi hủy
                System.Threading.Tasks.Task t = null;
                try { t = existing.Shutdown(false); } catch { }

                // 3) Đợi shutdown hoàn tất (nếu có Task)
                if (t != null)
                {
                    while (!t.IsCompleted)
                        yield return null;
                }

                // 4) Hủy gameObject của runner (nó là DontDestroyOnLoad)
                try { Destroy(existing.gameObject); } catch { }
            }

            // 5) Về MainMenu
            yield return null;
            SceneTransitionFader.LoadScene("MainMenu");
        }


        // ---------- UI helpers ----------
        private void SetOverlay(OverlayState s, string main, string sub)
        {
            if (overlayStatus)
            {
                bool show = s != OverlayState.Hidden;
                overlayStatus.alpha = show ? 1f : 0f;
                overlayStatus.interactable = show;
                overlayStatus.blocksRaycasts = show;
            }
            if (txtStatus) txtStatus.text = main ?? "";
            if (txtSub)
            {
                txtSub.gameObject.SetActive(!string.IsNullOrEmpty(sub));
                txtSub.text = sub ?? "";
            }

            bool canCancel = s == OverlayState.Connecting
                          || s == OverlayState.Fetching
                          || s == OverlayState.Refreshing
                          || s == OverlayState.Joining;

            if (btnCancel) btnCancel.gameObject.SetActive(canCancel);

            // Timer mm:ss trên overlay (realtime)
            if (canCancel) StartOverlayTimer(main);
            else StopOverlayTimer();

            bool interactive = (s == OverlayState.Hidden || s == OverlayState.Found);
            if (btnJoin) btnJoin.interactable = interactive && _hasSelected;
            if (btnRefresh) btnRefresh.interactable = interactive;
            if (btnBack) btnBack.interactable = s != OverlayState.Disconnecting;
        }

        private void SetPanelRoot(bool on)
        {
            if (!panelRoot) return;

            if (!on)
            {
                panelRoot.alpha = 0f;
                panelRoot.interactable = false;
                panelRoot.blocksRaycasts = false;
                return;
            }

            panelRoot.alpha = 1f;

            // Intro tween chỉ chạy 1 lần khi scene vào
            if (!_introPlayed && _panelRt != null)
            {
                _introPlayed = true;

                panelRoot.interactable = false;
                panelRoot.blocksRaycasts = false;

                var b = panelBounds ? panelBounds.rect : new Rect(0, 0, 3840, 2160);
                float offTopY = (b.height * 0.5f) + _panelRt.rect.height + 50f;

                _panelRt.DOKill();
                _panelRt.anchoredPosition = new Vector2(_panelHome.x, offTopY);

                // rơi xuống + overshoot nhẹ
                _panelRt.DOAnchorPos(_panelHome + new Vector2(0f, -introOvershoot), introDuration)
                    .SetEase(introEase)
                    .SetUpdate(true)
                    .OnComplete(() =>
                    {
                        _panelRt.DOAnchorPos(_panelHome, 0.12f).SetEase(Ease.OutSine).SetUpdate(true);
                        panelRoot.interactable = true;
                        panelRoot.blocksRaycasts = true;
                    });

                return;
            }

            panelRoot.interactable = true;
            panelRoot.blocksRaycasts = true;
        }

        private void ClearList()
        {
            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);

            _pool.Clear();
            _sessions.Clear();
            _selected = default;
            _hasSelected = false;
            if (btnJoin) btnJoin.interactable = false;
        }

        private void RenderList(List<SessionInfo> list)
        {
            int count = list?.Count ?? 0;
            int before = content ? content.childCount : -1;
            Debug.Log($"[BrowserCtrl] RenderList: count={count} | beforeChildren={before} | " +
                      $"prefab={(serverItemPrefab ? serverItemPrefab.name : "<null>")} | " +
                      $"content={(content ? content.name : "<null>")}");

            // Clear cũ
            if (content)
            {
                for (int i = content.childCount - 1; i >= 0; i--)
                    Destroy(content.GetChild(i).gameObject);

                _pool.Clear();
            }

            if (serverItemPrefab == null || content == null)
            {
                Debug.LogError("[BrowserCtrl] Missing references: serverItemPrefab or content is NULL → assign in Inspector (ServerBrowserController).");
                SetOverlay(OverlayState.Found, count == 0 ? "No lobbies found" : $"Found {count} (UI missing refs)", "Check prefab & content in Inspector");
                SetPanelRoot(true);
                return;
            }

            if (count > 0)
            {
                foreach (var s in list.OrderByDescending(x => x.IsOpen).ThenBy(x => x.Name))
                {
                    var go = Instantiate(serverItemPrefab, content);
                    _pool.Add(go); 
                    if (!go.activeSelf) go.SetActive(true); // đề phòng prefab bị disable

                    var item = go.GetComponent<ServerCardItem>();
                    if (!item)
                    {
                        Debug.LogWarning($"[BrowserCtrl] Prefab '{serverItemPrefab.name}' thiếu ServerCardItem. Tự AddComponent để tiếp tục.");
                        item = go.AddComponent<ServerCardItem>(); // 🔧 tự cứu để còn thấy UI
                    }

                    try
                    {
                        item.Bind(s, OnSelectSession);
                    }
                    catch (System.Exception ex)
                    {   
                        Debug.LogError($"[BrowserCtrl] item.Bind() lỗi: {ex.Message}\nPrefab={serverItemPrefab.name}");
                    }
                }

                SetOverlay(OverlayState.Found, $"Found {count} lobby(ies)", "");
                StartCoroutine(AutoHideOverlay(3f)); // ẩn sau 3 giây
            }
            else
            {
                SetOverlay(OverlayState.Found, "No lobbies found", "Press Refresh to try again");
                StartCoroutine(AutoHideOverlay(3f)); // ẩn sau 3 giây
            }

            int after = content.childCount;
            Debug.Log($"[BrowserCtrl] RenderList done | afterChildren={after}");
            SetPanelRoot(true);
        }

        private IEnumerator AutoHideOverlay(float delay)
        {
            yield return new WaitForSeconds(delay);

            // Gọi lại SetOverlay để ẩn overlay
            SetOverlay(OverlayState.Hidden, "", "");
            Debug.Log("[BrowserCtrl] Overlay auto-hidden after refresh.");
        }


        private void OnSelectSession(SessionInfo s)
        {
            if (s == null) return;
            _selected = s;
            _hasSelected = true;
            if (btnJoin) btnJoin.interactable = true;
            LobbyParams.SessionName = s.Name;
            LobbyParams.MaxPlayers = s.MaxPlayers;
            LobbyParams.Mode = GameMode.Client;
            Debug.Log($"[BrowserCtrl] Selected lobby: {s.Name} ({s.PlayerCount}/{s.MaxPlayers})");
        }

        // ---------- Fusion callbacks ----------
        void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner r, List<SessionInfo> list)
        {
            _gotList = true;

            var count = list?.Count ?? 0;
            var names = (list == null || list.Count == 0)
                ? "<empty>"
                : string.Join(", ", list.Select(s => $"{s.Name}[open={s.IsOpen},vis={s.IsVisible}]"));

            Debug.Log($"[BrowserCtrl] OnSessionListUpdated: {count} room(s) -> {names}");

            _sessions.Clear();
            if (list != null) _sessions.AddRange(list);

            RenderList(_sessions);
        }

        // Những callback còn lại không dùng cho Browser
        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner r) { }
        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner r, NetDisconnectReason reason)
        {
            SetOverlay(OverlayState.Failed, "Disconnected", reason.ToString());
            SetPanelRoot(true);
        }
        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner r, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner r, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnInput(NetworkRunner r, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner r, PlayerRef player, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner r, ShutdownReason reason) { }
        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner r, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner r, NetAddress remoteAddress, NetConnectFailedReason reason)
        {
            SetOverlay(OverlayState.Failed, "Failed to connect", reason.ToString());
            SetPanelRoot(true);
        }
        void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner r, SimulationMessagePtr message) { }
        public void OnCustomAuthenticationResponse(NetworkRunner r, Dictionary<string, object> data) { }
        public void OnCustomAuthenticationResponse(NetworkRunner r, IReadOnlyDictionary<string, object> data) { }
        void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner r, HostMigrationToken hostMigrationToken) { }
        void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner r, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner r, PlayerRef player, ReliableKey key, float progress) { }
        void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner r) { }
        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner r) { }
        void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
    }
}
