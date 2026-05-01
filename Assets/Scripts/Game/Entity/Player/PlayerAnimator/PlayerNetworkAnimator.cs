#if FUSION_WEAVER
using Fusion;
#endif
using UnityEngine;

/// <summary>
/// Đồng bộ các tham số Animator của Player qua mạng (Fusion).
/// - StateAuthority: đọc param từ Animator -> ghi vào Networked fields.
/// - Proxy: đọc Networked fields -> apply lại lên Animator.
/// - Có sẵn helper cho "Downed" và "HasWeapon" dùng sau này.
/// </summary>
[DisallowMultipleComponent]
// KHÔNG còn RequireComponent(Animator) vì Animator nằm trong skin, được inject runtime
public sealed class PlayerNetworkAnimator :
#if FUSION_WEAVER
    NetworkBehaviour
#else
    MonoBehaviour
#endif
{
    [Header("References")]
    [SerializeField] private Animator animator;

    [Header("Appearance (skin)")]
    [SerializeField] private PlayerAppearance appearance;

    [Header("Animator Param Names")]
    [SerializeField] private string speedParam = "Speed";
    [SerializeField] private string locomotionStateParam = "LocomotionState";
    [SerializeField] private string groundedParam = "Grounded";
    [SerializeField] private string sprintParam = "Sprinting";
    [SerializeField] private string crouchParam = "Crouch";
    [SerializeField] private string stunnedParam = "Stunned";
    [SerializeField] private string moveXParam = "MoveX";
    [SerializeField] private string moveYParam = "MoveY";
    static readonly int HashIsADS = Animator.StringToHash("IsADS");


    [Header("Extra Params (optional)")]
    [SerializeField] private string downedParam = "Downed";
    [SerializeField] private string hasWeaponParam = "HasWeapon";
    [SerializeField] private string weaponTypeParam = "WeaponType";
    [SerializeField] private string fireTriggerParam = "Fire";

    int _hashSpeed, _hashLocomotionState, _hashGrounded, _hashSprint, _hashCrouch;
    int _hashStunned, _hashMoveX, _hashMoveY, _hashDowned, _hashHasWeapon, _hashWeaponType;
    int _hashFireTrigger;

#if FUSION_WEAVER
    [Networked] private float NetSpeed { get; set; }
    [Networked] private float NetMoveX { get; set; }
    [Networked] private float NetMoveY { get; set; }
    [Networked] private int NetLocomotionState { get; set; }
    [Networked] private int NetWeaponType { get; set; }
    [Networked] private int NetFireShotCounter { get; set; }
    [Networked] private NetworkBool NetGrounded { get; set; }
    [Networked] private NetworkBool NetSprint { get; set; }
    [Networked] private NetworkBool NetCrouch { get; set; }
    [Networked] private NetworkBool NetStunned { get; set; }
    [Networked] private NetworkBool NetDowned { get; set; }
    [Networked] private NetworkBool NetHasWeapon { get; set; }
    [Networked] private NetworkBool IsADS { get; set; }

    int _lastSeenFireShotCounter;
#endif

    void Awake()
    {
        // Cho phép fallback: nếu chưa được inject Animator từ skin, thì tự đi tìm trong children
        if (!animator)
            animator = GetComponentInChildren<Animator>(true);

        if (!appearance)
            appearance = GetComponent<PlayerAppearance>();

        _hashSpeed = Animator.StringToHash(speedParam);
        _hashLocomotionState = Animator.StringToHash(locomotionStateParam);
        _hashGrounded = Animator.StringToHash(groundedParam);
        _hashSprint = Animator.StringToHash(sprintParam);
        _hashCrouch = Animator.StringToHash(crouchParam);
        _hashStunned = Animator.StringToHash(stunnedParam);
        _hashMoveX = Animator.StringToHash(moveXParam);
        _hashMoveY = Animator.StringToHash(moveYParam);

        _hashDowned = string.IsNullOrEmpty(downedParam) ? 0 : Animator.StringToHash(downedParam);
        _hashHasWeapon = string.IsNullOrEmpty(hasWeaponParam) ? 0 : Animator.StringToHash(hasWeaponParam);
        _hashWeaponType = string.IsNullOrEmpty(weaponTypeParam) ? 0 : Animator.StringToHash(weaponTypeParam);
        _hashFireTrigger = string.IsNullOrEmpty(fireTriggerParam) ? 0 : Animator.StringToHash(fireTriggerParam);
    }

    /// <summary>
    /// Được PlayerAppearance gọi khi skin (và Animator) thay đổi.
    /// </summary>
    public void SetAnimator(Animator newAnimator)
    {
        animator = newAnimator;
    }

#if FUSION_WEAVER
    public override void Spawned()
    {
        base.Spawned();

        // MP: chỉ StateAuthority random skin & sync cho tất cả, giống ZombieNetworkAnimator
        if (Object != null && Object.IsValid && Object.HasStateAuthority && appearance != null && appearance.SkinCount > 0)
        {
            int skinIndex = Random.Range(0, appearance.SkinCount);
            RPC_SetSkin(skinIndex);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!animator || !Object || !Object.IsValid)
            return;

#if FUSION_WEAVER
        if (Object.HasStateAuthority)
        {
            // Lấy input của player này (chỉ host/state authority chắc chắn có)
            if (Runner.TryGetInputForPlayer(Object.InputAuthority, out PlayerInputData input))
                IsADS = input.ads;
            else
                IsADS = false;
        }

        // Apply cho animator trên MỌI máy (host + proxies)
        if (animator != null)
            animator.SetBool(HashIsADS, IsADS);
#endif

        // Chỉ StateAuthority mới ghi network state
        if (Object.HasStateAuthority)
            ReadFromAnimatorToNetwork();
    }
#endif

    void Update()
    {
#if FUSION_WEAVER
        if (!animator || !Object || !Object.IsValid)
            return;

        // Proxy (không phải StateAuthority) thì apply network state vào Animator
        if (!Object.HasStateAuthority)
            ApplyNetworkToAnimator();

        // Replicate trigger Fire cho non-authority
        if (Object != null && Object.IsValid && !Object.HasStateAuthority)
        {
            if (_hashFireTrigger != 0 && NetFireShotCounter != _lastSeenFireShotCounter)
            {
                _lastSeenFireShotCounter = NetFireShotCounter;
                animator.SetTrigger(_hashFireTrigger);
            }
        }
#endif
    }

#if FUSION_WEAVER
    void ReadFromAnimatorToNetwork()
    {
        // Float
        NetSpeed = animator.GetFloat(_hashSpeed);
        NetMoveX = animator.GetFloat(_hashMoveX);
        NetMoveY = animator.GetFloat(_hashMoveY);

        // Int
        NetLocomotionState = animator.GetInteger(_hashLocomotionState);

        // Bool
        NetGrounded = animator.GetBool(_hashGrounded);
        NetSprint = animator.GetBool(_hashSprint);
        NetCrouch = animator.GetBool(_hashCrouch);
        NetStunned = animator.GetBool(_hashStunned);

        if (_hashDowned != 0)
            NetDowned = animator.GetBool(_hashDowned);
        if (_hashHasWeapon != 0)
            NetHasWeapon = animator.GetBool(_hashHasWeapon);
        if (_hashWeaponType != 0)
            NetWeaponType = animator.GetInteger(_hashWeaponType);
    }

    void ApplyNetworkToAnimator()
    {
        // Float
        animator.SetFloat(_hashSpeed, NetSpeed);
        animator.SetFloat(_hashMoveX, NetMoveX);
        animator.SetFloat(_hashMoveY, NetMoveY);

        // Int
        animator.SetInteger(_hashLocomotionState, NetLocomotionState);

        // Bool
        animator.SetBool(_hashGrounded, NetGrounded);
        animator.SetBool(_hashSprint, NetSprint);
        animator.SetBool(_hashCrouch, NetCrouch);
        animator.SetBool(_hashStunned, NetStunned);

        if (_hashDowned != 0)
            animator.SetBool(_hashDowned, NetDowned);
        if (_hashHasWeapon != 0)
            animator.SetBool(_hashHasWeapon, NetHasWeapon);
        if (_hashWeaponType != 0)
            animator.SetInteger(_hashWeaponType, NetWeaponType);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_SetSkin(int skinIndex)
    {
        if (appearance != null)
        {
            appearance.ApplySkinByIndex(skinIndex);
        }
    }
#endif

    // ===== Helper API cho các hệ thống khác (Health, Weapon, v.v.) =====

    /// <summary>Set cờ Downed trên Animator + network (nếu có Fusion).</summary>
    public void SetDowned(bool isDowned)
    {
        if (_hashDowned == 0 || !animator)
            return;

        animator.SetBool(_hashDowned, isDowned);

#if FUSION_WEAVER
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetDowned = isDowned;
#endif
    }

    /// <summary>Gọi mỗi lần bắn 1 viên – sẽ play trigger Fire local và sync sang client khác.</summary>
    public void NotifyShotFired()
    {
        if (!animator || _hashFireTrigger == 0)
            return;

    //    Debug.Log("[Anim] NotifyShotFired()");

        // Local (authority) chơi anim luôn
        animator.SetTrigger(_hashFireTrigger);

#if FUSION_WEAVER
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
        {
            NetFireShotCounter++;
        }
#endif
    }

    /// <summary>Set WeaponType (0 = none, 1 = Rifle, 2 = Pistol...).</summary>
    public void SetWeaponType(int type)
    {
        if (_hashWeaponType == 0 || !animator)
            return;

        animator.SetInteger(_hashWeaponType, type);

#if FUSION_WEAVER
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetWeaponType = type;
#endif
    }

    /// <summary>Set cờ HasWeapon (được gọi từ PlayerWeaponWorldView hoặc Weapon bridge).</summary>
    public void SetHasWeapon(bool hasWeapon)
    {
        if (_hashHasWeapon == 0 || !animator)
            return;

        animator.SetBool(_hashHasWeapon, hasWeapon);

#if FUSION_WEAVER
        if (Object != null && Object.IsValid && Object.HasStateAuthority)
            NetHasWeapon = hasWeapon;
#endif
    }
}
