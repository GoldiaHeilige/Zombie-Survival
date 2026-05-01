public static class AudioSettingsManager
{
    // Giữ API cũ để các UI/script không phải sửa nhiều
    public static AudioSettingsData Data => SettingsManager.Data.audio;

    public static void Load()
    {
        SettingsManager.Load();
    }

    public static void Save()
    {
        SettingsManager.Save();
    }
}
