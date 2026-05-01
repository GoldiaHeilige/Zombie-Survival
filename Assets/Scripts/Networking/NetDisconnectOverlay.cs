using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TT; // NetworkTopics + NetDisconnectInfo

namespace TT.UI
{
    /// <summary>
    /// Overlay xuất hiện ngay trong scene khi mất kết nối / host rời.
    /// Lắng nghe Observer topic "net.disconnected", show message rồi auto quay về menu.
    /// </summary>
    public class NetDisconnectOverlay : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private CanvasGroup overlay;      // panel full-screen
        [SerializeField] private TMP_Text labelReason;     // text hiển thị lý do
        [SerializeField] private Button btnReturnNow;      // optional: nút "Back to Menu"

        [Header("Return Config")]
        [SerializeField] private float autoReturnDelay = 4f;

        [Tooltip("Nếu >= 0 thì dùng build index này để load menu.")]
        [SerializeField] private int menuSceneBuildIndex = -1;

        [Tooltip("Nếu buildIndex < 0 thì dùng scene path này.")]
        [SerializeField] private string menuScenePath = "";

        private bool _isShowing;
        private float _countdown;

        private void Awake()
        {
            SetVisible(false, "");

            if (btnReturnNow != null)
                btnReturnNow.onClick.AddListener(ReturnToMenuImmediately);
        }

        private void OnDestroy()
        {
            if (btnReturnNow != null)
                btnReturnNow.onClick.RemoveListener(ReturnToMenuImmediately);
        }

        private void OnEnable()
        {
            Observer.Instance?.AddObserver(NetworkTopics.Disconnected, OnNetDisconnected);
        }

        private void OnDisable()
        {
            Observer.Instance?.RemoveObserver(NetworkTopics.Disconnected, OnNetDisconnected);
        }

        private void Update()
        {
            if (!_isShowing) return;
            if (autoReturnDelay <= 0f) return;

            _countdown -= Time.unscaledDeltaTime;
            if (_countdown <= 0f)
            {
                _isShowing = false;
                LoadMenuScene();
            }
        }

        private void OnNetDisconnected(object data)
        {
            if (_isShowing) return;
            _isShowing = true;

            var info = data is NetDisconnectInfo n ? n : default;

            string msg = info.reasonText;
            if (string.IsNullOrWhiteSpace(msg))
            {
                switch (info.kind)
                {
                    case NetDisconnectKind.HostLeft:
                        msg = "Host has left the game.\nReturning to menu...";
                        break;
                    case NetDisconnectKind.ConnectionLost:
                        msg = "Connection lost.\nReturning to menu...";
                        break;
                    case NetDisconnectKind.Kicked:
                        msg = "You were kicked from the game.\nReturning to menu...";
                        break;
                    default:
                        msg = "Disconnected from server.\nReturning to menu...";
                        break;
                }
            }

            // Reset mọi blocker cho chắc ăn (movement/look/combat...)
            InputBlockerSystem.Add(InputBlocker.Full);

            // Mở chuột để bấm nút hoặc ngồi chờ auto thoát
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            _countdown = autoReturnDelay;
            SetVisible(true, msg);
        }

        private void SetVisible(bool on, string message)
        {
            if (overlay != null)
            {
                overlay.alpha = on ? 1f : 0f;
                overlay.interactable = on;
                overlay.blocksRaycasts = on;
            }

            if (labelReason != null)
                labelReason.text = message ?? "";
        }

        private void ReturnToMenuImmediately()
        {
            if (!_isShowing) return;
            _isShowing = false;
            LoadMenuScene();
        }

        private void LoadMenuScene()
        {
            // Dọn sạch state giữa các trận:
            // - Clear LastGameResult
            // - Destroy các DDOL không nằm trong whitelist (AudioManager, SettingsController, ...)
            // - Reset timeScale + cursor
            SessionCleanup.CleanupAll();   // <— THÊM DÒNG NÀY

            // Clear InputBlocker để sang menu không bị khoá input gameplay
            InputBlockerSystem.Clear();

            if (menuSceneBuildIndex >= 0)
            {
                SceneManager.LoadScene(menuSceneBuildIndex);
                return;
            }

            if (!string.IsNullOrWhiteSpace(menuScenePath))
            {
                int buildIndex = SceneUtility.GetBuildIndexByScenePath(menuScenePath);
                if (buildIndex >= 0)
                {
                    SceneManager.LoadScene(buildIndex);
                }
                else
                {
                    Debug.LogError($"[NetDisconnectOverlay] Menu scene path invalid or not in Build Settings: {menuScenePath}");
                }
            }
            else
            {
                Debug.LogError("[NetDisconnectOverlay] No menu scene configured (buildIndex < 0 and menuScenePath empty).");
            }
        }
    }
}
