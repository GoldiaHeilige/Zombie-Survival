// Assets/Scripts/UI/LobbyController.cs
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

namespace TT.UI
{
    public class LobbyController : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Runner")]
        [SerializeField] private NetworkRunner runner; // auto-find nếu trống

        [Header("Lobby Player Info")]
        [SerializeField] private NetworkObject lobbyPlayerInfoPrefab;

        [Header("Overlay (status)")]
        [SerializeField] private CanvasGroup overlayStatus;   // Overlay_Status
        [SerializeField] private CanvasGroup panelRoot;       // Panel_Root (UI chính)
        [SerializeField] private TMP_Text overlayText;
        [SerializeField] private TMP_Text overlaySubText;
        [SerializeField] private Button btnOverlayCancel;     // NEW: Cancel khi HOSTING / CONNECTING / LEAVING

        [Header("Players Panel (Right)")]
        [SerializeField] private Transform playerListContent; // Viewport/Content
        [SerializeField] private GameObject playerSlotPrefab; // prefab có child "PlayerName" (TMP_Text)
        [SerializeField] private Button btnStart;
        [SerializeField] private Button btnLeave;
        [SerializeField] private TMP_Text titlePlayers;

        [Header("Start Countdown & Overlay")]
        [SerializeField] private CanvasGroup overlayCountdown;   // panel đen mờ
        [SerializeField] private TMP_Text txtCountdown;          // số 5..1
        [SerializeField] private Button btnCancelCountdown;      // chỉ Host mới thấy khi đang countdown

        [Header("Fixed Map Display")]
        [SerializeField] private Image fixedMapPreview;

        // NEW: split texts
        [SerializeField] private TMP_Text fixedMapNameText;   // ví dụ: "Nacht Der Untoten"
        [SerializeField] private TMP_Text fixedMapTitleText;  // ví dụ: "Close-quarters survival"
        [SerializeField] private TMP_Text fixedMapDescText;   // optional

        // (optional) old single text, nếu còn dùng thì giữ
        [SerializeField] private TMP_Text fixedMapInfo;

        [Header("Connect-Complete Tween (Lobby Panels)")]
        [SerializeField] private RectTransform leftPanel;     // panel map (trái)
        [SerializeField] private RectTransform rightPanel;    // panel players (phải)
        [SerializeField] private float introDuration = 0.45f;
        [SerializeField] private float introOvershoot = 24f;
        [SerializeField] private Ease introEase = Ease.OutCubic;

        private Coroutine _pingTick;
        private const float PING_REFRESH_SEC = 5f;

        private Vector2 _leftHome;
        private Vector2 _rightHome;
        private bool _introPrepared;
        private bool _introPlayed;

        private readonly Dictionary<PlayerRef, string> _nameCache = new();
        private readonly HashSet<PlayerRef> _announcedJoining = new();
        private readonly HashSet<PlayerRef> _announcedConnected = new();

        // ---- internal ----
        private readonly List<GameObject> _slotPool = new();
        private readonly List<bool> _occupied = new(); // false = trống, true = có người
        private readonly Dictionary<PlayerRef, int> _seatOfPlayer = new();
        private LobbyState _state;
        private Coroutine _uiTick;

        // Overlay timer mm:ss
        private float _overlayElapsed;
        private Coroutine _overlayTimer;

        // Meta publish
        private string _lastPublishedMap;
        private float _mapPollElapsed;
        private string _lastFixedMapShown;

        private const string PROP_STATUS = "Status";
        private const string PROP_MAP = "MapName";


        private void OnEnable()
        {
            // Ban đầu: hiển thị overlay HOSTING/CONNECTING..., ẩn UI chính
            LobbyPlayerInfo.OnAnyNameChanged += HandleLobbyNameChanged;
            SetOverlay(true, "CONNECTING...", "");
            SetPanelRoot(false);
            PrepareIntroPositions();
            StartCoroutine(Co_HostTimeout());
        }

        private void OnDisable()
        {
            LobbyPlayerInfo.OnAnyNameChanged -= HandleLobbyNameChanged;
        }

        private IEnumerator Start()
        {
            // 1) Lấy runner (an toàn cho bootstrap – ở Lobby BootStrap luôn tạo runner mới)
            while (runner == null)
            {
                runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);
                if (runner == null) yield return null;

/*                float waited = 0f;
                while (runner == null && waited < 10f) { waited += Time.unscaledDeltaTime; yield return null; }
                if (runner == null)
                {
                    SetOverlay(true, "NO RUNNER", "Returning to Menu");
                    yield return new WaitForSecondsRealtime(5f);
                    SceneManager.LoadScene("MainMenu", LoadSceneMode.Single);
                    yield break;
                }*/
            }
            runner.AddCallbacks(this);

