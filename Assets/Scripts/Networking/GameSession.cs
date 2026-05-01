public enum AppPlayMode { Single, Host, Client }

public static class GameSession
{
    public static AppPlayMode Mode = AppPlayMode.Single;
    public static string SelectedMapSceneName;
    public static string SelectedMapDisplayName;
    public static int MaxPlayers = 4;
}
