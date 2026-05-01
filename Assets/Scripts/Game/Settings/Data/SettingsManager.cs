using System.IO;
using UnityEngine;

public static class SettingsManager
{
    private const string FILE = "Settings.json";

    // Backward-compat file cũ
    private const string LEGACY_AUDIO_FILE = "AudioSettings.json";

    public static GameSettingsData Data { get; private set; }

    public static string FilePath =>
        Path.Combine(Application.persistentDataPath, FILE);

    private static string LegacyAudioPath =>
        Path.Combine(Application.persistentDataPath, LEGACY_AUDIO_FILE);

    public static void Load()
    {
        // Ưu tiên settings mới
        if (File.Exists(FilePath))
        {
            var json = File.ReadAllText(FilePath);
            Data = JsonUtility.FromJson<GameSettingsData>(json);
            if (Data == null) Data = new GameSettingsData();
            return;
        }

        // Nếu chưa có Settings.json nhưng có AudioSettings.json (cũ) => migrate
        Data = new GameSettingsData();

        if (File.Exists(LegacyAudioPath))
        {
            try
            {
                var json = File.ReadAllText(LegacyAudioPath);
                var audio = JsonUtility.FromJson<AudioSettingsData>(json);
                if (audio != null) Data.audio = audio;
            }
            catch
            {
                // ignore, dùng default
            }
        }

        Save(); // tạo Settings.json mới
        Debug.Log("[SettingsManager] JSON:\n" + JsonUtility.ToJson(Data, true));

    }

    public static void Save()
    {
        if (Data == null) Data = new GameSettingsData();
        var json = JsonUtility.ToJson(Data, true);
        File.WriteAllText(FilePath, json);
    }
}
