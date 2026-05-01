using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Fusion;
using System.Collections;
using TT.UI;
using UnityEngine.InputSystem;

public class PauseMenuController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private CanvasGroup root;
    [SerializeField] private Button btnResume;
    [SerializeField] private Button btnSettings; // Thêm nút Settings
    [SerializeField] private Button btnLeave;

    private bool _isOpen;
    private bool _pauseEdgeConsumed = false;

    private bool _inputReady;
    private FusionInputProvider _fusion;
    private InputHub _hub;

    private bool _isExiting = false;

    IEnumerator Start()
    {
        // Chờ input thật sự sẵn sàng (không giới hạn 240 frame nữa)
        while (!IsInputReady())
            yield return null;

        _inputReady = true;
        Debug.Log($"[PauseMenu] Input ready. Mode={GameSession.Mode}");
    }


    void Awake()
    {
        HideImmediate();

        Debug.Log("PauseMenu");
        btnResume.onClick.AddListener(OnResumePressed);
        btnSettings.onClick.AddListener(OnSettingsPressed); // Thêm listener
        btnLeave.onClick.AddListener(OnLeavePressed);
    }

    void Update()
    {
        if (_isExiting) return; // Nếu đang thoát thì không làm gì cả
        if (GameOverManager.Instance != null && GameOverManager.Instance.IsGameOver)
            return;

        bool pausePressed = false;

        if (GameSession.Mode == AppPlayMode.Single)
        {
            var hub = InputHub.Instance;
            pausePressed = hub != null && hub.Current.OnPausePressed;
        }
        else
        {
            var fusion = FusionInputProvider.Instance;

            // Ưu tiên input action nếu dùng được
            if (fusion != null && fusion.isActiveAndEnabled && fusion.IsValid)
                pausePressed = fusion.OnPausePressed;
            else
                // Fallback cứng: ESC luôn hoạt động dù action map đang lỗi / null
                pausePressed = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
        }

        if (pausePressed)
        {
            if (!_pauseEdgeConsumed)
            {
                _pauseEdgeConsumed = true;
                TogglePause();
            }
        }
        else
        {
            _pauseEdgeConsumed = false;
        }
    }

    void OnEnable()
    {
        GameOverManager.OnGameOver += HandleGameOver;
    }

    void OnDisable()
    {
        GameOverManager.OnGameOver -= HandleGameOver;
    }

    void HandleGameOver()
    {
        // Không cho toggle pause nữa
        _pauseEdgeConsumed = true;

        // Chỉ tắt UI pause, không lock cursor, không remove blocker
        ForceCloseUIOnly();
    }

    public void ForceCloseUIOnly()
    {
        _isOpen = false;

        root.alpha = 0;
        root.interactable = false;
        root.blocksRaycasts = false;
    }


    // ==========================================
    // SHOW / HIDE
    // ==========================================

    void Show()
    {
        _isOpen = true;

        Debug.Log("SHOW MENU");

        root.alpha = 1;
        root.interactable = true;
        root.blocksRaycasts = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        InputBlockerSystem.Add(InputBlocker.Full);
    }

    void Hide()
    {
        _isOpen = false;

        root.alpha = 0;
        root.interactable = false;
        root.blocksRaycasts = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        InputBlockerSystem.Remove(InputBlocker.Full);
    }

    void HideImmediate()
    {
        _isOpen = false;
        root.alpha = 0;
        root.interactable = false;
        root.blocksRaycasts = false;
    }

    void TogglePause()
    {
        if (SettingsController.Instance != null && SettingsController.Instance.IsOpen)
        {
            SettingsController.Instance.CloseSettings();
            return;
        }

        if (_isOpen) Hide();
        else Show();
    }

    void OnResumePressed()
    {
        Hide();
    }

    void OnSettingsPressed()
    {
        var settings = FindFirstObjectByType<SettingsController>();
        if (!settings)
        {
            Debug.LogError("[PauseMenu] Không tìm thấy SettingsController trong Game Scene. Hãy đặt prefab Settings vào scene.");
            return;
        }

        settings.BindPauseMenu(this);
        settings.OpenSettings();

        root.alpha = 0;
        root.interactable = false;
        root.blocksRaycasts = false;
    }


    void OnLeavePressed()
    {
        _isExiting = true;
        SessionCleanup.CleanupAll();
        SceneTransitionFader.LoadScene("MainMenu");
    }

    // Thêm hàm public để quay lại từ Settings
    public void ReturnFromSettings()
    {
        if (_isOpen)
        {
            // Hiển thị lại pause menu
            root.alpha = 1;
            root.interactable = true;
            root.blocksRaycasts = true;
        }
    }

    bool IsInputReady()
    {
        if (GameSession.Mode == AppPlayMode.Single)
        {
            // SP → chỉ cần InputHub.Instance tồn tại
            return InputHub.Instance != null;
        }

        // MP → cần FusionInputProvider tồn tại và hợp lệ
        if (FusionInputProvider.Instance == null)
            return false;

        return FusionInputProvider.Instance.IsValid;
    }
}