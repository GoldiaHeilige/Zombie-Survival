// Assets/Scripts/Networking/LobbyParams.cs
using Fusion;

public static class LobbyParams
{
    public static GameMode Mode = GameMode.AutoHostOrClient;
    public static string SessionName = "default";
    public static int MaxPlayers = 4;

    // Map sẽ được chọn ở Lobby, để dành sẵn:
    public static string SelectedMapSceneName = "SampleScene";
}