            // 2) Dựng sẵn slot theo MaxPlayers
            PrebuildSlots(LobbyParams.MaxPlayers);

            // 3) Nếu là Host/Server → publish meta lần đầu
            if (runner && (runner.GameMode == GameMode.Host || runner.GameMode == GameMode.Server))
            {
                var initialMap = string.IsNullOrWhiteSpace(LobbyParams.SelectedMapSceneName)
                    ? "Gameplay_Map_01"
                    : LobbyParams.SelectedMapSceneName;

                PublishSessionMeta("Lobby", initialMap, true);
                _lastPublishedMap = initialMap;

                string map = LobbyParams.SelectedMapSceneName;
                var data = Resources.Load<MapData>(map);

                if (!data)
                {
                    // fallback load all
                    var all = Resources.LoadAll<MapData>("");
                    foreach (var m in all)
                        if (m.sceneName == map) data = m;
                }

                if (data)
                {
                    if (fixedMapPreview) fixedMapPreview.sprite = data.thumbnail;

                    // TÊN MAP (bạn muốn “map name”)
                    if (fixedMapNameText) fixedMapNameText.text = data.displayName;

                    // TIÊU ĐỀ MAP (tuỳ bạn định nghĩa: tags / subtitle / short title)
                    if (fixedMapTitleText) fixedMapTitleText.text = data.tags;

                    // MÔ TẢ (optional)
                    if (fixedMapDescText) fixedMapDescText.text = data.description;

                    // Nếu bạn vẫn muốn fallback 1 TMP cũ:
                    if (fixedMapInfo) fixedMapInfo.text = $"{data.displayName}\n{data.tags}\n\n{data.description}";
                }
            }

            // 4) Tìm LobbyState
            _state = FindFirstObjectByType<LobbyState>(FindObjectsInactive.Include);
            if (_state == null) _state = FindAnyObjectByType<LobbyState>(FindObjectsInactive.Include);


            // 5) Listeners
            if (btnStart)
            {
                btnStart.onClick.RemoveAllListeners();
                btnStart.onClick.AddListener(OnStartMatch_Click);
                btnStart.interactable = _state && _state.Object && _state.Object.IsValid;
            }
            if (btnCancelCountdown)
            {
                btnCancelCountdown.onClick.RemoveAllListeners();
                btnCancelCountdown.onClick.AddListener(OnCancelCountdown_Click);
            }
            if (btnLeave)
            {
                btnLeave.onClick.RemoveAllListeners();
                btnLeave.onClick.AddListener(OnLeave_Click);
            }
            if (btnOverlayCancel)
            {
                btnOverlayCancel.onClick.RemoveAllListeners();
                // Cancel = Leave ngay tại Lobby (shutdown runner tạm thời và quay về Browser)
                btnOverlayCancel.onClick.AddListener(OnLeave_Click);
            }

            // 6) Fill các player đã có (host local thường đã join)
            foreach (var p in runner.ActivePlayers.OrderBy(p => p.PlayerId))
                SeatPlayer(p, runner);

            UpdateTitle();

            // 7) Tick UI + countdown + poll đổi map
            UpdateButtonsVisibilityAndState();
            RefreshCountdownUI();
            if (_uiTick != null) StopCoroutine(_uiTick);
            _uiTick = StartCoroutine(Co_UITick());

