using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Fusion;
using DG.Tweening;

namespace TT.UI
{
    public class MenuController : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private GameObject panelHost;

        [Header("Overlay (Loading)")]
        [SerializeField] private CanvasGroup overlayConnecting;
        [SerializeField] private TMP_Text labelStatus;

        [Header("Main Buttons Row")]
        [SerializeField] private Button btnPlaySolo;
        [SerializeField] private Button btnHost;
        [SerializeField] private Button btnJoin;
        [SerializeField] private Button btnProfile;
        [SerializeField] private Button btnQuit;

        [Header("Solo Panel")]
        [SerializeField] private GameObject panelSolo;
        [SerializeField] private Button btnSoloStart;
        [SerializeField] private Button btnSoloCancel;
        [SerializeField] private MapSelectionUI soloMapSelectionUI;

        [Header("Host Panel")]
        [SerializeField] private TMP_InputField inputRoomName;
        [SerializeField] private TMP_Text labelPlayerCount;
        [SerializeField] private Button btnPlayerMinus;
        [SerializeField] private Button btnPlayerPlus;
        [SerializeField] private MapSelectionUI hostMapSelectionUI;

        private int hostPlayerCount = 2;   // default 2

        [SerializeField] private Button btnStartHost;
        [SerializeField] private Button btnBackFromHost;

        [Header("Tutorial Panel")]
        [SerializeField] private Button btnTutorial;
        [SerializeField] private CanvasGroup cgTutorial;
        [SerializeField] private TutorialTabCycler tutorialTabCycler;
        [SerializeField] private Button btnBackFromTutorial;

        // ===== Profile OVERLAY (NEW) =====
        [Header("Profile Overlay")]
        [SerializeField] private GameObject panelProfile;      // Root overlay (enable/disable)
        [SerializeField] private TMP_Text labelEditName;        // "Edit Name" (tùy chọn để set text)
        [SerializeField] private TMP_InputField inputDisplayName;
        [SerializeField] private Button btnProfileApply;
        [SerializeField] private Button btnProfileCancel;

        [Header("Setting Panel")]
        [SerializeField] private Button btnSettings;
        [SerializeField] private CanvasGroup cgSettings;

        [Header("Panels (CanvasGroup)")]
        [SerializeField] private CanvasGroup cgSolo;
        [SerializeField] private CanvasGroup cgHost;
        [SerializeField] private CanvasGroup cgProfile;

        [Header("Scenes")]
        [SerializeField] private string lobbyScenePath = "Assets/Scenes/LobbyScene.unity";
        [SerializeField] private string serverBrowserScenePath = "Assets/Scenes/ServerBrowserScene.unity";

        private const string PREF_KEY_NAME = "player_display_name";
        private const int MIN_MP_PLAYERS = 2;
        private const int MAX_MP_PLAYERS = 4;

        [Header("Panel Tween")]
        [SerializeField] private RectTransform panelBounds; // thường = RectTransform của panelRoot / Canvas root
        [SerializeField] private float enterDuration = 0.35f;
        [SerializeField] private float exitDuration = 0.30f;
        [SerializeField] private float overshootPx = 40f;   // nhún/overshoot (px trong canvas space)
        [SerializeField] private Ease enterEase = Ease.OutCubic;
        [SerializeField] private Ease settleEase = Ease.OutBack;
        [SerializeField] private Ease exitEase = Ease.InCubic;

        private CanvasGroup _current;
        private Tween _transitionTween;
        private Vector2 _homeSolo;
        private Vector2 _homeHost;
        private Vector2 _homeProfile;
        private Vector2 _homeSettings;
        private Vector2 _homeTutorial;

        private RectTransform Rt(CanvasGroup cg) => cg ? cg.transform as RectTransform : null;


