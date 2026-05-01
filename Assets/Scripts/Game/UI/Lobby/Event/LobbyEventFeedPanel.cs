using UnityEngine;
using TT;
using System.Collections.Generic;
using UnityEngine.UI;

public class LobbyEventFeedPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform contentRoot;
    [SerializeField] private EventFeedItem itemPrefab;

    [Header("Settings")]
    [SerializeField] private int maxItems = 5;
    [SerializeField] private float itemLifetime = 4f;

    private readonly List<GameObject> activeItems = new();

    private void Awake()
    {
        Observer.Instance.AddObserver(LobbyEventFeed.Topic, OnFeedEvent);
    }

    private void OnDestroy()
    {
        if (Observer.Instance != null)
            Observer.Instance.RemoveObserver(LobbyEventFeed.Topic, OnFeedEvent);
    }

    private void OnFeedEvent(object data)
    {
        if (data is not EventFeedData feedData)
            return;

        AddFeed(feedData);
    }

    public void AddFeed(EventFeedData feed)
    {
        if (activeItems.Count >= maxItems)
        {
            Destroy(activeItems[0]);
            activeItems.RemoveAt(0);
        }

        var go = Instantiate(itemPrefab, contentRoot);
        go.Setup(feed.message, GetColor(feed.type), itemLifetime);

        activeItems.Add(go.gameObject);

        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot as RectTransform);
    }

    private Color GetColor(EventFeedType type)
    {
        return type switch
        {
            EventFeedType.Info => new Color(0.55f, 0.75f, 1f),
            EventFeedType.Success => new Color(0.45f, 1f, 0.55f),
            EventFeedType.Warning => new Color(1f, 0.9f, 0.3f),
            EventFeedType.Danger => new Color(1f, 0.4f, 0.4f),
            EventFeedType.Action => new Color(0.4f, 1f, 1f),
            _ => Color.white
        };
    }
}