            if (_pingTick != null) StopCoroutine(_pingTick);
            _pingTick = StartCoroutine(Co_PingTick());
        }

        private void OnDestroy()
        {
            if (runner != null) runner.RemoveCallbacks(this);
            btnStart?.onClick.RemoveAllListeners();
            btnLeave?.onClick.RemoveAllListeners();
            btnCancelCountdown?.onClick.RemoveAllListeners();
            btnOverlayCancel?.onClick.RemoveAllListeners();
            if (_pingTick != null) StopCoroutine(_pingTick);
        }

        // ===================== Buttons =====================

        private void OnStartMatch_Click()
        {
            if (!_state)
            {
                _state = FindFirstObjectByType<LobbyState>(FindObjectsInactive.Include);
                if (!_state) { Debug.LogError("[LobbyCtrl] _state NULL"); return; }
            }

            string map = string.IsNullOrWhiteSpace(LobbyParams.SelectedMapSceneName)
                ? "Gameplay_Map_01"
                : LobbyParams.SelectedMapSceneName;

            // khóa join & publish InGame
            PublishSessionMeta("InGame", map, false);

            _state.RPC_RequestStart(5, map);
            Debug.Log($"[LobbyCtrl] Start CLICK → RPC_RequestStart (map='{map}')");
        }

        private void OnCancelCountdown_Click()
        {
            if (!_state) return;
            _state.RPC_RequestCancel(); // chỉ Host (StateAuthority) mới hủy được
            Debug.Log("[LobbyCtrl] Cancel CLICK → RPC_RequestCancel");
            PublishSessionMeta("Lobby", LobbyParams.SelectedMapSceneName, true);
        }

        private void OnLeave_Click()
        {
            Debug.Log("[LobbyCtrl] Leave/Cancel CLICK");
            SetOverlay(true, "LEAVING...", "Disconnecting");
            SetPanelRoot(false);
            StartCoroutine(Co_ShutdownAndReturnToMenuClean());
        }

        private IEnumerator Co_ShutdownAndReturnToMenuClean()
        {
            // Nếu là host: ẩn & khóa phòng trước khi rời để Browser không thấy 1/4 “ảo”
            if (runner && (runner.GameMode == GameMode.Host || runner.GameMode == GameMode.Server))
            {
                try
                {
                    var si = runner.SessionInfo;
                    if (si != null)
                    {
                        si.IsOpen = false;
                        si.IsVisible = false;
                        var updates = new Dictionary<string, SessionProperty>
                        {
                            ["Status"] = (SessionProperty)"Closing",
                            ["MapName"] = (SessionProperty)(LobbyParams.SelectedMapSceneName ?? "")
                        };
                        si.UpdateCustomProperties(updates);
                    }
                }
                catch { /* ignore */ }
            }

            // Gỡ callback + yêu cầu shutdown (gửi gói leave)
            if (runner) { try { runner.RemoveCallbacks(this); } catch { } }
            if (runner) { try { runner.Shutdown(false); } catch { } }

            // ✅ CHỜ thật sự rời phiên để host nhận OnPlayerLeft
            float t = 0f;
            while (runner && runner.IsRunning && t < 3.0f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(0.25f); // đệm thêm cho Photon

            // Dọn mọi NetworkRunner còn sót (Browser + Lobby) để về menu “sạch”
            var allRunners = FindObjectsByType<NetworkRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var r in allRunners)
            {
                if (r == null) continue;
                try { r.RemoveCallbacks(this); } catch { }
                try { r.Shutdown(false); } catch { }
            }
            // chờ 1–2 frame rồi hủy gameObjects
            yield return null; yield return null;
            allRunners = FindObjectsByType<NetworkRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var r in allRunners)
                if (r) Destroy(r.gameObject);
            runner = null;

            // Load Main Menu (đổi tên scene nếu của bạn khác)
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }



        // ===================== UI Tick =====================

        private IEnumerator Co_UITick()
        {
            var wait = new WaitForSeconds(0.1f);
            while (true)
            {
                UpdateButtonsVisibilityAndState();
                RefreshCountdownUI();

                ReconcileSeatsWithRunner();

                // Poll map thay đổi mỗi 0.5s → Host publish lại meta để Browser thấy
                _mapPollElapsed += 0.1f;
                if (_mapPollElapsed >= 0.5f)
                {
                    _mapPollElapsed = 0f;
                    bool isHost = runner && (runner.GameMode == GameMode.Host || runner.GameMode == GameMode.Server);
                }

                if (_state == null) _state = FindFirstObjectByType<LobbyState>(FindObjectsInactive.Include);
                if (_state != null && _state.IsReady)
                {
                    var map = _state.GetMapNameSafe();
                    if (!string.IsNullOrWhiteSpace(map))
                        UpdateFixedMapUI(map);
                }


                yield return wait;
            }
        }

        private void ReconcileSeatsWithRunner()
        {
            if (runner == null) return;
            var alive = new HashSet<PlayerRef>(runner.ActivePlayers);
            var ghosts = new List<PlayerRef>();
            foreach (var kv in _seatOfPlayer)
                if (!alive.Contains(kv.Key)) ghosts.Add(kv.Key);
            foreach (var p in ghosts) UnseatPlayer(p);
        }


        /// Quy tắc hiển thị/enable:
        /// - Start: chỉ Host mới thấy (client không thấy).
        /// - Cancel: chỉ Host mới thấy, và CHỈ khi đang countdown.
        /// - Overlay countdown: tất cả đều thấy khi countdown đang chạy.
        /// - Leave: ai cũng thấy.
        private void UpdateButtonsVisibilityAndState()
        {
            if (_state == null) _state = FindFirstObjectByType<LobbyState>(FindObjectsInactive.Include);
            if (runner == null) runner = FindFirstObjectByType<NetworkRunner>(FindObjectsInactive.Include);

            bool hasState = _state && _state.Object && _state.Object.IsValid;

            bool isHostByMode = runner && (runner.GameMode == GameMode.Host || runner.GameMode == GameMode.Server);
            bool hostHasAuthority = (hasState && _state.Object.HasStateAuthority) || isHostByMode;
            bool countdownOn = hasState && _state.CountdownActive;

            // Start – chỉ Host thấy
            if (btnStart)
            {
                if (btnStart.gameObject.activeSelf != hostHasAuthority)
                    btnStart.gameObject.SetActive(hostHasAuthority);

                btnStart.interactable = hostHasAuthority && !countdownOn;
            }

            // Cancel countdown – chỉ Host thấy khi đang đếm
            if (btnCancelCountdown)
            {
                bool showCancel = hostHasAuthority && countdownOn;
                if (btnCancelCountdown.gameObject.activeSelf != showCancel)
                    btnCancelCountdown.gameObject.SetActive(showCancel);

                btnCancelCountdown.interactable = showCancel;
            }

            // Leave luôn hiển thị/enable
            if (btnLeave)
            {
                if (!btnLeave.gameObject.activeSelf) btnLeave.gameObject.SetActive(true);
                btnLeave.interactable = true;
            }
        }

        private void RefreshCountdownUI()
        {
            bool on = _state && _state.Object && _state.Object.IsValid && _state.CountdownActive;

            if (overlayCountdown)
            {
                if (overlayCountdown.gameObject.activeSelf != on)
                    overlayCountdown.gameObject.SetActive(on);

                overlayCountdown.alpha = on ? 1f : 0f;

                bool hostHasAuthority = _state && _state.Object && _state.Object.HasStateAuthority;
                overlayCountdown.interactable = on && hostHasAuthority;
                overlayCountdown.blocksRaycasts = on;
            }

            if (txtCountdown)
                txtCountdown.text = on ? Mathf.Max(1, _state.SecondsLeft).ToString() : "";
        }

        // ===================== Overlay status helpers =====================

        private void SetOverlay(bool active, string main, string sub = "")
        {
            if (!overlayStatus)
            {
                Debug.LogWarning("[LobbyCtrl] overlayStatus NULL, overlay không hiển thị được!");
                return;
            }

            overlayStatus.alpha = active ? 1 : 0;
            overlayStatus.interactable = active;
            overlayStatus.blocksRaycasts = active;

            if (overlayText) overlayText.text = main ?? "";
            if (overlaySubText)
            {
                overlaySubText.gameObject.SetActive(true);
                overlaySubText.text = sub ?? "";
            }

            // Bật nút Cancel khi overlay đang mở (để hủy kết nối/hosting)
            if (btnOverlayCancel) btnOverlayCancel.gameObject.SetActive(active);

            // Đồng hồ mm:ss
            if (active) StartOverlayTimer(main);
            else StopOverlayTimer();

            Debug.Log($"[LobbyCtrl] Overlay {(active ? "ON" : "OFF")} → {main} / {sub}");
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
            // đếm bằng realtime để không kẹt khi Editor pause
            while (true)
            {
                _overlayElapsed += Time.unscaledDeltaTime;
                int mm = Mathf.FloorToInt(_overlayElapsed / 60f);
                int ss = Mathf.FloorToInt(_overlayElapsed % 60f);
                if (overlaySubText) overlaySubText.text = $"{label}  —  {mm:00}:{ss:00}";
                yield return null;
            }
        }

        private void SetPanelRoot(bool active)
        {
            if (!panelRoot) return;

            panelRoot.alpha = active ? 1 : 0;
            panelRoot.interactable = active;
            panelRoot.blocksRaycasts = active;
        }

        // ===================== Meta Session =====================
        private void PublishSessionMeta(string status, string mapName = null, bool? isOpen = null)
        {
            if (runner == null || runner.SessionInfo == null) return;

            try
            {
                var si = runner.SessionInfo;

                // Các cờ mặc định vẫn set trực tiếp được
                si.IsVisible = true;
                if (isOpen.HasValue) si.IsOpen = isOpen.Value;

                // Giá trị muốn publish
                string map = string.IsNullOrWhiteSpace(mapName)
                    ? (LobbyParams.SelectedMapSceneName ?? "")
                    : mapName;

                // ✅ Fusion v2: cập nhật properties qua UpdateCustomProperties(...)
                var updates = new Dictionary<string, SessionProperty>
                {
                    [PROP_STATUS] = (SessionProperty)(status ?? "Lobby"),
                    [PROP_MAP] = (SessionProperty)map
                };
                si.UpdateCustomProperties(updates);

                Debug.Log($"[LobbyCtrl] Publish meta → Status={status}, Map={map}, IsOpen={si.IsOpen}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbyCtrl] PublishSessionMeta error: {e.Message}");
            }
        }

        // ===================== Slot logic =====================

        private void PrebuildSlots(int count)
        {
            count = Mathf.Clamp(count <= 0 ? 4 : count, 1, 16);

            foreach (Transform c in playerListContent) Destroy(c.gameObject);
            _slotPool.Clear();
            _occupied.Clear();
            _seatOfPlayer.Clear();

            for (int i = 0; i < count; i++)
            {
                var slot = Instantiate(playerSlotPrefab, playerListContent);
                slot.name = $"PlayerSlotItem_{i + 1}";

                // Giữ GO bật để layout/scroll ổn, nhưng tắt component để “ẩn ghế”
                SetUIEnabled(slot, false);

                var txt = slot.transform.Find("PlayerName")?.GetComponent<TMP_Text>();
                if (txt) txt.text = "Empty";

                _slotPool.Add(slot);
                _occupied.Add(false);
            }
        }

        private int GetFreeSeatIndex()
        {
            for (int i = 0; i < _slotPool.Count; i++)
                if (_occupied[i] == false)
                    return i;
            return -1;
        }

        private string GetPlayerDisplayNameFromNetwork(NetworkRunner r, PlayerRef p)
        {
            if (r == null) return null;

            var obj = r.GetPlayerObject(p);
            if (obj == null) return null;

            var info = obj.GetComponent<LobbyPlayerInfo>();
            if (info == null) return null;

            var s = info.DisplayName.ToString();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        private void SeatPlayer(PlayerRef p, NetworkRunner r)
        {
            if (_seatOfPlayer.ContainsKey(p)) return;

            int seat = GetFreeSeatIndex();
            if (seat < 0)
            {
                Debug.LogWarning("[Lobby] No free seat");
                return;
            }

            var go = _slotPool[seat];
            SetUIEnabled(go, true);

            var txt = go.transform.Find("PlayerName")?.GetComponent<TMP_Text>();
            if (txt)
            {
                // Thử lấy tên từ network trước
                string displayName = GetPlayerDisplayNameFromNetwork(r, p);

                // Nếu DisplayName chưa có (RPC chưa về) → fallback
                if (string.IsNullOrEmpty(displayName))
                {
                    if (r != null && p == r.LocalPlayer)
                    {
                        displayName = PlayerProfileManager.Data.playerName ?? $"Player {p.PlayerId}";
                    }
                    else
                    {
                        displayName = $"Player {p.PlayerId}";
                    }

                    var item = go.GetComponent<LobbyPlayerItem>();
                    if (item != null)
                    {
                        item.SetName(txt ? txt.text : displayName);
                        item.SetPingMs(null); // default "-- ms" lúc mới seat
                    }
                }

                txt.text = displayName;
            }

            _seatOfPlayer[p] = seat;
            _occupied[seat] = true;
            UpdateTitle();
        }


        private void UnseatPlayer(PlayerRef p)
        {
            if (!_seatOfPlayer.TryGetValue(p, out var seat)) return;

            var go = _slotPool[seat];
            var txt = go.transform.Find("PlayerName")?.GetComponent<TMP_Text>();
            if (txt) txt.text = "Empty";

            SetUIEnabled(go, false);
            _occupied[seat] = false;
            _seatOfPlayer.Remove(p);

            UpdateTitle();
        }

        private void UpdateTitle()
        {
            if (!titlePlayers) return;
            int current = _occupied.Count(x => x);
            titlePlayers.text = $"Players ({current}/{_slotPool.Count})";
        }

        private void SetUIEnabled(GameObject go, bool on)
        {
            var behaviours = go.GetComponentsInChildren<UnityEngine.Behaviour>(true);
            foreach (var b in behaviours)
            {
                if (b == null) continue;
                b.enabled = on;
            }

            var groups = go.GetComponentsInChildren<CanvasGroup>(true);
            foreach (var g in groups)
            {
                g.alpha = on ? 1f : 0f;
                g.interactable = on;
                g.blocksRaycasts = on;
            }
        }

        private IEnumerator Co_HostTimeout()
        {
            const float TIMEOUT = 50f; // an toàn: 50s
            float t = 0f;
            while (t < TIMEOUT)
            {
                // LocalPlayer đã join → ẩn overlay (ở OnPlayerJoined cũng ẩn)
                if (runner && runner.IsRunning && runner.LocalPlayer != PlayerRef.None)
                    yield break;

                t += Time.unscaledDeltaTime;
                yield return null;
            }

            // Hết thời gian mà chưa join → báo lỗi
            SetOverlay(true, "FAILED TO CONNECT", "Check AppId/NetworkProjectConfig/Firewall");
            Debug.LogError("[Lobby] Host/Join timeout. Kiểm tra: AppIdFusion trong Resources, NetworkProjectConfig, firewall, internet.");
        }

        private void HandleLobbyNameChanged(PlayerRef player, string name)
        {
            if (!string.IsNullOrWhiteSpace(name))
                _nameCache[player] = name;

            // Khi đã có name => coi như connect xong (đủ thân thiện cho lobby)
            if (!_announcedConnected.Contains(player))
            {
                LobbyEventFeed.Push($"{name} has connected.");
                _announcedConnected.Add(player);
            }

            RefreshLobbyList();
        }

        private void RefreshLobbyList()
        {
            // disable toàn bộ UI slot
            for (int i = 0; i < _slotPool.Count; i++)
                SetUIEnabled(_slotPool[i], false);

            _seatOfPlayer.Clear();
            for (int i = 0; i < _occupied.Count; i++)
                _occupied[i] = false;

            // assign slot lại từ đầu
            foreach (var p in runner.ActivePlayers.OrderBy(p => p.PlayerId))
            {
                SeatPlayer(p, runner);
            }

            UpdateTitle();
        }

        private void UpdateFixedMapUI(string mapSceneName)
        {
            if (string.IsNullOrWhiteSpace(mapSceneName)) return;
            if (_lastFixedMapShown == mapSceneName) return;
            _lastFixedMapShown = mapSceneName;

            MapData data = Resources.Load<MapData>(mapSceneName);
            if (!data)
            {
                // fallback load all (nếu Resources path không match)
                var all = Resources.LoadAll<MapData>("");
                foreach (var m in all)
                    if (m != null && m.sceneName == mapSceneName) { data = m; break; }
            }

            if (!data)
            {
                Debug.LogWarning($"[LobbyCtrl] MapData not found for sceneName='{mapSceneName}'");
                if (fixedMapInfo) fixedMapInfo.text = mapSceneName;
                return;
            }

            if (fixedMapPreview) fixedMapPreview.sprite = data.thumbnail;

            if (fixedMapNameText) fixedMapNameText.text = data.displayName;
            if (fixedMapTitleText) fixedMapTitleText.text = data.tags;
            if (fixedMapDescText) fixedMapDescText.text = data.description;

            // optional fallback
            if (fixedMapInfo) fixedMapInfo.text = $"{data.displayName}\n{data.tags}\n\n{data.description}";

            Debug.Log($"[LobbyCtrl] Fixed map UI updated -> {data.displayName} ({mapSceneName})");
        }

        private void PrepareIntroPositions()
        {
            if (_introPrepared) return;
            _introPrepared = true;

            if (panelRoot)
            {
                // Auto bind nếu chưa kéo trong Inspector:
                if (!leftPanel || !rightPanel)
                {
                    var children = new List<RectTransform>();
                    var rootRt = panelRoot.transform as RectTransform;

                    for (int i = 0; i < panelRoot.transform.childCount; i++)
                    {
                        var rt = panelRoot.transform.GetChild(i) as RectTransform;
                        if (rt) children.Add(rt);
                    }

                    if (children.Count >= 2)
                    {
                        var a = children[0];
                        var b = children[1];
                        if (a.anchoredPosition.x <= b.anchoredPosition.x)
                        {
                            leftPanel = a;
                            rightPanel = b;
                        }
                        else
                        {
                            leftPanel = b;
                            rightPanel = a;
                        }
                    }
                }
            }

            if (leftPanel) _leftHome = leftPanel.anchoredPosition;
            if (rightPanel) _rightHome = rightPanel.anchoredPosition;

            // Đẩy ra ngoài màn hình (chỉ set vị trí, không tween)
            if (leftPanel)
                leftPanel.anchoredPosition = _leftHome + new Vector2(-leftPanel.rect.width - 2000f, 0f);

            if (rightPanel)
                rightPanel.anchoredPosition = _rightHome + new Vector2(rightPanel.rect.width + 2000f, 0f);
        }

        private void PlayIntroTween()
        {
            if (_introPlayed) return;
            _introPlayed = true;

            // Kill tween cũ (nếu có)
            if (leftPanel) leftPanel.DOKill();
            if (rightPanel) rightPanel.DOKill();

            int done = 0;
            void OneDone()
            {
                done++;
                if (done >= 2 && panelRoot)
                {
                    // xong tween thì mới cho tương tác
                    panelRoot.interactable = true;
                    panelRoot.blocksRaycasts = true;
                }
            }

            // Trong lúc tween: show nhưng khóa input
            if (panelRoot)
            {
                panelRoot.interactable = false;
                panelRoot.blocksRaycasts = false;
            }

            if (leftPanel)
            {
                leftPanel
                    .DOAnchorPos(_leftHome + new Vector2(introOvershoot, 0f), introDuration)
                    .SetEase(introEase)
                    .OnComplete(() =>
                    {
                        leftPanel.DOAnchorPos(_leftHome, 0.12f).SetEase(Ease.OutSine);
                        OneDone();
                    });
            }
            else OneDone();

            if (rightPanel)
            {
                rightPanel
                    .DOAnchorPos(_rightHome + new Vector2(-introOvershoot, 0f), introDuration)
                    .SetEase(introEase)
                    .OnComplete(() =>
                    {
                        rightPanel.DOAnchorPos(_rightHome, 0.12f).SetEase(Ease.OutSine);
                        OneDone();
                    });
            }
            else OneDone();
        }

        private IEnumerator Co_PingTick()
        {
            var wait = new WaitForSecondsRealtime(PING_REFRESH_SEC);

            while (true)
            {
                if (runner != null && runner.IsRunning)
                {
                    foreach (var kv in _seatOfPlayer)
                    {
                        var player = kv.Key;
                        var seat = kv.Value;

                        if (seat < 0 || seat >= _slotPool.Count) continue;

                        var go = _slotPool[seat];
                        var item = go ? go.GetComponent<LobbyPlayerItem>() : null;
                        if (item == null) continue;

                        int? ping = TryGetPingMs(runner, player);
                        item.SetPingMs(ping);
                    }
                }

                yield return wait;
            }
        }

        private static int? TryGetPingMs(NetworkRunner r, PlayerRef p)
        {
            if (r == null) return null;

            try
            {
                // 1) Thử: runner.GetPlayerRtt(PlayerRef) -> float/double (seconds)
                var m = r.GetType().GetMethod("GetPlayerRtt", new[] { typeof(PlayerRef) });
                if (m != null)
                {
                    object v = m.Invoke(r, new object[] { p });
                    if (v is float f) return Mathf.Clamp(Mathf.RoundToInt(f * 1000f), 0, 9999);
                    if (v is double d) return Mathf.Clamp((int)Math.Round(d * 1000.0), 0, 9999);
                }

                // 2) Thử: GetPlayerRTT / GetPlayerRttInSeconds (nếu version khác)
                var m2 = r.GetType().GetMethods()
                    .FirstOrDefault(x => x.Name.IndexOf("Rtt", StringComparison.OrdinalIgnoreCase) >= 0
                                      && x.GetParameters().Length == 1
                                      && x.GetParameters()[0].ParameterType == typeof(PlayerRef));
                if (m2 != null)
                {
                    object v = m2.Invoke(r, new object[] { p });
                    if (v is float f2) return Mathf.Clamp(Mathf.RoundToInt(f2 * 1000f), 0, 9999);
                    if (v is double d2) return Mathf.Clamp((int)Math.Round(d2 * 1000.0), 0, 9999);
                }
            }
            catch { /* ignore */ }

            return null;
        }


        // ===================== INetworkRunnerCallbacks =====================

        void INetworkRunnerCallbacks.OnPlayerJoined(NetworkRunner r, PlayerRef player)
        {
            if (runner != r) runner = r;

            string displayName = GetPlayerDisplayNameFromNetwork(runner, player);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                // local thì lấy profile name cho đẹp
                if (runner != null && player == runner.LocalPlayer)
                    displayName = PlayerProfileManager.Data.playerName ?? $"Player {player.PlayerId}";
                else
                    displayName = $"Player {player.PlayerId}";
            }

            _nameCache[player] = displayName;

            // “is connecting”
            // ✅ Generic "joining" (không dùng tên để tránh fallback "Player X")
            // ✅ Chỉ Host/Server bắn để client mới join không spam "host is joining..."
            bool isHostOrServer = runner != null && (runner.GameMode == GameMode.Host || runner.GameMode == GameMode.Server);

            if (isHostOrServer && player != runner.LocalPlayer && _announcedJoining.Add(player))
            {
                LobbyEventFeed.Push("Someone is joining the lobby...");
                // hoặc: "Another player is joining..."
            }



            // NEW: Host/Server spawn LobbyPlayerInfo cho player này
            if (runner != null && (runner.IsServer || runner.IsSharedModeMasterClient))
            {
                if (lobbyPlayerInfoPrefab != null)
                {
                    // Spawn prefab NetworkObject, trả về NetworkObject
                    NetworkObject obj = runner.Spawn(lobbyPlayerInfoPrefab, Vector3.zero, Quaternion.identity, player);

                    // Gán làm PlayerObject cho player này
                    runner.SetPlayerObject(player, obj);
                }
                else
                {
                    Debug.LogWarning("[LobbyCtrl] lobbyPlayerInfoPrefab chưa gán!");
                }
            }

            // Cập nhật UI slot
            RefreshLobbyList();

            if (player == runner.LocalPlayer)
            {
                Debug.Log("[LobbyCtrl] Local player joined → hide overlay and tween UI in");

                // đảm bảo đã prepare vị trí ngoài màn hình
                PrepareIntroPositions();

                // tắt overlay rồi mới cho nhìn thấy tween (nếu overlay đang che)
                SetOverlay(false, "", "");

                // bật root lên rồi tween 2 panel vào
                SetPanelRoot(true);
                PlayIntroTween();
            }


            if (runner && (runner.GameMode == GameMode.Host || runner.GameMode == GameMode.Server))
            {
                PublishSessionMeta("Lobby", LobbyParams.SelectedMapSceneName, true);
            }
        }

        void INetworkRunnerCallbacks.OnPlayerLeft(NetworkRunner r, PlayerRef player)
        {
            if (runner != r) runner = r;

            string name = _nameCache.TryGetValue(player, out var n) && !string.IsNullOrWhiteSpace(n)
    ? n
    : $"Player {player.PlayerId}";

            LobbyEventFeed.Push($"{name} left the lobby.");
            _nameCache.Remove(player);
            _announcedConnected.Remove(player);
            _announcedJoining.Remove(player);

            // Remove khỏi UI
            RefreshLobbyList();

            // Nếu Host đang đếm → hủy countdown
            if (_state && _state.Object && _state.Object.HasStateAuthority && _state.CountdownActive)
            {
                _state.RPC_RequestCancel();
                Debug.Log($"[LobbyCtrl] Player {player.PlayerId} left → Host cancels countdown");
            }
        }

        private IEnumerator Co_ReturnToBrowserAfterDelay()
        {
            yield return new WaitForSecondsRealtime(1.5f);

            if (runner)
            {
                try { runner.RemoveCallbacks(this); } catch { }
                try { runner.Shutdown(false); } catch { }

                yield return new WaitForSecondsRealtime(0.1f);

                try { if (runner) Destroy(runner.gameObject); } catch { }
                runner = null;
            }

            SceneManager.LoadScene("ServerBrowserScene", LoadSceneMode.Single);
        }

        void INetworkRunnerCallbacks.OnConnectedToServer(NetworkRunner r) { }

        void INetworkRunnerCallbacks.OnDisconnectedFromServer(NetworkRunner r, NetDisconnectReason reason)
        {
            Debug.LogWarning($"[Lobby] Disconnected from server: {reason}");
            SetOverlay(true, "HOST LEFT", reason.ToString());
            SetPanelRoot(false);
            StartCoroutine(Co_ReturnToBrowserAfterDelay());
        }

        void INetworkRunnerCallbacks.OnConnectRequest(NetworkRunner r, NetworkRunnerCallbackArgs.ConnectRequest req, byte[] token) { }
        void INetworkRunnerCallbacks.OnConnectFailed(NetworkRunner r, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        void INetworkRunnerCallbacks.OnUserSimulationMessage(NetworkRunner r, SimulationMessagePtr message) { }
        void INetworkRunnerCallbacks.OnSessionListUpdated(NetworkRunner r, List<SessionInfo> sessionList) { }

        void INetworkRunnerCallbacks.OnShutdown(NetworkRunner r, ShutdownReason reason)
        {
            Debug.Log($"[Lobby] Runner shutdown: {reason}");
            // Hiện overlay để người chơi thấy trạng thái trước khi quay lại
            string msg = (runner != null && (runner.GameMode == GameMode.Host || runner.GameMode == GameMode.Server))
                ? "SESSION CLOSED"
                : "DISCONNECTED";
            SetOverlay(true, msg, reason.ToString());
            SetPanelRoot(false);
            StartCoroutine(Co_ReturnToBrowserAfterDelay());
        }

        void INetworkRunnerCallbacks.OnHostMigration(NetworkRunner r, HostMigrationToken token) { }
        void INetworkRunnerCallbacks.OnReliableDataReceived(NetworkRunner r, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        void INetworkRunnerCallbacks.OnReliableDataProgress(NetworkRunner r, PlayerRef player, ReliableKey key, float progress) { }
        void INetworkRunnerCallbacks.OnSceneLoadStart(NetworkRunner r) { }
        void INetworkRunnerCallbacks.OnSceneLoadDone(NetworkRunner r) { }
        void INetworkRunnerCallbacks.OnObjectExitAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnObjectEnterAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
        void INetworkRunnerCallbacks.OnInput(NetworkRunner r, NetworkInput input) { }
        void INetworkRunnerCallbacks.OnInputMissing(NetworkRunner r, PlayerRef player, NetworkInput input) { }

        public void OnCustomAuthenticationResponse(NetworkRunner r, Dictionary<string, object> data) { }
        public void OnCustomAuthenticationResponse(NetworkRunner r, IReadOnlyDictionary<string, object> data) { }
    }
}
