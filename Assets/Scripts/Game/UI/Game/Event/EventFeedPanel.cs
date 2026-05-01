using UnityEngine;
using TT; // để dùng Observer
using System.Collections.Generic;
using UnityEngine.UI;

public class EventFeedPanel : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform contentRoot;
    [SerializeField] private EventFeedItem itemPrefab;

    [Header("Settings")]
    [SerializeField] private int maxItems = 5;
    [SerializeField] private float itemLifetime = 4f;

    private readonly List<GameObject> activeItems = new();

    void Awake()
    {
        // đăng ký observer topic
        Observer.Instance.AddObserver("event.feed", OnFeedEvent);
    }

    private void OnFeedEvent(object data)
    {
        if (data is not EventFeedData feedData)
            return;

        AddFeed(feedData);
    }

    public void AddFeed(EventFeedData feed)
    {
        // Nếu quá số lượng → remove item đầu tiên
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
            EventFeedType.Info => new Color(0.55f, 0.75f, 1f),   // Xanh nhẹ
            EventFeedType.Success => new Color(0.45f, 1f, 0.55f),   // Xanh lá
            EventFeedType.Warning => new Color(1f, 0.9f, 0.3f),      // Vàng
            EventFeedType.Danger => new Color(1f, 0.4f, 0.4f),      // Đỏ
            EventFeedType.Action => new Color(0.4f, 1f, 1f),        // Cyan
            _ => Color.white
        };
    }
}
