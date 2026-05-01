using UnityEngine;

public class DeadVisibilityUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup target;

    private PlayerLifeController _life;

    void Reset()
    {
        if (!target) target = GetComponent<CanvasGroup>();
    }

    public void Bind(PlayerLifeController life)
    {
        if (_life != null)
        {
            _life.OnDowned -= OnDowned;
            _life.OnRevived -= OnRevivedOrRespawned;
            _life.OnRespawned -= OnRevivedOrRespawned;
            _life.OnDead -= OnDead;
        }

        _life = life;

        if (_life != null)
        {
            _life.OnDowned += OnDowned;
            _life.OnRevived += OnRevivedOrRespawned;
            _life.OnRespawned += OnRevivedOrRespawned;
            _life.OnDead += OnDead;

            // sync state ngay khi bind
            Apply(_life.state);
        }
    }

    void OnDisable()
    {
        if (_life != null)
        {
            _life.OnDowned -= OnDowned;
            _life.OnRevived -= OnRevivedOrRespawned;
            _life.OnRespawned -= OnRevivedOrRespawned;
            _life.OnDead -= OnDead;
        }
        _life = null;
    }

    private void OnDowned(PlayerLifeController who) => Apply(who.state);
    private void OnRevivedOrRespawned(PlayerLifeController who) => Apply(who.state);
    private void OnDead(PlayerLifeController who) => Apply(who.state);

    private void Apply(LifeState s)
    {
        if (!target) return;

        bool enabled = (s != LifeState.Dead);

        target.alpha = enabled ? 1f : 0f;
        target.interactable = enabled;
        target.blocksRaycasts = enabled;
    }
}
