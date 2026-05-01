using UnityEngine;
using TT; // AudioEventSO, AudioEventCollection

[CreateAssetMenu(menuName = "TT/AI/Zombie Audio Profile")]
public class ZombieAudioProfile : ScriptableObject
{
    // ================= FOOTSTEP =================
    [Header("Footstep")]
    [Tooltip("Footstep mặc định nếu không có SurfaceType match.")]
    public AudioEventSO defaultFootstep;

    [System.Serializable]
    public struct FootstepSurfaceEvent
    {
        public SurfaceType surface;
        public AudioEventSO eventSO;
    }

    [Tooltip("Footstep theo loại mặt sàn (SurfaceType).")]
    public FootstepSurfaceEvent[] surfaceFootsteps;

    [Header("Footstep Behaviour")]
    [Tooltip("Origin raycast = position + up * height.")]
    public float footRaycastHeight = 1.0f;

    [Tooltip("Độ dài raycast xuống dưới.")]
    public float footRaycastDistance = 1.5f;

    [Tooltip("Chỉ raycast vào các layer này (thường là Ground/Environment).")]
    public LayerMask footstepMask = ~0;

    [Range(0f, 1f)]
    [Tooltip("Xác suất chơi footstep mỗi lần animation event được gọi.")]
    public float footstepPlayChance = 0.7f;

    [Tooltip("Chỉ chơi footstep nếu Listener nằm trong khoảng cách này.")]
    public float maxFootstepDistance = 25f;

    [Tooltip("Tối thiểu bao nhiêu giây giữa 2 footstep của CÙNG MỘT con zombie.")]
    public float minFootstepInterval = 0.20f;

    // ================= AGGRO SHOUT =================
    [Header("Aggro Shout")]
    [Tooltip("Các clip hét khi zombie bắt đầu Chase.")]
    public AudioEventSO[] aggroShouts;
    public AudioEventCollection aggroCollection;

    [Range(0f, 1f)]
    [Tooltip("Xác suất hét khi chuyển sang trạng thái Chase.")]
    public float aggroShoutChance = 0.7f;

    [Tooltip("Chỉ hét nếu Listener nằm trong khoảng cách này.")]
    public float aggroMaxDistance = 40f;

    // ================= CHASE BARK =================
    [Header("Chase Bark (đang rượt đuổi)")]
    [Tooltip("Các clip vocal trong lúc zombie đang dí player.")]
    public AudioEventSO[] chaseBarks;
    public AudioEventCollection chaseBarkCollection;

    [Tooltip("Khoảng thời gian (min,max) giữa 2 lần kêu của MỘT con zombie khi đang Chase.")]
    public Vector2 chaseBarkInterval = new Vector2(6f, 12f);

    [Tooltip("Chỉ kêu nếu Listener nằm trong khoảng cách này.")]
    public float chaseMaxDistance = 35f;

    // ================= ATTACK =================
    [Header("Attack")]
    [Tooltip("Tiếng zombie kêu khi vung tay tấn công (attack vocal).")]
    public AudioEventSO[] attackVocals;
    public AudioEventCollection attackVocalCollection;

    [Tooltip("Whoosh / claw swipe khi vung tay (dùng cho cả hit/miss).")]
    public AudioEventSO[] attackSwings;
    public AudioEventCollection attackSwingCollection;

    [Tooltip("Tiếng móng trúng player (claw hit vào flesh).")]
    public AudioEventSO[] attackHitPlayer;
    public AudioEventCollection attackHitPlayerCollection;

    [Range(0f, 1f)]
    [Tooltip("Xác suất kêu vocal khi bắt đầu attack.")]
    public float attackVocalChance = 0.5f;

    [Range(0f, 1f)]
    [Tooltip("Xác suất chơi swing SFX khi event vung tay được gọi.")]
    public float attackSwingChance = 1.0f;

    [Range(0f, 1f)]
    [Tooltip("Xác suất chơi hit SFX khi thật sự đánh trúng player.")]
    public float attackHitPlayerChance = 1.0f;

    [Tooltip("Khoảng cách tối đa cho các tiếng attack (0 = dùng chaseMaxDistance).")]
    public float attackMaxDistanceOverride = 0f;

    // ================= HIT REACTION (Zombie bị bắn) =================
    [Header("Hit Reaction")]
    [Tooltip("Tiếng zombie kêu khi bị trúng đạn / đòn đánh.")]
    public AudioEventSO[] hitReactions;
    public AudioEventCollection hitReactionCollection;

    [Header("Death")]
    [Tooltip("Tiếng zombie kêu khi chết (rên, gào lần cuối).")]
    public AudioEventSO[] deathVocals;
    public AudioEventCollection deathVocalCollection;

    [Range(0f, 1f)]
    [Tooltip("Xác suất play death vocal khi zombie chết.")]
    public float deathVocalChance = 0.7f;

    [Tooltip("Tiếng xác rơi xuống đất trong anim chết.")]
    public AudioEventSO[] bodyFallSfx;
    public AudioEventCollection bodyFallCollection;

    [Tooltip("Khoảng cách tối đa cho tiếng death (0 = dùng vocalMaxDistanceOverride/chaseMaxDistance).")]
    public float deathMaxDistanceOverride = 0f;



    // ================= VOCAL COOLDOWN =================
    [Header("Per-Zombie Vocal Cooldown")]
    [Tooltip("Tối thiểu bao nhiêu giây giữa 2 tiếng kêu bất kỳ (Aggro + Chase) của MỘT zombie.")]
    public float minLocalVoiceInterval = 6f;

    [Tooltip("Nếu >0 sẽ dùng làm max distance chung cho mọi vocal, override aggro/chase distance.")]
    public float vocalMaxDistanceOverride = 0f;
}
