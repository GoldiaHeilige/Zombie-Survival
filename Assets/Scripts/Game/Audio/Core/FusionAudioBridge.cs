#if FUSION_WEAVER
using Fusion;
using UnityEngine;

namespace TT
{
    /// <summary>
    /// Cầu nối audio cho Fusion:
    /// - Có đúng 1 instance trong room.
    /// - Host gọi Host_PlayWorld3D(...) để gửi RPC phát 3D sound cho mọi client.
    /// - Client nào sở hữu shooter (HasInputAuthority) sẽ bỏ qua world sound để tránh double với FP 2D.
    /// </summary>
    [DisallowMultipleComponent]
    public class FusionAudioBridge : NetworkBehaviour
    {
        public static FusionAudioBridge Instance { get; private set; }

        public static bool IsReadyHost =>
            Instance != null &&
            Instance.Object != null &&
            Instance.Object.HasStateAuthority;

        public override void Spawned()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[FusionAudioBridge] Duplicate instance, despawning this one.");
                Runner.Despawn(Object);
                return;
            }

            Instance = this;
            base.Spawned();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this)
                Instance = null;

            base.Despawned(runner, hasState);
        }

        // ================= HOST API =================

        /// <summary>
        /// Chỉ host nên gọi. Gửi RPC world sound tới tất cả client.
        /// </summary>
        public void Host_PlayWorld3D(int eventId, Vector3 position, NetworkObject shooterNO)
        {
            if (!Object || !Object.HasStateAuthority)
            {
                // Fallback: nếu vì lý do gì đó không phải host, ít nhất vẫn có tiếng local
                AudioManager.Instance?.Play3DAtPoint(eventId, position);
                return;
            }

            RPC_PlayWorld3D(eventId, position, shooterNO);
        }

        public void Host_PlayUI2D(int eventId)
        {
            var mgr = AudioManager.Instance;
            if (mgr == null)
                return;

            // Nếu vì lý do gì đó không phải host thì vẫn cho máy này nghe.
            if (!Object || !Object.HasStateAuthority)
            {
                mgr.Play2D(eventId);
                return;
            }

            RPC_PlayUI2D(eventId);
        }


        // ================= RPC =================

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayWorld3D(int eventId, Vector3 position, NetworkObject shooterNO, RpcInfo info = default)
        {
            var mgr = AudioManager.Instance;
            if (!mgr)
                return;

            bool isLocalShooter = false;

            if (shooterNO && shooterNO.HasInputAuthority)
                isLocalShooter = true;

            // Local shooter không nghe lại world sound (option A)
            if (isLocalShooter)
                return;

            mgr.Play3DAtPoint(eventId, position);
        }

        // FusionAudioBridge.cs
        public void Host_PlayWorld3DAttached(int eventId, NetworkObject emitterNO, NetworkObject shooterNO)
        {
            if (!Object || !Object.HasStateAuthority)
            {
                // fallback local
                if (emitterNO)
                    AudioManager.Instance?.Play3DAttached(eventId, emitterNO.transform);
                return;
            }

            RPC_PlayWorld3DAttached(eventId, emitterNO, shooterNO);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayWorld3DAttached(int eventId, NetworkObject emitterNO, NetworkObject shooterNO, RpcInfo info = default)
        {
            var mgr = AudioManager.Instance;
            if (!mgr) return;

            // shooter local thì bỏ qua (dùng cho súng nếu cần)
            bool isLocalShooter = shooterNO && shooterNO.HasInputAuthority;
            if (isLocalShooter) return;

            if (emitterNO)
                mgr.Play3DAttached(eventId, emitterNO.transform);
        }

        public void Host_PlayUI2D(string eventName)
        {
            var mgr = AudioManager.Instance;
            if (mgr == null)
                return;

            if (!Object || !Object.HasStateAuthority)
            {
                mgr.Play2D(eventName);
                return;
            }

            RPC_PlayUI2D_Name(eventName);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayUI2D(int eventId, RpcInfo info = default)
        {
            var mgr = AudioManager.Instance;
            if (!mgr) return;

            mgr.Play2D(eventId);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayUI2D_Name(string eventName, RpcInfo info = default)
        {
            var mgr = AudioManager.Instance;
            if (!mgr) return;

            mgr.Play2D(eventName);
        }
    }
}
#endif