        private void Awake()
        {
            // Main row
            btnPlaySolo?.onClick.AddListener(ShowSoloPanel);
            btnHost?.onClick.AddListener(ShowHostPanel);
            btnJoin?.onClick.AddListener(OpenServerBrowser);
            btnProfile?.onClick.AddListener(OpenProfile);
            btnQuit?.onClick.AddListener(OnQuit);

            // Host
            btnStartHost?.onClick.AddListener(StartHost);
            btnBackFromHost?.onClick.AddListener(HideCurrentPanel);
            btnBackFromTutorial?.onClick.AddListener(() =>
            {
                tutorialTabCycler?.ResetToStart();
                HideCurrentPanel();
            });


            if (btnPlayerMinus) btnPlayerMinus.onClick.AddListener(() => ChangePlayerCount(-1));
            if (btnPlayerPlus) btnPlayerPlus.onClick.AddListener(() => ChangePlayerCount(1));

            // Settings overlay
            btnProfileApply?.onClick.AddListener(ApplyProfile);
            btnProfileCancel?.onClick.AddListener(HideCurrentPanel);

            if (btnSoloStart) btnSoloStart.onClick.AddListener(StartSoloGame);
            if (btnSoloCancel) btnSoloCancel.onClick.AddListener(HideCurrentPanel);

            btnSettings.onClick.AddListener(() =>
            {
                ShowOnly(cgSettings);
            });

            btnTutorial?.onClick.AddListener(() =>
            {
                ShowOnly(cgTutorial);
                tutorialTabCycler?.ResetToStart(); // optional
            });

            HideSubPanels();
            SetOverlay(false, "");
            CloseProfile(); // đảm bảo overlay settings tắt khi vào menu

            if (inputDisplayName) inputDisplayName.text = PlayerProfileManager.Data.playerName ?? "";
            if (labelEditName) labelEditName.text = "Edit Name";

            hostPlayerCount = 2;
            if (labelPlayerCount) labelPlayerCount.text = $"{hostPlayerCount}";

            // Cache "home" anchored positions (điểm cố định) để tween không bị lệch
            if (Rt(cgSolo)) _homeSolo = Rt(cgSolo).anchoredPosition;
            if (Rt(cgHost)) _homeHost = Rt(cgHost).anchoredPosition;
            if (Rt(cgProfile)) _homeProfile = Rt(cgProfile).anchoredPosition;
            if (Rt(cgSettings)) _homeSettings = Rt(cgSettings).anchoredPosition;
            if (Rt(cgTutorial)) _homeTutorial = Rt(cgTutorial).anchoredPosition;
        }

        private void OnDestroy()
        {
            btnPlaySolo?.onClick.RemoveAllListeners();
            btnHost?.onClick.RemoveAllListeners();
            btnJoin?.onClick.RemoveAllListeners();
            btnProfile?.onClick.RemoveAllListeners();
            btnQuit?.onClick.RemoveAllListeners();

            btnStartHost?.onClick.RemoveAllListeners();
            btnBackFromHost?.onClick.RemoveAllListeners();

            btnProfileApply?.onClick.RemoveAllListeners();
            btnProfileCancel?.onClick.RemoveAllListeners();
        }

        // ---------- UI helpers ----------
        private void ShowHostPanel() => ShowOnly(cgHost);
        private void OpenServerBrowser()
        {
            var buildIndex = SceneUtility.GetBuildIndexByScenePath(serverBrowserScenePath);
            if (buildIndex >= 0) SceneTransitionFader.LoadScene(buildIndex);
            else Debug.LogError($"[Menu] Scene path không hợp lệ hoặc chưa add Build Settings: {serverBrowserScenePath}");
        }

        private void HideSubPanels()
        {
            SetCG(cgSolo, false);
            SetCG(cgHost, false);
            SetCG(cgProfile, false);
            SetCG(cgSettings, false);
            SetCG(cgTutorial, false);
        }

        private void SetOverlay(bool on, string status)
        {
            if (overlayConnecting)
            {
                overlayConnecting.alpha = on ? 1f : 0f;
                overlayConnecting.interactable = on;
                overlayConnecting.blocksRaycasts = on;
            }
            if (labelStatus) labelStatus.text = status ?? "";
        }

        // ---------- Profile overlay (NEW) ----------
        private void OpenProfile()
        {
            ShowOnly(cgProfile);
            if (inputDisplayName) inputDisplayName.text = PlayerProfileManager.Data.playerName ?? "";
            if (labelEditName) labelEditName.text = "Edit Name";
        }

        private void CloseProfile()
        {
            SetCG(cgProfile, false);
        }

        private void ApplyProfile()
        {
            var name = inputDisplayName ? inputDisplayName.text.Trim() : "";
            if (!string.IsNullOrEmpty(name))
            {
                // Cắt ngắn nếu cần
                if (name.Length > 20) name = name.Substring(0, 20);
                // LƯU qua PlayerProfileManager
                PlayerProfileManager.Save(name);
            }
            HideCurrentPanel();
        }

