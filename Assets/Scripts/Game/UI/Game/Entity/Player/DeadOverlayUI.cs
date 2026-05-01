using UnityEngine;

public class DeadOverlayUI : MonoBehaviour
{
    public static DeadOverlayUI Instance { get; private set; }

    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Game Over")]
    [SerializeField] private GameOverPanel gameOverPanel;
    [SerializeField, Min(0f)] private float showGameOverDelay = 2f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (!canvasGroup)
            canvasGroup = GetComponentInChildren<CanvasGroup>(true);

        SetVisible(false);
    }

    private void OnEnable()
    {
        GameOverManager.OnGameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        GameOverManager.OnGameOver -= HandleGameOver;
    }

    /// <summary>
    /// Cho các chỗ khác vẫn có thể bật/tắt overlay thủ công (như trước).
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (!canvasGroup) return;

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    // ----------------------------------------------------
    // GameOver flow
    // ----------------------------------------------------

    private void HandleGameOver()
    {
        // 1) Hiện overlay chết (màn xám) ngay lập tức
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SetVisible(true);

        // 3) Sau 1 khoảng delay -> build snapshot + show GameOverPanel
        if (gameOverPanel != null)
        {
            gameOverPanel.ShowFromCurrentRuntimeDelayed(showGameOverDelay);
        }
        else
        {
            Debug.LogWarning("[DeadOverlayUI] GameOverPanel chưa được gán trong Inspector.");
        }
    }
}
