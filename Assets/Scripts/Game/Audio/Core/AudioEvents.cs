// AudioEvents.cs
using UnityEngine;

#if FUSION_WEAVER
using Fusion;
#endif

namespace TT
{
    /// <summary>
    /// Helper tĩnh để gameplay gọi audio mà không phải quan tâm SP / MP / RPC.
    /// - 2D (UI, FP) → luôn local.
    /// - 3D world → SP: play trực tiếp; MP: host gọi RPC qua FusionAudioBridge.
    /// </summary>
    public static class AudioEvents
    {
        /// <summary>SP hay MP? (dựa trên GameSession bạn đang dùng).</summary>
        private static bool IsSingle =>
            GameSession.Mode == AppPlayMode.Single;

        private static AudioManager Mgr => AudioManager.Instance;

        // ===================== 2D / LOCAL =====================

        /// <summary>UI click, HUD, game over, v.v.</summary>
        public static void PlayUI(int eventId) => Mgr?.Play2D(eventId);
        public static void PlayUI(string name) => Mgr?.Play2D(name);

        /// <summary>SFX 2D bình thường (nếu bạn thích dùng).</summary>
        public static void Play2D(int eventId) => Mgr?.Play2D(eventId);
        public static void Play2D(string name) => Mgr?.Play2D(name);

        /// <summary>Tiếng first-person (súng tay mình, reload, bước chân local...).</summary>
        public static void PlayFirstPerson(int eventId) => Mgr?.Play2D(eventId);
        public static void PlayFirstPerson(string name) => Mgr?.Play2D(name);

        // ===================== 2D / GLOBAL (Host broadcast cho mọi client) =====================

        /// <summary>
        /// UI 2D nhưng muốn TẤT CẢ client nghe (round start/end, game over...).
        /// Chỉ host nên gọi; SP thì play local bình thường.
        /// </summary>
        public static void PlayUiGlobal(int eventId)
        {
            if (Mgr == null) return;

            if (IsSingle)
            {
                Mgr.Play2D(eventId);
                return;
            }

#if FUSION_WEAVER
            if (!FusionAudioBridge.IsReadyHost)
            {
                // chưa có bridge / chưa spawn → ít nhất host vẫn nghe
                Mgr.Play2D(eventId);
                return;
            }

            FusionAudioBridge.Instance.Host_PlayUI2D(eventId);
#else
    Mgr.Play2D(eventId);
#endif
        }

        public static void PlayUiGlobal(string name)
        {
            if (Mgr == null) return;

            if (IsSingle)
            {
                Mgr.Play2D(name);
                return;
            }

#if FUSION_WEAVER
            if (!FusionAudioBridge.IsReadyHost)
            {
                Mgr.Play2D(name);
                return;
            }

            FusionAudioBridge.Instance.Host_PlayUI2D(name);
#else
    Mgr.Play2D(name);
#endif
        }


        // ===================== 3D / WORLD =====================

        /// <summary>
        /// Play 3D world sound tại 1 điểm:
        /// - SP: gọi AudioManager trực tiếp
        /// - MP: chỉ host mới được gọi; host RPC cho tất cả client.
        /// shooterNO dùng để client shooter bỏ qua world sound (tránh double).
        /// </summary>
        public static void PlayWorld3D(int eventId, Vector3 position
#if FUSION_WEAVER
            , NetworkObject shooterNO = null
#endif
        )
        {
            if (Mgr == null) return;

            // Singleplayer → play thẳng
            if (IsSingle)
            {
                Mgr.Play3DAtPoint(eventId, position);
                return;
            }

#if FUSION_WEAVER
            // Multiplayer
            if (!FusionAudioBridge.IsReadyHost)
            {
                // fallback: nếu chưa có bridge / không phải host thì ít nhất vẫn có tiếng local
                Mgr.Play3DAtPoint(eventId, position);
                return;
            }

            FusionAudioBridge.Instance.Host_PlayWorld3D(eventId, position, shooterNO);
#else
            // Build không có Fusion: play local
            Mgr.Play3DAtPoint(eventId, position);
#endif
        }

#if FUSION_WEAVER
        public static void PlayWorld3D(string name, Vector3 position, NetworkObject shooterNO = null)
        {
            if (Mgr == null) return;

            if (!Mgr.TryGetEvent(name, out var ev))
            {
                Debug.LogWarning($"[AudioEvents] PlayWorld3D: event '{name}' not found");
                return;
            }

            PlayWorld3D(ev.eventId, position, shooterNO);
        }
#else
        public static void PlayWorld3D(string name, Vector3 position)
        {
            if (Mgr == null) return;
            Mgr.Play3DAtPoint(name, position);
        }
#endif


        // AudioEvents.cs
        public static void PlayWorld3DAttached(int eventId, Transform emitter
#if FUSION_WEAVER
            , NetworkObject shooterNO = null
#endif
        )
        {
            if (Mgr == null || emitter == null) return;

            // Singleplayer → chỉ cần attach local
            if (IsSingle)
            {
                Mgr.Play3DAttached(eventId, emitter);
                return;
            }

#if FUSION_WEAVER
            if (!FusionAudioBridge.IsReadyHost)
            {
                // fallback: chưa có host bridge → vẫn cho local nghe
                Mgr.Play3DAttached(eventId, emitter);
                return;
            }

            var no = emitter.GetComponentInParent<NetworkObject>();
            FusionAudioBridge.Instance.Host_PlayWorld3DAttached(eventId, no, shooterNO);
#else
    Mgr.Play3DAttached(eventId, emitter);
#endif
        }

    }
}
    