// Assets/Scripts/Lobby/MapListUI.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MapListUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField] private MapData[] maps;

    [Header("UI Refs")]
    [SerializeField] private Transform content;          // Panel_MapSelection/MapList/Viewport/Content
    [SerializeField] private MapCardItem cardPrefab;     // prefab như bạn đã đặt "MapCardItem"
    [SerializeField] private Image mapPreview;           // Panel_MapSelection/MapPreview (Image)
    [SerializeField] private TMP_Text mapInfo;           // Panel_MapSelection/MapInfo (TMP_Text)

    private int _current = -1;

    private void Start()
    {
        Build();
        if (maps != null && maps.Length > 0) Select(0);
    }

    private void Build()
    {
        foreach (Transform c in content) Destroy(c.gameObject);
        if (maps == null) return;

        for (int i = 0; i < maps.Length; i++)
        {
            // trong Build(), ngay sau khi Instantiate
            var card = Instantiate(cardPrefab, content);

            // ÉP bật hết component nếu prefab bị tắt sẵn
            var behaviours = card.GetComponentsInChildren<Behaviour>(true);
            foreach (var b in behaviours) { if (b) b.enabled = true; }

            // gắn bind
            int idx = i;
            card.Bind(maps[i], _ => Select(idx));

            // (tuỳ chọn) nếu Button trên prefab không wired sẵn:
            var btn = card.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => card.OnClick());
            }
        }
    }

    private void Select(int index)
    {
        if (maps == null || index < 0 || index >= maps.Length) return;
        _current = index;
        var data = maps[index];

        LobbyParams.SelectedMapSceneName = data.sceneName;

        if (mapPreview) mapPreview.sprite = data.thumbnail;
        if (mapInfo) mapInfo.text = $"{data.displayName}\n{data.tags}\n\n{data.description}";
    }
}