        // ---------- Buttons ----------
        private void OnPlaySolo()
        {
            GameSession.Mode = AppPlayMode.Single;
            GameSession.SelectedMapSceneName = string.IsNullOrWhiteSpace(LobbyParams.SelectedMapSceneName)
                ? "SampleScene"
                : LobbyParams.SelectedMapSceneName;

            GameSession.MaxPlayers = 1;

            // Vào thẳng map (single không đi qua Lobby)
            var buildIndex = SceneUtility.GetBuildIndexByScenePath(GameSession.SelectedMapSceneName);
            if (buildIndex >= 0) SceneManager.LoadScene(buildIndex);
            else
            {
                // Fallback: nếu map chưa add Build Settings → báo & thôi
                Debug.LogError($"[Menu] Map scene chưa có trong Build Settings: {GameSession.SelectedMapSceneName}");
            }
        }


        private void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------- Host / Join ----------
        private void StartHost()
        {
            // 1) Bắt buộc có tên phòng
            string room = inputRoomName ? inputRoomName.text.Trim() : "";
            if (string.IsNullOrWhiteSpace(room))
            {
                Debug.LogWarning("[Menu] Lobby name is required.");
                SetOverlay(false, "");
                return;
            }

            // 2) Use selected number from ± buttons
            int maxPlayers = hostPlayerCount;

            // Ensure map is selected
            string selectedMap = LobbyParams.SelectedMapSceneName;
            if (string.IsNullOrWhiteSpace(selectedMap))
            {
                Debug.LogWarning("[Menu] Map not selected — using default SampleScene");
                selectedMap = "SampleScene";
            }

            // WRITE TO GAMESESSION
            GameSession.Mode = AppPlayMode.Host;
            GameSession.MaxPlayers = maxPlayers;
            GameSession.SelectedMapSceneName = selectedMap;

            // WRITE TO LOBBY PARAMS
            LobbyParams.Mode = GameMode.Host;
            LobbyParams.SessionName = room;
            LobbyParams.MaxPlayers = maxPlayers;
            LobbyParams.SelectedMapSceneName = selectedMap;

            SetOverlay(true, $"Loading Lobby...\nRoom: {room} | Max: {maxPlayers} | Map: {selectedMap}");
            LoadLobbyScene();

        }

        private void ShowSoloPanel() => ShowOnly(cgSolo);

        private void StartSoloGame()
        {
            var selectedScene = LobbyParams.SelectedMapSceneName;
            if (string.IsNullOrWhiteSpace(selectedScene))
                selectedScene = "SampleScene";

            GameSession.Mode = AppPlayMode.Single;
            GameSession.MaxPlayers = 1;
            GameSession.SelectedMapSceneName = selectedScene;

            // ✅ set display name thật (không phải scene name)
            var display = soloMapSelectionUI ? soloMapSelectionUI.GetSelectedMapDisplayName() : null;
            GameSession.SelectedMapDisplayName = string.IsNullOrWhiteSpace(display) ? selectedScene : display;

            int buildIndex = SceneUtility.GetBuildIndexByScenePath(selectedScene);
            if (buildIndex >= 0) SceneTransitionFader.LoadScene(buildIndex);
            else Debug.LogError($"[Menu] Map {selectedScene} không có trong Build Settings.");
        }


        private void ChangePlayerCount(int delta)
        {
            hostPlayerCount = Mathf.Clamp(hostPlayerCount + delta, 2, 4);
            if (labelPlayerCount) labelPlayerCount.text = hostPlayerCount.ToString();
        }

        private void LoadLobbyScene()
        {
            var buildIndex = SceneUtility.GetBuildIndexByScenePath(lobbyScenePath);
            if (buildIndex >= 0) SceneTransitionFader.LoadScene(buildIndex);
            else { Debug.LogError($"[Menu] Scene path không hợp lệ: {lobbyScenePath}"); SetOverlay(false, ""); }
        }

        private void SetCG(CanvasGroup cg, bool on)
        {
            if (!cg) return;
            cg.alpha = on ? 1f : 0f;
            cg.interactable = on;
            cg.blocksRaycasts = on;
        }

