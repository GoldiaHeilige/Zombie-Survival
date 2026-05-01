using System;
using System.Collections;
using TT;
using UnityEngine;
using UnityEngine.AI;

public enum LifeState { Alive, Downed, Dead }

/// <summary>
/// ONE-STOP cho Alive/Downed/Dead + gate damage/target + grace khi revive/respawn.
/// ĐÃ BỎ BLEED-OUT HOÀN TOÀN.
/// - SP: set enableDownedInThisMode = false -> lethal damage => Dead ngay.
/// - MP: set enableDownedInThisMode = true  -> Downed vô hạn, chờ đồng đội cứu.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DamageableHealth))]
public class PlayerLifeController : MonoBehaviour, IDamageable
{
    [Header("Mode")]
    [Tooltip("SP = tắt (false) để lethal damage -> Dead ngay. MP = bật (true) để có trạng thái Downed vô hạn.")]
    public bool enableDownedInThisMode = true;

    [Header("Target/Aim")]
    [Tooltip("Điểm AI bám theo/ngắm bắn. Để trống sẽ dùng transform của object này.")]
    public Transform targetTransformOverride;

    [Header("Downed/Coop")]
    [Tooltip("Trong Coop có cho zombie tiếp tục đánh khi player đang Downed không? (Solo nên tắt)")]
    public bool coopAllowDamageWhenDowned = true;

    [Header("Grace khi Revive/Respawn")]
    public float reviveInvulnSeconds = 1.75f;
    public float respawnInvulnSeconds = 3.0f;
    public float threatSpike = 500f;     // nếu bạn dùng TargetService (tuỳ)
    public float threatDelay = 0.5f;

    [Header("Repel/Stun gần khi đứng dậy")]
    public LayerMask zombieLayer;
    public float repelRadius = 2.5f;
    public float repelForce = 6f;
    public float stunDuration = 0.7f; // cần ZombieStunReceiver trên zombie để nhận stun

    [Header("Safe spawn (optional)")]
    [Tooltip("Nếu có SafeSpawnVolume ở điểm respawn, gán vào đây để bật tạm khi respawn.")]
    public SafeSpawnVolume safeSpawnVolume;

    // ===== Runtime =====
    public LifeState state = LifeState.Alive;
    public bool temporaryInvulnerable = false;
    float _invulnUntil = 0f;

    DamageableHealth _hp;

    // ===== Static events =====
    public event Action<PlayerLifeController> OnDowned;
    public event Action<PlayerLifeController> OnDead;   
    public event Action<PlayerLifeController> OnRevived;
    public event Action<PlayerLifeController> OnRespawned;

    // ===== ITargetable =====
    public Transform TargetTransform => targetTransformOverride != null ? targetTransformOverride : transform;
    public bool CanBeAttacked
    {
        get
        {
            if (temporaryInvulnerable || Time.time < _invulnUntil) return false;
            if (state == LifeState.Dead) return false;
            if (state == LifeState.Downed && !coopAllowDamageWhenDowned) return false;
            return true;
        }
    }
    public bool IsAliveLike => state == LifeState.Alive || state == LifeState.Downed;


    // ===== IDamageable passthrough (GATE) =====
    public event Action<DamageEvent, DamageResult> OnHit
    {
        add { if (_hp != null) _hp.OnHit += value; }
        remove { if (_hp != null) _hp.OnHit -= value; }
    }

    void Awake()
    {
        _hp = GetComponent<DamageableHealth>();
     //   AutoDetectMode();
/*        Debug.Log($"[Life] Awake {name}: state={state} enableDowned={enableDownedInThisMode} " +
                  $"coopAllowDamageWhenDowned={coopAllowDamageWhenDowned}");*/
        if (_hp == null)
            Debug.LogError("[PlayerLifeController] Missing DamageableHealth on the same object.");
    }


    void OnEnable()
    {
        AutoDetectMode();
    }

    void AutoDetectMode()
    {
#if FUSION_WEAVER || FUSION2
        var runner = FindFirstObjectByType<Fusion.NetworkRunner>(FindObjectsInactive.Exclude);
        enableDownedInThisMode = runner != null;
     // Debug.Log($"[Life] Mode detected: MP={enableDownedInThisMode}, Runner found: {runner != null}");
#else
    enableDownedInThisMode = false;
    Debug.Log($"[Life] Mode detected: SP");
#endif
    }


    // ---------- Public API ----------
    void NotifyNetLifeIfServer()
    {
#if FUSION_WEAVER
        var hsf = GetComponentInParent<HealthSyncFusion>();
        if (hsf && hsf.Object && hsf.Object.HasStateAuthority)
            hsf.ServerSetLife(state);
#endif
    }

    public void SignalDowned()
    {
        state = LifeState.Downed;
        OnDowned?.Invoke(this);

        NotifyNetLifeIfServer();

#if FUSION_WEAVER
        // Host (StateAuthority) broadcast downed event cho toàn bộ client
        var bridge = GetComponentInParent<FusionNetBridge>();
        if (bridge && bridge.Object && bridge.HasStateAuth && bridge.Runner != null && bridge.Runner.IsRunning)
        {
            bridge.RPC_AnnounceDowned();
        }
#endif
    }

    public void SignalDead()
    {
/*        Debug.Log($"[Life] SignalDead CALLED on {name} " +
                  $"state={state} IsAliveLike={IsAliveLike} CanBeAttacked={CanBeAttacked}");*/
        state = LifeState.Dead;
        OnDead?.Invoke(this);

        NotifyNetLifeIfServer();
    }

