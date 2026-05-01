using UnityEngine;
using TMPro;

public class FPSCounter : MonoBehaviour
{
    public TMP_Text label;

    float timer;

    void Update()
    {
        timer += Time.unscaledDeltaTime;
        if (timer >= 0.5f) // update mỗi 0.5s để ổn định
        {
            int fps = Mathf.RoundToInt(1f / Time.unscaledDeltaTime);
            if (label) label.text = $"FPS: {fps}";
            timer = 0f;
        }
    }
}
