using UnityEngine;

public class DownedOverlayUI : MonoBehaviour
{
    public static DownedOverlayUI Instance { get; private set; }

    [SerializeField] private CanvasGroup group;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (!group)
            group = GetComponent<CanvasGroup>();

        SetVisible(false); // mặc định tắt
    }

    public void SetVisible(bool visible)
    {
        if (!group) return;

        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
    }
}