    public void SignalRevived()
    {
/*        Debug.Log($"[Life] SignalRevived CALLED on {name} " +
                  $"state={state} IsAliveLike={IsAliveLike} CanBeAttacked={CanBeAttacked}");*/
        state = LifeState.Alive;
        OnRevived?.Invoke(this);
        NotifyNetLifeIfServer();
        StartCoroutine(CoGrace(reviveInvulnSeconds));
    }

    public void SignalRespawned()
    {
        state = LifeState.Alive;
        OnRespawned?.Invoke(this);
        if (safeSpawnVolume != null) safeSpawnVolume.EnableFor(respawnInvulnSeconds);
        StartCoroutine(CoGrace(respawnInvulnSeconds));
    }

    public void GrantInvulnerability(float seconds)
    {
        temporaryInvulnerable = true;
        _invulnUntil = Time.time + Mathf.Max(0f, seconds);
    }

    public void RevokeInvulnerability()
    {
        temporaryInvulnerable = false;
        _invulnUntil = 0f;
    }

    public Transform GetAimTarget() => _hp ? _hp.GetAimTarget() : TargetTransform;

    // ---------- IDamageable ----------
    public TeamId GetTeam() => _hp ? _hp.GetTeam() : TeamId.Neutral;

    public bool CanTakeDamage(in DamageEvent e)
    {
        if (!CanBeAttacked) return false;     // Gate chính
        return _hp ? _hp.CanTakeDamage(e) : false;
    }

    public DamageResult ApplyDamage(in DamageEvent e)
    {
     //   Debug.Log($"[Life] ApplyDamage: state={state}, enableDowned={enableDownedInThisMode}, currentHP={_hp.currentHealth}, damage={e.baseDamage}");//

        if (state == LifeState.Alive)
        {
            float projected = _hp.currentHealth - Mathf.Max(0f, e.baseDamage);
      //      Debug.Log($"[Life] Projected HP: {projected}");     

            if (projected <= 0f)
            {
          //      Debug.Log($"[Life] Lethal damage detected! enableDowned={enableDownedInThisMode}");
                if (enableDownedInThisMode)
                {
                    Debug.Log($"[Life] Going to Downed state");
                    // MP (hoặc mode có Downed): chuyển sang Downed vô hạn
                    if (state != LifeState.Downed)
                    {
                        state = LifeState.Downed;
                        SignalDowned(); // Host sẽ gọi RPC qua HealthSyncFusion

                        // THÊM: Đảm bảo client cũng nhận được trạng thái
#if FUSION_WEAVER
                        var hsf = GetComponentInParent<HealthSyncFusion>();
                        if (hsf && hsf.Object && hsf.Object.HasStateAuthority)
                        {
                            hsf.ServerSetLife(state);
                        }
#endif
                    }

                    float downedPool = Mathf.Max(1f, _hp.maxHealth * 0.1f);
                    // dùng helper có sẵn, tự bắn OnHpChanged
                    _hp.SetCurrentFromNet(Mathf.RoundToInt(downedPool));

                    return new DamageResult
                    {
                        isApplied = true,
                        finalDamage = Mathf.Max(0f, e.baseDamage),
                        isFatal = false,
                        remainingHealth = _hp.currentHealth
                    };
                }
                else
                {
                    Debug.Log($"[Life] Going to Dead state (SP mode)");
                    // SP: lethal => Dead ngay
                    if (state != LifeState.Dead)
                    {
                        Debug.Log("[Life] LETHAL -> call SignalDead");
                        state = LifeState.Dead;
                        SignalDead();

#if FUSION_WEAVER
                        var hsf = GetComponentInParent<HealthSyncFusion>();
                        if (hsf && hsf.Object && hsf.Object.HasStateAuthority)
                        {
                            hsf.ServerSetLife(state);
                        }
#endif
                    }

                    _hp.SetCurrentFromNet(0);  // tự bắn OnHpChanged(before, after)


                    return new DamageResult
                    {
                        isApplied = true,
                        finalDamage = Mathf.Max(0f, e.baseDamage),
                        isFatal = true,  // ← Giữ true cho SP
                        remainingHealth = 0f
                    };
                }

            }
        }

        // Nếu đang Downed (solo) hoặc Dead thì gate CanBeAttacked() đã chặn ở CanTakeDamage()
        if (!CanBeAttacked)
        {
            return new DamageResult
            {
                isApplied = false,
                finalDamage = 0f,
                isFatal = false,
                remainingHealth = _hp ? _hp.currentHealth : 0f
            };
        }

        // Trường hợp bình thường: forward vào DamageableHealth
        return _hp.ApplyDamage(e);
    }

    // ---------- Grace helpers ----------
    IEnumerator CoGrace(float invuln)
    {
        // 1) Invuln ngay lập tức
        GrantInvulnerability(invuln);

        // 2) Repel + Stun (tuỳ chọn) xung quanh

        // 3) Threat spike trễ (nếu có TargetService)
        if (TargetService.I != null && threatSpike > 0f)
        {
            yield return new WaitForSeconds(threatDelay);
            // TargetService.I.AddThreat(this, threatSpike); // bật nếu bạn dùng hệ threat
        }

        // 4) Hết invuln: thả cờ
        yield return new WaitForSeconds(Mathf.Max(0f, invuln - threatDelay));
        temporaryInvulnerable = false;
        _invulnUntil = 0f;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (repelRadius > 0f)
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, repelRadius);
        }
    }
#endif
}
