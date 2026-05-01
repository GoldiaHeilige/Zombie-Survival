using UnityEngine;
using TT; // assumes AudioEventSO etc live under TT namespace

[CreateAssetMenu(menuName = "TT/Player/Movement Audio Profile")]
public class PlayerMovementAudioProfile : ScriptableObject
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
    public FootstepSurfaceEvent[] surfaceFootsteps = new FootstepSurfaceEvent[0];

    [Header("Footstep Raycast")]
    [Tooltip("Origin raycast = position + up * height.")]
    public float footRaycastHeight = 1.0f;

    [Tooltip("Độ dài raycast xuống dưới.")]
    public float footRaycastDistance = 2.0f;

    [Tooltip("Chỉ raycast vào các layer này (thường là Ground/Environment).")]
    public LayerMask footstepMask = ~0;

    [Range(0f, 1f)]
    [Tooltip("Xác suất chơi footstep mỗi lần animation event được gọi.")]
    public float footstepPlayChance = 1.0f;

    [Tooltip("Chỉ chơi footstep nếu Listener nằm trong khoảng cách này. <= 0 = luôn chơi.")]
    public float maxFootstepDistance = 25f;

    [Tooltip("Tối thiểu bao nhiêu giây giữa 2 footstep của CÙNG MỘT player.")]
    public float minFootstepInterval = 0.20f;

    // ================= CROUCH / STAND =================
    [Header("Crouch / Stand")]
    [Tooltip("Tiếng lục cục / vải khi NGỒI xuống.")]
    public AudioEventSO crouchDownSfx;

    [Tooltip("Tiếng lục cục / vải khi ĐỨNG dậy.")]
    public AudioEventSO standUpSfx;

    // ================= RATTLE (Walk / Sprint) =================
    [Header("Rattle (đồ đạc trên người)")]
    [Tooltip("Tiếng rattle nhẹ khi đi bộ.")]
    public AudioEventSO walkRattleSfx;

    [Range(0f, 1f)]
    [Tooltip("Xác suất rattle mỗi footstep khi đang Walking.")]
    public float walkRattleChance = 0.1f;

    [Tooltip("Tiếng rattle mạnh hơn khi sprint.")]
    public AudioEventSO sprintRattleSfx;

    [Range(0f, 1f)]
    [Tooltip("Xác suất rattle mỗi footstep khi đang Sprinting.")]
    public float sprintRattleChance = 0.4f;

    // ================= JUMP / LAND =================
    [Header("Jump / Land")]
    [Tooltip("Tiếng phát khi bắt đầu nhảy (bật khỏi đất). 3D attached.")]
    public AudioEventSO jumpStartSfx;

    [Tooltip("Tiếng chạm đất nhẹ (soft landing). Played at hit point).")]
    public AudioEventSO landSoftSfx;

    [Tooltip("Tiếng chạm đất mạnh (hard landing). Played at hit point).")]
    public AudioEventSO landHardSfx;

    [Tooltip("Ngưỡng vertical velocity (negative) để coi là hard landing. Ví dụ -6 => va chạm mạnh nếu tốc độ rơi < -6")]
    public float landHardVelocity = -6f;

    /// <summary>Get footstep event for SurfaceType, fallback default.</summary>
    public AudioEventSO GetFootstepForSurface(SurfaceType surface)
    {
        if (surfaceFootsteps != null)
        {
            for (int i = 0; i < surfaceFootsteps.Length; i++)
            {
                if (surfaceFootsteps[i].surface == surface && surfaceFootsteps[i].eventSO != null)
                    return surfaceFootsteps[i].eventSO;
            }
        }
        return defaultFootstep;
    }
}
