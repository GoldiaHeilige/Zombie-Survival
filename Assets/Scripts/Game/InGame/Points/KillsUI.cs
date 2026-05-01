using UnityEngine;
using TMPro;
using TT;
using DG.Tweening; // NEW

public class KillsUI : MonoBehaviour
{
    [SerializeField] private TMP_Text killsText;

    [Header("Tween")]
    [SerializeField] private float punchScale = 0.2f;
    [SerializeField] private float punchDuration = 0.15f;

    Transform _boundPlayerRoot;
    int _displayedKills;

    Vector3 _baseScale;
    Tween _killsTween;

    void Awake()
    {
        if (killsText != null)
        {
            killsText.text = "0";
            _baseScale = killsText.rectTransform.localScale;
        }
    }

    void OnEnable()
    {
        Observer.Instance?.AddObserver(KillTopics.Changed, OnKillsChanged);
    }

    void OnDisable()
    {
        Observer.Instance?.RemoveObserver(KillTopics.Changed, OnKillsChanged);
        _killsTween?.Kill();
    }

    public void Bind(Transform playerRoot)
    {
        _boundPlayerRoot = playerRoot;
    }

    void OnKillsChanged(object payloadObj)
    {
        if (!(payloadObj is KillsChangedEventData data))
            return;

        if (_boundPlayerRoot != null && data.owner != null)
        {
            if (data.owner.transform.root != _boundPlayerRoot.root)
                return;
        }

        _displayedKills = data.newValue;

        if (killsText != null)
        {
            killsText.text = _displayedKills.ToString();

            _killsTween?.Kill();
            killsText.rectTransform.localScale = _baseScale;
            _killsTween = killsText.rectTransform
                .DOPunchScale(Vector3.one * punchScale, punchDuration, 0, 0)
                .SetUpdate(true);
        }
    }
}
