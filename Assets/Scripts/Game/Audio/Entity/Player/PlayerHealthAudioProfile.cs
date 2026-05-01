using UnityEngine;
using TT; // AudioEventSO

[CreateAssetMenu(menuName = "TT/Player/Health Audio Profile")]
public class PlayerHealthAudioProfile : ScriptableObject
{
    // ===== HURT =====
    [Header("Hurt")]
    [Tooltip("Các clip khi player bị thương (zombie đánh, trap, vv). Sẽ pick random.")]
    public AudioEventSO[] hurtClips;

    [Tooltip("Thời gian tối thiểu giữa 2 tiếng hurt (chống spam).")]
    public float minHurtInterval = 0.35f;

    [Tooltip("Chỉ play hurt nếu damage > giá trị này.")]
    public float minDamageToPlayHurt = 1.0f;

    // ===== FATAL (Downed / Dead) =====
    [Header("Fatal (Downed / Dead)")]
    [Tooltip("Tiếng vocal khi vào trạng thái Downed (MP). Nếu null sẽ fallback sang deathSFX.")]
    public AudioEventSO downedSFX;

    [Tooltip("Tiếng vocal khi chết (SP: Alive->Dead trực tiếp). Nếu null sẽ fallback từ downedSFX.")]
    public AudioEventSO deathSFX;

    // ===== LOW HP BREATHING =====
    [Header("Low HP Breathing")]
    [Tooltip("Tiếng thở gấp/thở dồn dập khi HP thấp (3D attached).")]
    public AudioEventSO lowHpBreathSFX;

    [Range(0f, 1f)]
    [Tooltip("Ngưỡng HP/Max dưới mức này sẽ coi là 'low HP' (ví dụ 0.3 = 30%).")]
    public float lowHpThreshold = 0.3f;

    [Tooltip("Khoảng thời gian giữa 2 tiếng thở khi HP thấp (min, max).")]
    public Vector2 lowHpBreathInterval = new Vector2(2.5f, 4.5f);

    /// <summary>Pick random hurt clip, hoặc null nếu không có.</summary>
    public AudioEventSO GetRandomHurt()
    {
        if (hurtClips == null || hurtClips.Length == 0)
            return null;

        int idx = Random.Range(0, hurtClips.Length);
        return hurtClips[idx];
    }
}
