using UnityEngine;
#if FUSION_WEAVER || FUSION2
using Fusion;
#endif

/// CoD-style Health Regen cho cả SP & MP (Fusion):
/// - Authority-only (StateAuthority) khi ở MP
/// - Tick rời rạc (mỗi tick cộng +tickAmount)
/// - Delay sau khi bị damage
/// - SP không Runner vẫn hoạt động bằng timer nội bộ (tick cố định, không phụ thuộc FPS)
[DisallowMultipleComponent]
[RequireComponent(typeof(DamageableHealth))]
public class HealthRegen : /* NetworkBehaviour nếu có Fusion, ngược lại MonoBehaviour */
#if FUSION_WEAVER || FUSION2
    NetworkBehaviour
#else
    MonoBehaviour
#endif
{
    [Header("Delay & Tick")]
    [Tooltip("Sau khi bị dính damage, chờ bấy nhiêu giây rồi mới bắt đầu hồi")]
    public float startDelaySeconds = 2.0f;

    [Tooltip("Khoảng thời gian giữa các tick hồi")]
    public float tickIntervalSeconds = 1.5f;

    [Tooltip("Lượng máu hồi mỗi tick (số nguyên)")]
    public int tickAmount = 15;

    [Header("Rules")]
    [Tooltip("Không cho vượt quá maxHealth")]
    public bool clampToMaxHealth = true;

    [Tooltip("Bắt buộc phải từng bị damage rồi mới được hồi (chuẩn CoD)")]
    public bool requireDamageBeforeRegen = true;

    [Header("Debug")]
    public bool printLogs = false;

    private DamageableHealth _hp;
    private PlayerLifeController _life;

    // Cờ đã từng bị damage (để bắt đầu delay)
    private bool _armedByDamage = false;

    // Mỗi lần dính damage tăng serial -> để hủy timer hiện tại
    private int _damageSerial = 0;

    // --- MP (Fusion) timers ---
#if FUSION_WEAVER || FUSION2
    private TickTimer _delayTimer; // tạo từ startDelaySeconds
    private TickTimer _tickTimer;  // tạo từ tickIntervalSeconds
#endif

    // --- SP timers (khi không có Runner) ---
    private float _spDelayRemain = 0f;
    private float _spTickRemain = 0f;

    void Awake()
    {
        _hp = GetComponent<DamageableHealth>();
        _life = GetComponent<PlayerLifeController>();
        if (_hp == null)
        {
            Debug.LogError("[Regen] Missing DamageableHealth.");
            enabled = false;
            return;
        }
    }

    void OnEnable()
    {
        DamageSystem.OnReady += OnDamageSystemReady;

        if (DamageSystem.Instance != null)
            DamageSystem.Instance.OnDamageApplied += OnDamageApplied;
        ResetAllTimers(); // bảo đảm sạch timer khi bật

    //  Debug.Log("[Regen] OnEnable => DS instance = " + DamageSystem.Instance);
    }

    void OnDisable()
    {
        DamageSystem.OnReady -= OnDamageSystemReady;
        if (DamageSystem.Instance != null)
            DamageSystem.Instance.OnDamageApplied -= OnDamageApplied;
    }

    // ======== DAMAGE HOOK ========
    private void OnDamageApplied(DamageEvent e, DamageResult result)
    {
        // Chỉ quan tâm khi damage đã áp và nạn nhân là object này
        if (!result.isApplied || e.victimGO != gameObject) return;

        _damageSerial++;               // đánh dấu "phiên" mới
        _armedByDamage = true;         // đã bị damage => cho phép regen sau delay
        ResetAllTimersAfterDamage();   // bắt đầu lại delay, hủy tick đang chạy

        if (printLogs) Debug.Log("[Regen] Took damage -> stop regen & restart delay.", this);
    }

    private void OnDamageSystemReady(DamageSystem ds)
    {
        ds.OnDamageApplied += OnDamageApplied;
    }


    // ======== AUTHORITY CHECK ========
    private bool HasAuthorityOrSP()
    {
#if FUSION_WEAVER || FUSION2
        // Có Runner => chỉ StateAuthority chạy logic
        if (Runner != null)
            return Object != null && Object.HasStateAuthority;
#endif
        // Không có Runner => SP coi như authority
        return true;
    }


    // ======== APPLY ONE TICK ========
    void ApplyOneRegenTick()
    {
        float max = _hp.maxHealth;
        float cur = _hp.currentHealth;

        if (clampToMaxHealth && cur >= max)
            return;

        float add = tickAmount; // số nguyên
        float newHealth = clampToMaxHealth ? Mathf.Min(cur + add, max) : (cur + add);

        // dùng SetCurrentFromNet để vừa set HP vừa bắn OnHpChanged
        int newInt = Mathf.RoundToInt(newHealth);
        _hp.SetCurrentFromNet(newInt);

        if (printLogs)
            Debug.Log($"[Regen] +{(int)add} -> {newHealth:0}/{max:0}", this);
    }


    // ======== COMMON STEP ========
    private void StepAuthorityLogic(float deltaTimeForSP)
    {
        // Nếu chưa từng bị damage và yêu cầu phải bị damage trước -> không làm gì
        if (requireDamageBeforeRegen && !_armedByDamage)
            return;

        // Full máu thì thôi
        if (clampToMaxHealth && _hp.currentHealth >= _hp.maxHealth)
            return;

        if (_life != null && _life.state != LifeState.Alive)
            return;

        if (requireDamageBeforeRegen && !_armedByDamage)
            return;

#if FUSION_WEAVER || FUSION2
        if (Runner != null)
        {
            // --- MP bằng TickTimer ---
            // 1) Delay phase
            if (_delayTimer.IsRunning)
            {
                if (!_delayTimer.Expired(Runner)) return;
                // delay hết -> rơi xuống tick phase
            }

            // 2) Tick phase
            if (!_tickTimer.IsRunning)
            {
                _tickTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.01f, tickIntervalSeconds));
                return;
            }

            if (_tickTimer.Expired(Runner))
            {
                ApplyOneRegenTick();

                // Nếu vẫn còn thiếu máu -> làm lại tick; nếu đã full -> dừng
                if (!clampToMaxHealth || _hp.currentHealth < _hp.maxHealth)
                {
                    _tickTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0.01f, tickIntervalSeconds));
                }
            }
            return;
        }
