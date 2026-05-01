using System;
using Fusion;
using UnityEngine;

namespace TT
{
    public class LobbyPlayerInfo : NetworkBehaviour
    {
        public static event Action<PlayerRef, string> OnAnyNameChanged;

        [Networked]
        public NetworkString<_16> DisplayName { get; private set; }

        public override void Spawned()
        {
            if (Object.HasInputAuthority)
            {
                string name = PlayerProfileManager.Data.playerName;
                if (string.IsNullOrWhiteSpace(name))
                    name = $"Player{Object.InputAuthority.PlayerId}";

                // gửi tên mình lên host
                RPC_RequestSetName(name);
            }
        }

        // Client → Host
        [Rpc(sources: RpcSources.InputAuthority, targets: RpcTargets.StateAuthority)]
        private void RPC_RequestSetName(string name, RpcInfo info = default)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = $"Player{Object.InputAuthority.PlayerId}";

            DisplayName = name;

            // Host broadcast cho tất cả
            RPC_BroadcastName(Object.InputAuthority, name);
        }

        // Host → All
        [Rpc(sources: RpcSources.StateAuthority, targets: RpcTargets.All)]
        private void RPC_BroadcastName(PlayerRef player, string name, RpcInfo info = default)
        {
            OnAnyNameChanged?.Invoke(player, name);
        }
    }
}