        private void ShowOnly(CanvasGroup target)
        {
            if (!target) return;
            if (_current == target) return;

            KillTransition();

            var nextRt = Rt(target);
            if (!nextRt) return;

            // Nếu chưa có current (lần đầu)
            if (_current == null || !Rt(_current))
            {
                HideSubPanels();                 // tắt alpha/raycast hết
                SetCG(target, true);             // bật target
                nextRt.anchoredPosition = GetOffRight(nextRt);

                // bay vào + overshoot nhẹ
                Vector2 home = GetHome(target);

                _transitionTween = nextRt.DOAnchorPos(home + new Vector2(-overshootPx, 0f), enterDuration)
                    .SetEase(enterEase)
                    .OnComplete(() =>
                    {
                        nextRt.DOAnchorPos(home, 0.12f).SetEase(Ease.OutSine);
                    });

                _current = target;
                return;
            }

            var cur = _current;
            var curRt = Rt(cur);
            Vector2 curHome = GetHome(cur);
            Vector2 nextHome = GetHome(target);
            curRt.anchoredPosition = curHome;


            // chuẩn bị next: bật raycast/alpha nhưng đặt ở ngoài phải
            SetCG(target, true);
            nextRt.anchoredPosition = GetOffRight(nextRt);

            // current: nhún lên 1 chút rồi rơi xuống
            Vector2 offBottom = GetOffBottom(curRt);

            Sequence seq = DOTween.Sequence();

            seq.Append(curRt.DOAnchorPos(curHome + new Vector2(0f, overshootPx), 0.12f).SetEase(Ease.OutSine));
            seq.Append(curRt.DOAnchorPos(offBottom, exitDuration).SetEase(exitEase));
            seq.AppendCallback(() =>
            {
                // tắt hẳn current sau khi rơi xong
                SetCG(cur, false);
                // reset pos current về home để lần sau mở lại đúng chỗ (quan trọng)
                curRt.anchoredPosition = curHome;
            });

            // next: bay vào + overshoot nhẹ rồi settle
            seq.Join(nextRt.DOAnchorPos(nextHome + new Vector2(-overshootPx, 0f), enterDuration).SetEase(enterEase));
            seq.Append(nextRt.DOAnchorPos(nextHome, 0.14f).SetEase(Ease.OutSine));

            _transitionTween = seq;
            _current = target;
        }


        private Vector2 GetOffRight(RectTransform panel)
        {
            var b = panelBounds ? panelBounds.rect : new Rect(0, 0, 3840, 2160);
            // đưa panel ra ngoài phải: x = halfWidth + panelWidth
            float x = (b.width * 0.75f) + panel.rect.width + 50f;
            return new Vector2(x, panel.anchoredPosition.y);
        }

        private Vector2 GetOffBottom(RectTransform panel)
        {
            var b = panelBounds ? panelBounds.rect : new Rect(0, 0, 3840, 2160);
            // đưa panel xuống dưới: y = -halfHeight - panelHeight
            float y = -(b.height * 0.75f) - panel.rect.height - 50f;
            return new Vector2(panel.anchoredPosition.x, y);
        }

        private void KillTransition()
        {
            _transitionTween?.Kill();
            _transitionTween = null;

            // kill tween riêng trên từng panel để khỏi “kẹt”
            Rt(cgSolo)?.DOKill();
            Rt(cgHost)?.DOKill();
            Rt(cgProfile)?.DOKill();
            Rt(cgSettings)?.DOKill();
            Rt(cgTutorial)?.DOKill();
        }

        private Vector2 GetHome(CanvasGroup cg)
        {
            if (cg == cgSolo) return _homeSolo;
            if (cg == cgHost) return _homeHost;
            if (cg == cgProfile) return _homeProfile;
            if (cg == cgSettings) return _homeSettings;
            if (cg == cgTutorial) return _homeTutorial;
            return Vector2.zero;
        }
        private void HideCurrentPanel()
        {
            if (_current == null) return;

            KillTransition();

            var cur = _current;
            var curRt = Rt(cur);

            if (!curRt)
            {
                SetCG(cur, false);
                _current = null;
                return;
            }

            Vector2 home = GetHome(cur);
            Vector2 offBottom = GetOffBottom(curRt);

            Sequence seq = DOTween.Sequence();
            seq.Append(curRt.DOAnchorPos(home + new Vector2(0f, overshootPx), 0.12f).SetEase(Ease.OutSine));
            seq.Append(curRt.DOAnchorPos(offBottom, exitDuration).SetEase(exitEase));
            seq.AppendCallback(() =>
            {
                SetCG(cur, false);
                curRt.anchoredPosition = home; // reset để lần sau mở lại đúng chỗ
                _current = null;
            });

            _transitionTween = seq;
        }

        public void HidePanelFromExternal()
        {
            HideCurrentPanel();
        }

    }
}