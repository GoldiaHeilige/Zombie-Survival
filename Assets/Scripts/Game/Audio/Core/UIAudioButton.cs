using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TT;

public class UIAudioButton : MonoBehaviour, IPointerEnterHandler
{
    [Header("Audio Event Names")]
    public string clickEvent = "ui.click";
    public string hoverEvent = "ui.hover";

    [Header("Enable / Disable")]
    public bool enableClickSound = true;
    public bool enableHoverSound = true;

    private Button btn;

    void Awake()
    {
        btn = GetComponent<Button>();
        if (btn && enableClickSound)
        {
            btn.onClick.AddListener(() =>
            {
                if (enableClickSound)
                    AudioEvents.PlayUI(clickEvent);
            });
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (enableHoverSound)
            AudioEvents.PlayUI(hoverEvent);
    }
}
