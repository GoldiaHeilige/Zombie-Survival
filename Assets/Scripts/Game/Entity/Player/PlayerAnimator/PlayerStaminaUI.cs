using UnityEngine;
using UnityEngine.UI;

public class PlayerStaminaUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image staminaFill;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Bar Settings")]
    [SerializeField] private float fillLerpSpeed = 10f;
    [SerializeField] private bool alwaysVisible = true; // Thêm option này để linh hoạt
    [SerializeField] private float visibleHoldTime = 1.5f;
    [SerializeField] private float fadeInDuration = 0.1f;
    [SerializeField] private float fadeOutDuration = 0.25f;

    IMovementState _state;

    float _target01 = 1f;
    float _current01 = 1f;
    float _lastChangeTime = -999f;

    public void Bind(IMovementState state)
    {
        _state = state;

        if (_state != null)
        {
            // Giả sử Stamina đã là 0–1
            _target01 = Mathf.Clamp01(_state.Stamina);
            _current01 = _target01;
            _lastChangeTime = Time.unscaledTime;

            if (staminaFill != null)
                staminaFill.fillAmount = _current01;
        }
    }

    void Awake()
    {
        if (canvasGroup != null)
        {
            // Nếu alwaysVisible = true, hiển thị luôn
            canvasGroup.alpha = alwaysVisible ? 1f : 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (staminaFill != null)
            staminaFill.fillAmount = 1f;
    }

    void Update()
    {
        if (_state == null || staminaFill == null)
            return;

        float max = Mathf.Max(1f, _state.MaxStamina);
        float newTarget = Mathf.Clamp01(_state.Stamina / max);

        if (Mathf.Abs(newTarget - _target01) > 0.001f)
        {
            _target01 = newTarget;
            _lastChangeTime = Time.unscaledTime;
        }

        _current01 = Mathf.MoveTowards(
            _current01,
            _target01,
            fillLerpSpeed * Time.unscaledDeltaTime
        );
        staminaFill.fillAmount = _current01;

        if (canvasGroup == null || alwaysVisible)
        {
            // Nếu alwaysVisible = true, không cần xử lý fade
            if (alwaysVisible && canvasGroup != null && canvasGroup.alpha != 1f)
            {
                canvasGroup.alpha = 1f;
            }
            return;
        }

        float targetAlpha = 0f;
        bool recentlyChanged = (Time.unscaledTime - _lastChangeTime) <= visibleHoldTime;
        if (recentlyChanged)
            targetAlpha = 1f;

        float dur = targetAlpha > canvasGroup.alpha ? fadeInDuration : fadeOutDuration;
        if (dur <= 0f) dur = 0.001f;
        float speed = 1f / dur;

        canvasGroup.alpha = Mathf.MoveTowards(
            canvasGroup.alpha,
            targetAlpha,
            speed * Time.unscaledDeltaTime
        );
    }
}