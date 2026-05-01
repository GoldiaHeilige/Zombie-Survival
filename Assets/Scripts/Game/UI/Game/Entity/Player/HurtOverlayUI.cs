using UnityEngine;

public class HurtOverlayUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private CanvasGroup overlayGroup;

    [Header("Config")]
    [SerializeField] private float maxAlpha = 0.8f;    // HP thấp → alpha tối đa
    [SerializeField] private bool hideWhenDowned = true;

    private IHealthState _health;
    private PlayerLifeController _life;

    void Awake()
    {
        if (!overlayGroup)
            overlayGroup = GetComponent<CanvasGroup>();

        if (!overlayGroup)
            overlayGroup = gameObject.AddComponent<CanvasGroup>();

        // overlay không cần interact
        overlayGroup.interactable = false;
        overlayGroup.blocksRaycasts = false;
        overlayGroup.alpha = 0f;
    }

    void OnDisable()
    {
        Unhook();
    }

    public void Bind(IHealthState health, PlayerLifeController life)
    {
        Unhook();

        _health = health;
        _life = life;

        Hook();
        RefreshAll();
    }

    private void Hook()
    {
        if (_health != null)
        {
            _health.OnHealthChanged += OnHealthChanged;
            _health.OnDeath += OnDeath;
            _health.OnRevive += OnRevive;
        }

        if (_life != null)
        {
            _life.OnDowned += OnDowned;
            _life.OnDead += OnLifeDead;
            _life.OnRevived += OnLifeAlive;
            _life.OnRespawned += OnLifeAlive;
        }
    }

    private void Unhook()
    {
        if (_health != null)
        {
            _health.OnHealthChanged -= OnHealthChanged;
            _health.OnDeath -= OnDeath;
            _health.OnRevive -= OnRevive;
        }

        if (_life != null)
        {
            _life.OnDowned -= OnDowned;
            _life.OnDead -= OnLifeDead;
            _life.OnRevived -= OnLifeAlive;
            _life.OnRespawned -= OnLifeAlive;
        }
    }

    // ===== EVENT HANDLERS =====

    void OnHealthChanged(float cur, float max)
    {
        UpdateAlphaByHealth(cur, max);
    }

    void OnDeath()
    {
        // Chết thì tắt hẳn overlay
        SetAlpha(0f);
    }

    void OnRevive()
    {
        RefreshAll();
    }

    void OnDowned(PlayerLifeController who)
    {
        if (hideWhenDowned)
            SetAlpha(0f);
    }

    void OnLifeDead(PlayerLifeController who)
    {
        SetAlpha(0f);
    }

    void OnLifeAlive(PlayerLifeController who)
    {
        RefreshAll();
    }

    // ===== CORE LOGIC =====

    private void RefreshAll()
    {
        if (_health == null)
        {
            SetAlpha(0f);
            return;
        }

        UpdateAlphaByHealth(_health.Current, _health.Max);
    }

    private void UpdateAlphaByHealth(float cur, float max)
    {
        if (!overlayGroup) return;

        // Nếu đang Downed/Dead → tắt overlay luôn
        if (_life != null)
        {
            if (_life.state == LifeState.Dead ||
                (hideWhenDowned && _life.state == LifeState.Downed))
            {
                SetAlpha(0f);
                return;
            }
        }

        if (max <= 0f)
        {
            SetAlpha(0f);
            return;
        }

        float hp01 = Mathf.Clamp01(cur / max);   // 1 = full HP
        if (hp01 >= 0.99f)
        {
            // Full HP ~ không bị thương → tắt overlay
            SetAlpha(0f);
            return;
        }

        // HP càng thấp → alpha càng cao
        float intensity = 1f - hp01;             // 0 (full HP) → 1 (0 HP)
        float alpha = intensity * maxAlpha;
        SetAlpha(alpha);
    }

    private void SetAlpha(float a)
    {
        if (!overlayGroup) return;

        overlayGroup.alpha = Mathf.Clamp01(a);
        // luôn là HUD bị động, không block raycast
        overlayGroup.interactable = false;
        overlayGroup.blocksRaycasts = false;
    }
}
