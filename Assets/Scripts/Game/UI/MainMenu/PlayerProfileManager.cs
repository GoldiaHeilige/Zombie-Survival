using System.IO;
using UnityEngine;

[System.Serializable]
public class PlayerProfileData
{
    public string playerName;
}

public static class PlayerProfileManager
{
    private static string FilePath => Path.Combine(Application.persistentDataPath, "player_profile.json");
    private static PlayerProfileData _cached;

    public static PlayerProfileData Data
    {
        get { if (_cached == null) Load(); return _cached; }
    }

    public static void Save(string name)
    {
        _cached = new PlayerProfileData { playerName = string.IsNullOrWhiteSpace(name) ? DefaultName() : name.Trim() };
        var json = JsonUtility.ToJson(_cached, true);
        File.WriteAllText(FilePath, json);
#if UNITY_EDITOR
        Debug.Log($"[Profile] Saved: {FilePath}");
#endif
    }

    public static void Load()
    {
        if (File.Exists(FilePath))
        {
            var json = File.ReadAllText(FilePath);
            _cached = JsonUtility.FromJson<PlayerProfileData>(json);
        }
        else
        {
            _cached = new PlayerProfileData { playerName = DefaultName() };
            Save(_cached.playerName);
        }
    }

    private static string DefaultName() => $"Player{Random.Range(1000, 9999)}";
}
