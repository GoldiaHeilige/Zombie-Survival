// AudioEvent.cs
using UnityEngine;

namespace TT
{
    /// <summary>
    /// 1 "sự kiện âm thanh" có thể có nhiều biến thể clip, category, 3D/2D, khoảng cách,...
    /// Dùng làm SO để designer kéo thả.
    /// </summary>
    [CreateAssetMenu(menuName = "TT/Audio/Audio Event", fileName = "AudioEvent_")]
    public class AudioEventSO : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("ID số dùng cho network / code. Cố gắng unique.")]
        public int eventId = 0;

        [Tooltip("Tên logic (debug / code). Không bắt buộc trùng tên asset.")]
        public string eventName = "unnamed";

        [Header("Category")]
        public AudioCategory category = AudioCategory.SFX;

        [Header("Clips")]
        [Tooltip("Có thể nhiều clip để random cho đỡ nhàm.")]
        public AudioClip[] clips;

        [Header("Playback")]
        [Range(0f, 1f)]
        public float volume = 1f;

        [Tooltip("Pitch trung bình.")]
        public float pitch = 1f;

        [Tooltip("Random thêm hoặc bớt quanh pitch, ví dụ 0.05.")]
        [Range(0f, 0.5f)]
        public float pitchRandomRange = 0.05f;

        [Tooltip("Có lặp lại không (chủ yếu cho music/ambient).")]
        public bool loop = false;

        [Header("3D Settings")]
        [Tooltip("Nếu true thì dùng 3D (spatialBlend=1). Nếu false thì 2D.")]
        public bool is3D = true;

        [Min(0f)] public float minDistance = 1f;
        [Min(0.1f)] public float maxDistance = 40f;

        [Tooltip("Override rolloff bằng custom curve nếu cần (optional).")]
        public AnimationCurve customRolloff;

        [Header("Mixer override (optional)")]
        [Tooltip("Nếu null, AudioManager sẽ tự chọn group theo category.")]
        public UnityEngine.Audio.AudioMixerGroup overrideMixerGroup;

        /// <summary>Lấy 1 clip random từ list (hoặc null nếu không có).</summary>
        public AudioClip GetRandomClip()
        {
            if (clips == null || clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];
            int idx = Random.Range(0, clips.Length);
            return clips[idx];
        }

        /// <summary>Random pitch quanh pitch trung bình.</summary>
        public float GetRandomPitch()
        {
            if (pitchRandomRange <= 0f) return pitch;
            float offset = Random.Range(-pitchRandomRange, pitchRandomRange);
            return pitch + offset;
        }
    }
}
