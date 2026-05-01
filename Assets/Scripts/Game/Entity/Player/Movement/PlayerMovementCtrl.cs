using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
#if FUSION_WEAVER
using Fusion;
#endif


[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class PlayerMovementController : MonoBehaviour
{
    [Header("Config")]
    public MovementConfig config;

    [Header("Driver")]
    [Tooltip("Nếu bật, core sẽ được driver bên ngoài gọi Simulate(). Core sẽ KHÔNG tự chạy trong Update().")]
    public bool drivenExternally = true;


    [Header("Debug/Runtime")]
    public float stamina;
    public bool wantsSprint;
    public bool wantsCrouch;
    // NOTE: jumpPressedThisFrame vẫn giữ để tương thích, nhưng sẽ không dùng để nhảy trực tiếp nữa
    public bool jumpPressedThisFrame;
    public MoveState state { get; private set; } = MoveState.Idle;

    public Vector3 WorldVelocity { get; private set; }

    public enum MoveState { Idle, Walking, Sprinting, Jumping, Falling, Crouching, Stunned }
    MoveState _lastState = (MoveState)(-1);

    CharacterController cc;
    bool _freezeBodyYaw;
    public MovementStats stats { get; private set; }
    MovementModifierManager mods = new();

    public Vector2 moveInput { get; private set; }  // Để đảm bảo an toàn cho việc chỉnh sửa ngoài phạm vi
    Vector3 planarVel;
    float yVel;
    float staminaRegenCooldown;

    Transform camTr; // cache camera
    float _viewYawFromInput = float.NaN;

    [Header("Body Yaw")]
    [SerializeField] private float bodyTurnSpeed = 360f;    // độ/giây quay thân

    float _bodyYaw;
    bool _bodyYawInitialized;

    // NEW: nhận ADS từ combat
    bool _isADSExternal;

    // ====== NEW: Jump Buffer & Coyote ======
    [Header("Jump Settings")]
    [Tooltip("Thời gian cho phép nhảy ngay sau khi vừa rời đất (s).")]
    public float coyoteTime = 0.10f;
    [Tooltip("Thời gian nhớ 1 lần bấm Jump gần nhất (s).")]
    public float jumpBufferTime = 0.12f;

    float coyoteTimer = 0f;
    float jumpBufferTimer = 0f;
    bool jumpQueued = false; // chỉ là cờ 1 lần, không cộng dồn
    bool groundedStable;
    const float GROUND_PROBE = 0.15f;

#if FUSION_WEAVER
    private NetworkObject _no;
    private bool IsPredictedClient => _no != null && _no.Runner != null && _no.Runner.IsRunning
                                     && _no.HasInputAuthority && !_no.HasStateAuthority;
#endif


    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (cc == null)
        {
            Debug.LogError("[PlayerMovementCtrl] Missing CharacterController!", this);
            enabled = false;
            return;
        }

#if FUSION_WEAVER
        _no = GetComponentInParent<NetworkObject>();
#endif

        cc.minMoveDistance = 0f;

        if (config == null)
        {
            Debug.LogWarning("MovementConfig chưa gán. Tạo tạm để chạy.");
            config = ScriptableObject.CreateInstance<MovementConfig>();
        }

        if (stats == null)
            stats = new MovementStats();

        stats.LoadFromConfig(config);
        stamina = stats.ResolveStaminaMax();

        var cam = Camera.main;
        camTr = cam != null ? cam.transform : null;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // Nếu core được driver điều khiển (multiplayer, hoặc wrapper ngoài), thì KHÔNG tự tick.
        if (drivenExternally)
            return;

#if FUSION_WEAVER
        // Nếu có NetworkRunner trong scene thì driver Fusion sẽ gọi simulate, không tick ở đây.
        if (Fusion.NetworkRunner.GetRunnerForScene(gameObject.scene) != null)
            return;
#endif

        // Single mode fallback
        Simulate(Time.deltaTime);
    }

    public void Simulate(float dt)
    {
        // ----- Mods & Stats -----
        mods.Tick(dt);
        stats.LoadFromConfig(config);
        mods.BakeInto(stats, config);

        // ----- Align yaw: thân luôn quay mượt về hướng nhìn -----
        float viewYaw;

        // Ưu tiên yaw từ input (MP)
        if (!float.IsNaN(_viewYawFromInput))
        {
            viewYaw = _viewYawFromInput;
        }
        else
        {
            // Fallback singleplayer: lấy yaw từ camera
            if (camTr == null)
            {
                var cam = Camera.main;
                if (cam != null) camTr = cam.transform;
            }

            if (camTr != null)
                viewYaw = camTr.eulerAngles.y;
            else
                viewYaw = transform.eulerAngles.y; // fallback cuối
        }

        // Init bodyYaw lần đầu
        if (!_bodyYawInitialized)
        {
            _bodyYaw = transform.eulerAngles.y;
            _bodyYawInitialized = true;
        }

        if (!_freezeBodyYaw)
        {
            // Thân quay dần về viewYaw với tốc độ bodyTurnSpeed (độ/giây)
            float maxDelta = bodyTurnSpeed * dt;
            _bodyYaw = Mathf.MoveTowardsAngle(_bodyYaw, viewYaw, maxDelta);

            // Apply lên transform: chân / locomotion sẽ quay theo bodyYaw
            transform.rotation = Quaternion.Euler(0f, _bodyYaw, 0f);
        }

        // ====== NEW: Ground check + Coyote countdown ======
        bool ccGrounded = cc.isGrounded;
        bool closeToGround = false;
        if (!ccGrounded)
        {
            var ray = new Ray(transform.position + Vector3.up * 0.05f, Vector3.down);
            closeToGround = Physics.SphereCast(ray, cc.radius * 0.9f, out _, GROUND_PROBE, ~0, QueryTriggerInteraction.Ignore);
        }
        groundedStable = ccGrounded || closeToGround;

        if (groundedStable)
        {
            coyoteTimer = coyoteTime;
            if (yVel < 0f) yVel = -2f; // sticky push nhỏ để dính đất
        }
        else
        {
            coyoteTimer -= dt;
        }

        // ====== NEW: Jump buffer countdown ======
        if (jumpBufferTimer > 0f) jumpBufferTimer -= dt;

        // ===== HARD EXIT SPRINT (chống kẹt sprint do stamina/input bị lệch frame) =====
        if (state == MoveState.Sprinting)
        {
            bool mustExitSprint =
                !wantsSprint ||
                stamina <= 0f ||
                !groundedStable ||
                IsADS() ||
                moveInput.sqrMagnitude < 0.0001f;

            if (mustExitSprint)
                ChangeState(moveInput.sqrMagnitude > 0.0001f ? MoveState.Walking : MoveState.Idle);
        }


        // ----- FSM Transition -----
        if (IsStunned())
        {
            ChangeState(MoveState.Stunned);
        }
        else if (!groundedStable)
        {
            ChangeState(yVel > 0.1f ? MoveState.Jumping : MoveState.Falling);
        }
        else
        {
            bool hasInput = moveInput.sqrMagnitude > 0.0001f;
            bool allowSprint = wantsSprint && CanStartSprint() && hasInput && !IsADS();

            if (wantsCrouch)
                ChangeState(MoveState.Crouching);
            else if (allowSprint)
                ChangeState(MoveState.Sprinting);
            else if (hasInput)
                ChangeState(MoveState.Walking);
            else
                ChangeState(MoveState.Idle);

            // NOTE: KHÔNG còn nhảy ngay tại đây bằng jumpPressedThisFrame
            // vì ta dùng buffer & coyote để kích hoạt nhảy đúng thời điểm bên dưới.
            jumpPressedThisFrame = false; // vẫn reset flag cũ để tương thích
        }

        // ====== NEW: Tiêu thụ buffer để nhảy (gating theo coyote) ======
        TryConsumeJump();

        // ----- FSM Update -----
        UpdateState(dt);
    }

    public void SetInput(Vector2 move, bool sprint, bool crouch, bool jumpDown, float viewYaw)
    {
        moveInput = move;
        wantsSprint = sprint;
        wantsCrouch = crouch;
        _viewYawFromInput = viewYaw;

        // ====== NEW: Ghi nhận 1 lần bấm Jump vào buffer ======
        if (jumpDown)
        {
            jumpQueued = true;
            jumpBufferTimer = jumpBufferTime;
        }

        // giữ compat cho code khác nếu đang đọc biến này ở nơi khác
        if (jumpDown) jumpPressedThisFrame = true;
    }

    // ---------- FSM Core ----------
    void ChangeState(MoveState next)
    {
        if (state == next) return;
        OnExitState(state);
        _lastState = state;
        state = next;
        OnEnterState(state);
    }

    void OnEnterState(MoveState s)
    {
        switch (s)
        {
            case MoveState.Sprinting:
                staminaRegenCooldown = config.staminaRegenDelay;
                break;

            case MoveState.Jumping:
                // ====== CHỈNH: bỏ điều kiện isGrounded để hỗ trợ coyote ======
                yVel = Mathf.Sqrt(stats.ResolveJumpHeight() * -2f * stats.ResolveGravity());
                break;
        }
    }

    void OnExitState(MoveState s)
    {
        // clear flags/FX nếu cần
    }

    void UpdateState(float dt)
    {
        // Hướng nhập theo hướng nhìn (camera / viewYaw), KHÔNG phụ thuộc bodyYaw
        Vector3 basisForward;
        Vector3 basisRight;

        // Ưu tiên dùng viewYaw từ input (MP)
        if (!float.IsNaN(_viewYawFromInput))
        {
            Quaternion viewRot = Quaternion.Euler(0f, _viewYawFromInput, 0f);
            basisForward = viewRot * Vector3.forward;
            basisRight = viewRot * Vector3.right;
        }
        else
        {
            // Fallback singleplayer: dùng camera
            if (camTr == null)
            {
                var cam = Camera.main;
                if (cam != null) camTr = cam.transform;
            }

            if (camTr != null)
            {
                Vector3 fwd = camTr.forward;
                fwd.y = 0f;
                basisForward = fwd.normalized;
                // vector vuông góc sang phải với fwd
                basisRight = new Vector3(basisForward.z, 0f, -basisForward.x);
            }
            else
            {
                // Fallback cuối cùng
                basisForward = transform.forward;
                basisRight = transform.right;
            }
        }

        // Từ input (x,y) → vector thế giới theo hướng nhìn
        Vector3 inputDir = basisRight * moveInput.x + basisForward * moveInput.y;
        if (inputDir.sqrMagnitude > 1e-4f) inputDir.Normalize();


        // Gravity (dựa vào groundedStable để tránh rung)
        if (groundedStable)
        {
            if (yVel < 0f) yVel = -2f; // giữ dính đất, KHÔNG cộng gravity khung này
        }
        else
        {
            yVel += stats.ResolveGravity() * dt; // chỉ rơi khi thật sự air
        }


        float control = cc.isGrounded ? 1f : stats.ResolveAirControl();
        float targetSpeed = 0f;

        switch (state)
        {
            case MoveState.Idle:
                targetSpeed = 0f;
                StaminaRegen(dt);
                break;

            case MoveState.Walking:
                targetSpeed = stats.ResolveSpeed(false);
                StaminaRegen(dt);
                break;

            case MoveState.Sprinting:
                // nếu đang ADS giữa chừng, rớt về Walking
                if (IsADS())
                {
                    ChangeState(moveInput.sqrMagnitude > 0.0001f ? MoveState.Walking : MoveState.Idle);
                    targetSpeed = stats.ResolveSpeed(false);
                    StaminaRegen(dt);
                }
                else
                {
                    targetSpeed = stats.ResolveSpeed(true);
                    StaminaDrain(dt, inputDir);
                    if (!wantsSprint || !groundedStable || stamina <= 0f)
                        ChangeState(moveInput.sqrMagnitude > 0.0001f ? MoveState.Walking : MoveState.Idle);
                }
                break;

            case MoveState.Crouching:
                targetSpeed = stats.ResolveSpeed(false) * 0.5f;
                StaminaRegen(dt);
                break;

            case MoveState.Jumping:
            case MoveState.Falling:
                targetSpeed = stats.ResolveSpeed(wantsSprint && !IsADS());
                StaminaRegen(dt);
                break;

            case MoveState.Stunned:
                targetSpeed = 0f;
                StaminaRegen(dt, 0.5f);
                break;
        }

        // Tăng tốc mượt
        float accel = stats.ResolveAccel();
        Vector3 targetPlanar = inputDir * targetSpeed * control;
        planarVel = Vector3.MoveTowards(planarVel, targetPlanar, accel * dt);

        // Move
        Vector3 vel = planarVel;
        vel.y = yVel;

        WorldVelocity = vel;

        // (Nếu bạn muốn chỉ StateAuthority/LocalOwner được Move thì mở block Fusion ở đây)
        if (cc != null && cc.enabled)
            cc.Move(vel * dt);
    }

    // ====== NEW: tiêu thụ jump buffer theo coyote ======
    void TryConsumeJump()
    {
        bool canJump = (coyoteTimer > 0f);
        bool hasBuffered = (jumpQueued && jumpBufferTimer > 0f);

        if (canJump && hasBuffered)
        {
            // set nhảy đúng 1 lần
            jumpQueued = false;
            jumpBufferTimer = 0f;
            coyoteTimer = 0f;

            // kích hoạt state Jumping -> OnEnterState sẽ set yVel
            ChangeState(MoveState.Jumping);
        }
        else if (!hasBuffered)
        {
            // không còn buffer -> bảo đảm sạch cờ
            jumpQueued = false;
        }
    }

    // ---------- Stamina ----------
    bool CanStartSprint()
    {
        return stamina >= config.minSprintToStart && groundedStable && !IsStunned();
    }

    void StaminaDrain(float dt, Vector3 inputDir)
    {
        if (inputDir.sqrMagnitude > 0.0001f)
        {
            stamina -= stats.ResolveStaminaDrain() * dt;
            stamina = Mathf.Max(0f, stamina);
            staminaRegenCooldown = config.staminaRegenDelay;
        }
        else
        {
            StaminaRegen(dt);
        }
    }

    void StaminaRegen(float dt, float multiplier = 1f)
    {
        if (staminaRegenCooldown > 0f) { staminaRegenCooldown -= dt; return; }
        stamina += stats.ResolveStaminaRegen() * multiplier * dt;
        stamina = Mathf.Min(stamina, stats.ResolveStaminaMax());
    }

    // ---------- Hooks ----------
    bool IsStunned() => false;

    // NEW: được interactor/weapon thông báo
    public void SetADSExternal(bool value) => _isADSExternal = value;
    bool IsADS() => _isADSExternal;

    // API modifiers
    public string ApplyModifier(MovementModifier mod) { mods.Apply(mod); return mod.id; }
    public void RemoveModifier(string id) { mods.RemoveById(id); }
    public void RemoveModifiersFrom(string source) { mods.RemoveBySource(source); }

    // --- External control (downed / dead / revive) ---
    public void SetBodyYawFrozen(bool frozen)
    {
        _freezeBodyYaw = frozen;
    }

}
