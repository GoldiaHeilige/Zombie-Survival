using TT;

public static class EventFeed
{
    public static void Push(string message, EventFeedType type = EventFeedType.Info)
    {
        Observer.Instance.NotifyWithData("event.feed", new EventFeedData(message, type));
    }
}

public struct EventFeedData
{
    public string message;
    public EventFeedType type;

    public EventFeedData(string msg, EventFeedType t)
    {
        message = msg;
        type = t;
    }
}

public enum EventFeedType
{
    Info,       // xanh dương nhẹ
    Success,    // xanh lá
    Warning,    // vàng cam
    Danger,     // đỏ
    Action      // tím / cyan để highlight event đặc biệt
}
