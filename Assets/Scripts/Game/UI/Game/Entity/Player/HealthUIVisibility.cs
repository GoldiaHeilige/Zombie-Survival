using UnityEngine;

public class HealthUIVisibility : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private bool hideWhenDowned = true; // Downed cũng ẩn luôn

    private PlayerLifeController _life;

    void Awake()
    {
        if (!canvasGroup)
            canvasGroup = GetComponent<CanvasGroup>();

        if (!canvasGroup)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void Bind(PlayerLifeController life)
    {
        if (!life) return;

        // unbind cũ nếu có
        if (_life != null)
        {
            _life.OnDowned -= HandleDowned;
            _life.OnDead -= HandleDead;
            _life.OnRevived -= HandleAlive;
            _life.OnRespawned -= HandleAlive;
        }

        _life = life;

        _life.OnDowned += HandleDowned;
        _life.OnDead += HandleDead;
        _life.OnRevived += HandleAlive;
        _life.OnRespawned += HandleAlive;

        ApplyState(_life.state);
    }

    void OnDisable()
    {
        if (_life != null)
        {
            _life.OnDowned -= HandleDowned;
            _life.OnDead -= HandleDead;
            _life.OnRevived -= HandleAlive;
            _life.OnRespawned -= HandleAlive;
        }
    }

    void HandleDowned(PlayerLifeController who) => ApplyState(LifeState.Downed);
    void HandleDead(PlayerLifeController who) => ApplyState(LifeState.Dead);
    void HandleAlive(PlayerLifeController who) => ApplyState(LifeState.Alive);

    void ApplyState(LifeState s)
    {
        if (!canvasGroup) return;

        bool show =
            (s == LifeState.Alive) ||
            (!hideWhenDowned && s == LifeState.Downed);

        canvasGroup.alpha = show ? 1f : 0f;
        canvasGroup.interactable = show;
        canvasGroup.blocksRaycasts = show;
    }
}
