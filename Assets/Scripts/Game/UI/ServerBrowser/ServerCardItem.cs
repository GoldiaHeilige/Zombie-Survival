using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Linq;
using UnityEngine.EventSystems;

namespace TT.UI
{
    public class ServerCardItem : MonoBehaviour, IPointerClickHandler
    {
        [Header("UI References")]
        [SerializeField] private Image lobbyImg;
        [SerializeField] private TMP_Text lblLobbyName;
        [SerializeField] private TMP_Text lblMapName;
        [SerializeField] private TMP_Text lblPlayers;
        [SerializeField] private TMP_Text lblStatus;

        private SessionInfo _info;
        private System.Action<SessionInfo> _onJoin;


        private static MapData[] _mapCache;

        public string SessionName => _info.Name; // NEW


        public void Bind(SessionInfo info, System.Action<SessionInfo> onJoin)
        {
            _info = info;
            _onJoin = onJoin;

            string name = string.IsNullOrEmpty(info.Name) ? "Unnamed Lobby" : info.Name;
            string sceneName = "Unknown";   // sẽ là giá trị của SessionProperties["MapName"]
            string status = "Unknown";

            int players = info.PlayerCount;
            int maxPlayers = info.MaxPlayers;

            // --- đọc custom properties MapName, Status (dạng string) ---
            if (info.Properties != null)
            {
                if (info.Properties.TryGetValue("MapName", out var mapProp))
                    sceneName = ExtractSessionPropertyText(mapProp.ToString());   // ví dụ "SampleScene"

                if (info.Properties.TryGetValue("Status", out var stProp))
                    status = ExtractSessionPropertyText(stProp.ToString());       // ví dụ "Lobby"
            }

            // --- tra MapData để lấy displayName + thumbnail ---
            string displayName = sceneName;
            Sprite thumb = null;
            var md = FindMapData(sceneName);
            if (md != null)
            {
                if (!string.IsNullOrWhiteSpace(md.displayName)) displayName = md.displayName;
                if (md.thumbnail) thumb = md.thumbnail;
            }

            // --- gán UI ---
            if (lblLobbyName) lblLobbyName.text = name;
            if (lblMapName) lblMapName.text = displayName;
            if (lblStatus) lblStatus.text = status;
            if (lblPlayers) lblPlayers.text = $"{players} / {maxPlayers}";

            if (lobbyImg)
            {
                if (thumb != null)
                {
                    lobbyImg.sprite = thumb;
                    lobbyImg.enabled = true;
                    lobbyImg.preserveAspect = true;
                    lobbyImg.color = Color.white;
                }
                else
                {
                    lobbyImg.sprite = null;
                    lobbyImg.enabled = false;    // <- tránh ô trắng
                }
            }


            var btn = GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => _onJoin?.Invoke(_info));
            }

            Debug.Log($"[ServerCardItem] Bound -> {name} ({players}/{maxPlayers}) MapScene={sceneName} Display='{displayName}' Status={status}");
        }


        private static MapData FindMapData(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName)) return null;
            if (_mapCache == null)
            {
                // Tải tất cả MapData trong Resources (đặt asset vào thư mục "Resources" để load được)
                _mapCache = Resources.LoadAll<MapData>("");
                Debug.Log($"[ServerCardItem] MapData loaded: {_mapCache?.Length ?? 0}");
            }
            return _mapCache?.FirstOrDefault(m =>
                string.Equals(m.sceneName, sceneName, StringComparison.OrdinalIgnoreCase));
        }

        static string ExtractSessionPropertyText(string s)
        {
            // Ví dụ: "[SessionProperty: SampleScene, Type=System.String]" -> "SampleScene"
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf(':');
            if (i >= 0 && i + 1 < s.Length)
            {
                i++;
                int j = s.IndexOf(',', i);
                if (j > i) return s.Substring(i, j - i).Trim();
                return s.Substring(i).Trim().TrimEnd(']');
            }
            return s.Trim('[', ']').Trim();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            _onJoin?.Invoke(_info);
        }
    }
}
