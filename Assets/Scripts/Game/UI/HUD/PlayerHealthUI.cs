using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerHealthUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image hpFill;      // image fill của thanh HP
    [SerializeField] private TMP_Text hpText;   // optional, có cũng được
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Bar Settings")]
    [SerializeField] private float fillLerpSpeed = 8f;     // tốc độ mượt fill
    [SerializeField] private bool alwaysVisible = true; // Thêm option này để linh hoạt
    [SerializeField] private float visibleHoldTime = 2f;   // bao lâu sau thay đổi thì giữ hiện
    [SerializeField] private float fadeInDuration = 0.15f;
    [SerializeField] private float fadeOutDuration = 0.3f;

    private IHealthState _state;

    float _target01 = 1f;
    float _current01 = 1f;
    float _lastChangeTime = -999f;

    void Awake()
    {
        if (canvasGroup != null)
        {
            // Nếu alwaysVisible = true, hiển thị luôn
            canvasGroup.alpha = alwaysVisible ? 1f : 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }
    }

    void OnDisable()
    {
        Unhook();
    }

    public void Bind(IHealthState state)
    {
        if (state == _state)
        {
            Refresh();
            return;
        }

        Unhook();
        _state = state;
        Hook();
        Refresh();
    }

    void Hook()
    {
        if (_state == null) return;
        _state.OnHealthChanged += OnHealthChanged;
        _state.OnDeath += OnDeath;
        _state.OnRevive += OnRevive;
    }

    void Unhook()
    {
        if (_state == null) return;
        _state.OnHealthChanged -= OnHealthChanged;
        _state.OnDeath -= OnDeath;
        _state.OnRevive -= OnRevive;
        _state = null;
    }

    void OnHealthChanged(float cur, float max)
    {
        UpdateBar(cur, max);
    }

    void OnDeath()
    {
        UpdateBar(0f, _state != null ? _state.Max : 0f);
    }

    void OnRevive()
    {
        Refresh();
    }

    void Refresh()
    {
        if (_state == null) return;
        UpdateBar(_state.Current, _state.Max);
    }

    void UpdateBar(float cur, float max)
    {
        float t = (max > 0f) ? Mathf.Clamp01(cur / max) : 0f;
        _target01 = t;

        if (hpText != null)
            hpText.text = $"{cur:F0}/{max:F0}";

        _lastChangeTime = Time.unscaledTime; // đánh dấu vừa thay đổi để hiện HUD
    }

    void Update()
    {
        // Lerp fill cho mượt
        if (hpFill != null)
        {
            _current01 = Mathf.MoveTowards(
                _current01,
                _target01,
                fillLerpSpeed * Time.unscaledDeltaTime
            );
            hpFill.fillAmount = _current01;
        }

        // Fade CanvasGroup - chỉ xử lý nếu không phải alwaysVisible
        if (canvasGroup == null || alwaysVisible)
        {
            // Nếu alwaysVisible = true, đảm bảo alpha luôn là 1
            if (alwaysVisible && canvasGroup != null && canvasGroup.alpha != 1f)
            {
                canvasGroup.alpha = 1f;
            }
            return;
        }

        float targetAlpha = 0f;

        if (_state != null)
        {
            bool recentlyChanged = (Time.unscaledTime - _lastChangeTime) <= visibleHoldTime;
            if (recentlyChanged)
                targetAlpha = 1f;
        }

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