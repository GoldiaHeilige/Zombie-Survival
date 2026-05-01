using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MapSelectionUI : MonoBehaviour
{
    [Header("Optional (old dropdown header)")]
    [SerializeField] private TMP_Text txtSelectedMapName;            // vẫn update để show map đang chọn

    [Header("Scroll List (was Dropdown)")]
    [SerializeField] private GameObject panelDropdown;              // giờ coi như panel LIST (nên luôn bật)
    [SerializeField] private Transform dropdownContent;              // Content của ScrollRect (VerticalLayoutGroup)
    [SerializeField] private GameObject dropdownItemPrefab;          // Prefab item (Button + TMP_Text)

    [Header("Preview")]
    [SerializeField] private Image previewThumbnail;
    [SerializeField] private TMP_Text previewTitle;
    [SerializeField] private TMP_Text previewDescription;

    // Internal
    private MapData[] maps;
    private int currentIndex = -1;

    private void Awake()
    {
        // List panel luôn bật
        if (panelDropdown)
            panelDropdown.SetActive(true);
    }

    private void Start()
    {
        LoadMaps();

        if (maps != null && maps.Length > 0)
            SelectMap(0, refreshSelectionVisual: true);
    }

    private void LoadMaps()
    {
        maps = Resources.LoadAll<MapData>("");

        // Clear list
        if (dropdownContent)
        {
            for (int i = dropdownContent.childCount - 1; i >= 0; i--)
                Destroy(dropdownContent.GetChild(i).gameObject);
        }

        if (maps == null || maps.Length == 0 || !dropdownContent || !dropdownItemPrefab)
            return;

        // Spawn list items
        for (int i = 0; i < maps.Length; i++)
        {
            int index = i;
            var itemGO = Instantiate(dropdownItemPrefab, dropdownContent);

            // label
            var txt = itemGO.GetComponentInChildren<TMP_Text>(true);
            if (txt) txt.text = maps[i].displayName;

            // click
            var btn = itemGO.GetComponent<Button>();
            if (btn)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnListItemClicked(index));
            }
        }

        RefreshSelectionVisual();
    }

    private void OnListItemClicked(int index)
    {
        SelectMap(index, refreshSelectionVisual: true);
    }

    private void SelectMap(int index, bool refreshSelectionVisual)
    {
        if (maps == null || index < 0 || index >= maps.Length)
            return;

        currentIndex = index;
        var data = maps[index];

        // Update selected label (nếu mày có chỗ hiển thị)
        if (txtSelectedMapName)
            txtSelectedMapName.text = data.displayName;

        // Update preview
        if (previewThumbnail)
            previewThumbnail.sprite = data.thumbnail;

        if (previewTitle)
            previewTitle.text = data.displayName;

        if (previewDescription)
            previewDescription.text = data.description;

        // Update global params
        LobbyParams.SelectedMapSceneName = data.sceneName;

        GameSession.SelectedMapSceneName = data.sceneName;
        GameSession.SelectedMapDisplayName = data.displayName;


        if (refreshSelectionVisual)
            RefreshSelectionVisual();
    }

    private void RefreshSelectionVisual()
    {
        if (!dropdownContent) return;

        // Cố gắng highlight item đang chọn:
        // - Nếu item có Image (background) thì đổi alpha
        // - Nếu không có thì thôi (vẫn chạy bình thường)
        for (int i = 0; i < dropdownContent.childCount; i++)
        {
            var child = dropdownContent.GetChild(i);
            bool isSelected = (i == currentIndex);

            // background image (ưu tiên Image ngay trên root item)
            var bg = child.GetComponent<Image>();
            if (bg)
            {
                var c = bg.color;
                c.a = isSelected ? 1f : 0.35f; // chỉ đổi alpha để khỏi phụ thuộc màu
                bg.color = c;
            }

            // text alpha nhẹ
            var txt = child.GetComponentInChildren<TMP_Text>(true);
            if (txt)
            {
                var tc = txt.color;
                tc.a = isSelected ? 1f : 0.75f;
                txt.color = tc;
            }
        }
    }

    public string GetSelectedMapDisplayName()
    {
        if (maps == null || maps.Length == 0) return null;
        if (currentIndex < 0 || currentIndex >= maps.Length) return null;
        return maps[currentIndex] != null ? maps[currentIndex].displayName : null;
    }

    public string GetSelectedMapSceneName()
    {
        if (maps == null || maps.Length == 0) return null;
        if (currentIndex < 0 || currentIndex >= maps.Length) return null;
        return maps[currentIndex] != null ? maps[currentIndex].sceneName : null;
    }

}