#endif
        // --- SP fallback: timer nội bộ, deltaTimeForSP đến từ Update ---
        // 1) Delay
        if (_spDelayRemain > 0f)
        {
            _spDelayRemain -= deltaTimeForSP;
            return;
        }

        // 2) Tick
        _spTickRemain -= deltaTimeForSP;
        if (_spTickRemain <= 0f)
        {
            ApplyOneRegenTick();
            if (!clampToMaxHealth || _hp.currentHealth < _hp.maxHealth)
                _spTickRemain = Mathf.Max(0.01f, tickIntervalSeconds);
        }
    }

    // ======== TIMER RESET HELPERS ========
    private void ResetAllTimers()
    {
#if FUSION_WEAVER || FUSION2
        _delayTimer = default;
        _tickTimer = default;
#endif
        _spDelayRemain = 0f;
        _spTickRemain = 0f;
    }

    private void ResetAllTimersAfterDamage()
    {
        // Sau khi bị damage: reset delay và huỷ tick hiện tại
#if FUSION_WEAVER || FUSION2
        if (Runner != null)
        {
            _delayTimer = TickTimer.CreateFromSeconds(Runner, Mathf.Max(0f, startDelaySeconds));
            _tickTimer = default; // huỷ tick
            return;
        }
#endif
        _spDelayRemain = Mathf.Max(0f, startDelaySeconds);
        _spTickRemain = Mathf.Max(0.01f, tickIntervalSeconds); // sẽ được sử dụng sau khi delay hết
    }

    // ======== MAIN LOOPS ========
#if FUSION_WEAVER || FUSION2
    public override void FixedUpdateNetwork()
    {
        if (!HasAuthorityOrSP()) return;
        // deltaTime cho SP không dùng ở đây (MP dùng TickTimer), nhưng truyền 0 an toàn
        StepAuthorityLogic(0f);
    }
#endif

    void Update()
    {
        // Chỉ SP (Runner == null) dùng Update làm “đồng hồ” tick cố định
#if FUSION_WEAVER || FUSION2
        if (Runner != null) return; // MP: đã chạy trong FixedUpdateNetwork
#endif
        if (!HasAuthorityOrSP()) return;
        StepAuthorityLogic(Time.deltaTime);
    }
}
