using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class ZombieNetworkAnimator : NetworkBehaviour
{
    [Header("References")]
    public Animator animator;

    ZombieAppearance _appearance;

    [Header("Locomotion Variants (separate)")]
    public string idleVariantParam = "IdleVariant";
    public int idleVariantCount = 1;

    public string walkVariantParam = "WalkVariant";
    public int walkVariantCount = 1;

    public string runVariantParam = "RunVariant";
    public int runVariantCount = 1;

    [Header("Death Variants")]
    [Tooltip("Tên tham số int dùng để chọn biến thể death (0,1,2,...)")]
    public string deathIndexParam = "DeathIndex";

    [Tooltip("Số biến thể death (clip chết) hiện có, đặt >= 1")]
    public int deathVariantCount = 1;

    [Header("Climb Window")]
    [Tooltip("Tên trigger để play anim trèo cửa sổ")]
    public string climbTriggerParam = "ClimbWindow";

    int _hashClimbTrigger;
    int _hashClimbRootTrigger;

    int _hashAttackIndex;
    int _hashDoAttack;
    int _hashDie;
    int _hashDeathIndex;

    int _hashIdleVariant;
    int _hashWalkVariant;
    int _hashRunVariant;

    bool _locomotionLocked;
    int _idleV, _walkV, _runV;

    [Networked] public float MoveSpeed { get; private set; }

    void Awake()
    {
        if (!animator)
            animator = GetComponentInChildren<Animator>();

        _appearance = GetComponent<ZombieAppearance>();

        _hashAttackIndex = Animator.StringToHash("AttackIndex");
        _hashDoAttack = Animator.StringToHash("DoAttack");
        _hashDie = Animator.StringToHash("Die");

        if (!string.IsNullOrEmpty(idleVariantParam))
            _hashIdleVariant = Animator.StringToHash(idleVariantParam);

        if (!string.IsNullOrEmpty(walkVariantParam))
            _hashWalkVariant = Animator.StringToHash(walkVariantParam);

        if (!string.IsNullOrEmpty(runVariantParam))
            _hashRunVariant = Animator.StringToHash(runVariantParam);

        if (!string.IsNullOrEmpty(deathIndexParam))
            _hashDeathIndex = Animator.StringToHash(deathIndexParam);

        if (!string.IsNullOrEmpty(climbTriggerParam))
            _hashClimbTrigger = Animator.StringToHash(climbTriggerParam);
    }

    void OnEnable()
    {
        _locomotionLocked = false;

        // Local SP: có NetworkObject nhưng KHÔNG có Runner / chưa spawned
        if (Object == null || Object.Runner == null)
            StartCoroutine(ApplyLocomotionNextFrame());
    }

    System.Collections.IEnumerator ApplyLocomotionNextFrame()
    {
        yield return null; // đợi Animator init xong

        EnsureLocomotionLoadoutChosen();
        ApplyLocomotionLoadoutLocally(_idleV, _walkV, _runV);
    }


    public override void Spawned()
    {
        // Chỉ StateAuthority (host) được chọn skin & variant
        if (Object != null && Object.HasStateAuthority)
        {
            // Skin
            if (_appearance != null && _appearance.skinPrefabs != null && _appearance.skinPrefabs.Count > 0)
            {
                int idx = Random.Range(0, _appearance.skinPrefabs.Count);
                RPC_SetSkin(idx);
            }

            // Locomotion variant (nếu có > 1)
            EnsureLocomotionLoadoutChosen();
            RPC_SetLocomotionLoadout(_idleV, _walkV, _runV);
        }
    }

    // ============= RPC SYNC SKIN =============

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_SetSkin(int index)
    {
        ApplySkinLocally(index);
    }

    void ApplySkinLocally(int index)
    {
        if (_appearance != null)
        {
            _appearance.ApplySkinByIndex(index);
        }
    }

    // ============= RPC SYNC LOCOMOTION VARIANT =============

    void EnsureLocomotionLoadoutChosen()
    {
        if (_locomotionLocked) return;

        _idleV = (idleVariantCount > 1) ? Random.Range(0, idleVariantCount) : 0;
        _walkV = (walkVariantCount > 1) ? Random.Range(0, walkVariantCount) : 0;
        _runV = (runVariantCount > 1) ? Random.Range(0, runVariantCount) : 0;

        _locomotionLocked = true;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_SetLocomotionLoadout(int idle, int walk, int run)
    {
        ApplyLocomotionLoadoutLocally(idle, walk, run);
    }

    void ApplyLocomotionLoadoutLocally(int idle, int walk, int run)
    {
        if (!animator) return;

        if (_hashIdleVariant != 0)
            animator.SetFloat(_hashIdleVariant, Mathf.Clamp(idle, 0, Mathf.Max(0, idleVariantCount - 1)));

        if (_hashWalkVariant != 0)
            animator.SetFloat(_hashWalkVariant, Mathf.Clamp(walk, 0, Mathf.Max(0, walkVariantCount - 1)));

        if (_hashRunVariant != 0)
            animator.SetFloat(_hashRunVariant, Mathf.Clamp(run, 0, Mathf.Max(0, runVariantCount - 1)));
    }


    // ============= RPC SYNC ATTACK =============

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlayAttack(int attackIndex)
    {
        PlayAttackLocally(attackIndex);
    }

    void PlayAttackLocally(int attackIndex)
    {
        if (!animator) return;

        animator.SetInteger(_hashAttackIndex, attackIndex);
        animator.SetTrigger(_hashDoAttack);
    }

    public void PlayAttack(int attackIndex)
    {
        // MP: chỉ StateAuthority gọi RPC
        if (Object != null && Object.HasStateAuthority)
        {
            RPC_PlayAttack(attackIndex);
        }
        // SP: gọi trực tiếp (không có NetworkObject)
        else if (Object == null)
        {
            PlayAttackLocally(attackIndex);
        }
        // MP Client: không làm gì (chờ RPC từ host)
    }

    // ============= RPC SYNC DEATH =============

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlayDeath(int variantIndex)
    {
        PlayDeathLocally(variantIndex);
    }

    void PlayDeathLocally(int variantIndex)
    {
        if (!animator) return;

        // Chọn death index hợp lệ
        if (deathVariantCount > 0)
            variantIndex = Mathf.Clamp(variantIndex, 0, deathVariantCount - 1);
        else
            variantIndex = 0;

        // Nếu Animator có tham số death index thì set trước
        if (_hashDeathIndex != 0)
            animator.SetFloat(_hashDeathIndex, variantIndex);

        // Bật bool Die để chuyển sang state death
        animator.SetBool(_hashDie, true);

     //   Debug.Log($"[ZombieNetworkAnimator] PlayDeathLocally, variant={variantIndex}");
    }

    /// <summary>
    /// Gọi khi zombie chết; nếu variantIndex < 0 thì tự random.
    /// </summary>
    public void PlayDeath(int variantIndex = -1)
    {
        // Tự random nếu không chỉ định
        if (variantIndex < 0)
        {
            variantIndex = (deathVariantCount > 1)
                ? Random.Range(0, deathVariantCount)
                : 0;
        }

        // MP: chỉ StateAuthority gọi RPC
        if (Object != null && Object.HasStateAuthority)
        {
            RPC_PlayDeath(variantIndex);
        }
        // SP: gọi trực tiếp (không có NetworkObject)
        else if (Object == null)
        {
            PlayDeathLocally(variantIndex);
        }
        // MP Client: không làm gì (chờ RPC từ host)
    }

    // ============= RPC SYNC CLIMB WINDOW =============

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlayClimbWindow()
    {
        PlayClimbWindowLocally();
    }

    void PlayClimbWindowLocally()
    {
        if (!animator || _hashClimbTrigger == 0) return;
        animator.SetTrigger(_hashClimbTrigger);
    }

    /// <summary>
    /// Gọi từ host (AI) để play anim trèo trên tất cả client.
    /// SP thì gọi trực tiếp không qua RPC.
    /// </summary>
    public void PlayClimbWindow()
    {
        // MP: chỉ StateAuthority gọi RPC
        if (Object != null && Object.HasStateAuthority)
        {
            RPC_PlayClimbWindow();
        }
        // SP: không có NetworkObject
        else if (Object == null)
        {
            PlayClimbWindowLocally();
        }
        // MP client: không làm gì, chờ RPC từ host
    }

    // ============= RPC SYNC CLIMB WINDOW (ROOT MOTION) =============

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlayClimbWindowRM()
    {
        PlayClimbWindowRMLocally();
    }

    void PlayClimbWindowRMLocally()
    {
        if (!animator || _hashClimbRootTrigger == 0) return;
        animator.SetTrigger(_hashClimbRootTrigger);
    }

    /// <summary>
    /// Gọi từ host (AI) để play anim trèo ROOT MOTION trên tất cả client.
    /// SP thì gọi trực tiếp không qua RPC.
    /// </summary>
    public void PlayClimbWindowRM()
    {
        if (Object != null && Object.HasStateAuthority)
        {
            RPC_PlayClimbWindowRM();
        }
        else if (Object == null)
        {
            PlayClimbWindowRMLocally();
        }
    }

    public override void FixedUpdateNetwork()
    {
        // Chỉ host/state authority ghi networked values
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
            return;

        float s = 0f;

        // Ưu tiên lấy từ ZombieMovement nếu có
        var mover = GetComponent<ZombieMovement>();
        if (mover != null && mover.enabled)
        {
            var v = mover.ManualVelocity;
            v.y = 0f;
            s = v.magnitude;
        }

        MoveSpeed = s;
    }

}
