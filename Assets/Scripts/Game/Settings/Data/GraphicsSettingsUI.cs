using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Globalization;

public class GraphicsSettingsUI : MonoBehaviour
{
    [Header("VSync")]
    public Toggle vsyncToggle;

    [Header("FPS Cap (hidden when VSync on)")]
    public CanvasGroup fpsCapGroup;     // thay cho fpsCapRoot
    public TMP_InputField fpsCapInput;

    [Header("Background")]
    public Toggle runInBackgroundToggle;

    [Header("Render Scale (0.1 -> 1)")]
    public TMP_InputField renderScaleInput;

    private void OnEnable()
    {
        // đảm bảo load chung settings
        SettingsManager.Load();

        var g = SettingsManager.Data.graphics;

        vsyncToggle.isOn = g.vsync;
        runInBackgroundToggle.isOn = g.runInBackground;

        fpsCapInput.text = Mathf.Clamp(g.fpsCap, 1, 1000).ToString();
        renderScaleInput.text = Mathf.Clamp(g.renderScale, 0.1f, 1f).ToString("0.##");

        RefreshVisibility();
        ApplyImmediate();
    }

    // Gắn vào OnValueChanged của Toggle/InputField
    public void OnAnyChanged()
    {
        RefreshVisibility();
        ApplyImmediate();

        GraphicsSettingsApplier.Apply(SettingsManager.Data.graphics);
        SettingsManager.Save();
    }

    private void RefreshVisibility()
    {
        bool vsync = vsyncToggle != null && vsyncToggle.isOn;
        SetGroupVisible(fpsCapGroup, !vsync);

        if (fpsCapInput) fpsCapInput.interactable = !vsync;
    }

    private static void SetGroupVisible(CanvasGroup g, bool on)
    {
        if (!g) return;
        g.interactable = on;
        g.blocksRaycasts = on;
    }


    private void ApplyImmediate()
    {
        var g = SettingsManager.Data.graphics;

        g.vsync = vsyncToggle != null && vsyncToggle.isOn;
        g.runInBackground = runInBackgroundToggle != null && runInBackgroundToggle.isOn;

        // fpsCap chỉ meaningful khi vsync off
        if (fpsCapInput != null)
        {
            if (!int.TryParse(fpsCapInput.text, out int fps)) fps = g.fpsCap;
            g.fpsCap = Mathf.Clamp(fps, 1, 1000);
            fpsCapInput.text = g.fpsCap.ToString();
        }

        if (renderScaleInput != null)
        {
            // Cho phép user gõ 0.5 hoặc 0,5 (tuỳ culture)
            string s = renderScaleInput.text.Trim();
            s = s.Replace(',', '.');

            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float rs))
                rs = g.renderScale;

            g.renderScale = Mathf.Clamp(rs, 0.1f, 1f);

            // Đừng format lại text nếu đang edit kiểu OnValueChanged (nó phá input)
            // Chỉ format khi Save/Apply hoặc OnEndEdit thì ngon hơn.
            renderScaleInput.text = g.renderScale.ToString("0.##", CultureInfo.InvariantCulture);
        }


        GraphicsSettingsApplier.Apply(g);
    }

    public void SaveChanges()
    {
        // data đã được update sẵn trong OnAnyChanged
        Debug.Log("[GraphicsSettingsUI] SaveChanges CALLED");
        ApplyImmediate();
        SettingsManager.Save();
    }

    public void ReloadFromData()
    {
        SettingsManager.Load();
        OnEnable(); // simplest
    }
}
