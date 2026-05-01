// Assets/Scripts/Lobby/MapCardItem.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MapCardItem : MonoBehaviour
{
    [SerializeField] private Image thumbnail;
    [SerializeField] private TMP_Text text;
    private MapData _data;
    private System.Action<MapData> _onClick;

    public void Bind(MapData data, System.Action<MapData> onClick)
    {
        _data = data; _onClick = onClick;
        if (thumbnail) thumbnail.sprite = data.thumbnail;
        if (text) text.text = $"{data.displayName}\n{data.tags}";
    }

    public void OnClick() => _onClick?.Invoke(_data);
}
