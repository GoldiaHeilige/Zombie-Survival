using TMPro;
using UnityEngine;

public class PowerUpTimerHUD : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private TMP_Text instaKillText;
    [SerializeField] private TMP_Text doublePointsText;

    [Header("Labels")]
    [SerializeField] private string instaKillLabel = "Insta-Kill";
    [SerializeField] private string doublePointsLabel = "Double Points";

    // end times (unscaled)
    private float _instaKillEnd;
    private float _doublePointsEnd;

    private const string TopicStarted = "hud.powerup.timed.started";
    private const string TopicEnded = "hud.powerup.timed.ended";

    private void Awake()
    {
        // Hide at start
        if (instaKillText) instaKillText.gameObject.SetActive(false);
        if (doublePointsText) doublePointsText.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        TT.Observer.Instance?.AddObserver(TopicStarted, OnTimedStarted);
        TT.Observer.Instance?.AddObserver(TopicEnded, OnTimedEnded);
    }

    private void OnDisable()
    {
        TT.Observer.Instance?.RemoveObserver(TopicStarted, OnTimedStarted);
        TT.Observer.Instance?.RemoveObserver(TopicEnded, OnTimedEnded);
    }

    private void Update()
    {
        float now = Time.unscaledTime;

        UpdateOne(instaKillText, instaKillLabel, ref _instaKillEnd, now);
        UpdateOne(doublePointsText, doublePointsLabel, ref _doublePointsEnd, now);
    }

    private static void UpdateOne(TMP_Text text, string label, ref float endTime, float now)
    {
        if (!text) return;

        float remain = endTime > 0f ? (endTime - now) : 0f;

        if (remain <= 0f)
        {
            endTime = 0f;
            if (text.gameObject.activeSelf) text.gameObject.SetActive(false);
            return;
        }

        if (!text.gameObject.activeSelf) text.gameObject.SetActive(true);

        int seconds = Mathf.CeilToInt(remain);
        text.text = $"{label} : {seconds}";
    }

    private void OnTimedStarted(object payload)
    {
        // payload phải là (PowerUpType type, float durationSeconds)
        var (type, duration) = ((PowerUpType, float))payload;

        float now = Time.unscaledTime;
        float end = now + Mathf.Max(0.1f, duration);

        switch (type)
        {
            case PowerUpType.InstaKill:
                _instaKillEnd = end;
                break;

            case PowerUpType.DoublePoints:
                _doublePointsEnd = end;
                break;
        }
    }

    private void OnTimedEnded(object payload)
    {
        // payload là PowerUpType
        var type = (PowerUpType)payload;

        switch (type)
        {
            case PowerUpType.InstaKill:
                _instaKillEnd = 0f;
                break;

            case PowerUpType.DoublePoints:
                _doublePointsEnd = 0f;
                break;
        }
    }
}
