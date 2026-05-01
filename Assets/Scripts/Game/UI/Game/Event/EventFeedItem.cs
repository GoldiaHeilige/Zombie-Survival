using UnityEngine;
using TMPro;
using System.Collections;

public class EventFeedItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField] private CanvasGroup canvasGroup;

    [SerializeField] private float fadeDuration = 0.25f;  // thời gian fade in/out

    private float lifetime = 4f; // sẽ được set từ Panel

    /// <summary>
    /// Gọi sau khi Instantiate prefab để set nội dung + màu + thời gian sống.
    /// </summary>
    public void Setup(string message, Color color, float duration)
    {
        lifetime = duration;

        if (text != null)
        {
            text.text = message;
            text.color = color;
        }

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        StopAllCoroutines();
        StartCoroutine(CoLife());
    }

    private IEnumerator CoLife()
    {
        // 1) Fade IN
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            if (canvasGroup != null)
                canvasGroup.alpha = k;
            yield return null;
        }

        if (canvasGroup != null)
            canvasGroup.alpha = 1f;

        // 2) Đứng yên trong lifetime
        float remain = lifetime;
        while (remain > 0f)
        {
            remain -= Time.deltaTime;
            yield return null;
        }

        // 3) Fade OUT
        t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            if (canvasGroup != null)
                canvasGroup.alpha = 1f - k;
            yield return null;
        }

        // 4) Xóa object
        Destroy(gameObject);
    }
}
