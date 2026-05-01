using UnityEngine;

[DisallowMultipleComponent]
public class PlayerDownedHandler : MonoBehaviour
{
    [Header("Refs")]
    public PlayerLifeController life;
    public PlayerNetworkAnimator netAnimator;

    LifeState _lastState = LifeState.Alive;

    void Awake()
    {
        if (!life)
            life = GetComponentInParent<PlayerLifeController>();
        if (!netAnimator)
            netAnimator = GetComponentInChildren<PlayerNetworkAnimator>(true);
    }

    void OnEnable()
    {
        if (life != null)
        {
            life.OnDowned += OnLifeDowned;
            life.OnDead += OnLifeDead;
            life.OnRevived += OnLifeRevivedOrRespawned;
            life.OnRespawned += OnLifeRevivedOrRespawned;

            _lastState = life.state;
            ApplyStateImmediate(_lastState);
        }
    }

    void OnDisable()
    {
        if (life != null)
        {
            life.OnDowned -= OnLifeDowned;
            life.OnDead -= OnLifeDead;
            life.OnRevived -= OnLifeRevivedOrRespawned;
            life.OnRespawned -= OnLifeRevivedOrRespawned;
        }
    }

    // ===== Event handlers =====

    void OnLifeDowned(PlayerLifeController who)
    {
        if (who != life) return;

        Transition(_lastState, LifeState.Downed);
        _lastState = LifeState.Downed;
    }

    void OnLifeDead(PlayerLifeController who)
    {
        if (who != life) return;

        Transition(_lastState, LifeState.Dead);
        _lastState = LifeState.Dead;
    }

    void OnLifeRevivedOrRespawned(PlayerLifeController who)
    {
        if (who != life) return;

        Transition(_lastState, LifeState.Alive);
        _lastState = LifeState.Alive;
    }

    // ===== Core transition logic =====

    void ApplyStateImmediate(LifeState s)
    {
        if (netAnimator == null)
            return;

        // Nếu đang Downed hoặc Dead → Downed bool = true
        bool downed = (s == LifeState.Downed || s == LifeState.Dead);
        netAnimator.SetDowned(downed);
    }

    void Transition(LifeState from, LifeState to)
    {
        if (netAnimator == null)
            return;

        // Alive -> Downed  : lần lethal đầu tiên (MP)
        // Alive -> Dead    : lethal trực tiếp (SP)
        // Downed -> Dead   : đã nằm sẵn, không set thêm gì
        // Any   -> Alive   : đứng dậy

        if (to == LifeState.Alive)
        {
            netAnimator.SetDowned(false);
        }
        else if (from == LifeState.Alive && (to == LifeState.Downed || to == LifeState.Dead))
        {
            netAnimator.SetDowned(true);
        }
        // from Downed -> Dead: giữ nguyên Downed = true
    }
}
