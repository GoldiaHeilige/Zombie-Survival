// Assets/Scripts/Lobby/MapData.cs
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Map Data")]
public class MapData : ScriptableObject
{
    public string sceneName;           // đúng tên scene gameplay
    public string displayName;
    public Sprite thumbnail;
    [TextArea] public string description;
    public string tags;                // ví dụ: "Small • Night • Zombies"
}
