using TT;

public static class LobbyEventFeed
{
    public const string Topic = "lobby.feed";

    public static void Push(string message, EventFeedType type = EventFeedType.Info)
    {
        Observer.Instance.NotifyWithData(Topic, new EventFeedData(message, type));
    }
}
