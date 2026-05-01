// Assets/Scripts/UI/LobbyPlayerItem.cs
using TMPro;
using UnityEngine;

namespace TT.UI
{
    public class LobbyPlayerItem : MonoBehaviour
    {
        [SerializeField] private TMP_Text playerName;
        [SerializeField] private TMP_Text playerPing; // NEW

        public void SetName(string name)
        {
            if (playerName) playerName.text = name;
        }

        public void SetPingMs(int? ms) // NEW
        {
            if (!playerPing) return;
            playerPing.text = ms.HasValue ? $"{ms.Value} ms" : "-- ms";
        }
    }
}
