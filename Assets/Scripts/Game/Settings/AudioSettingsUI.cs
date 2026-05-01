using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class AudioSettingsUI : MonoBehaviour
{
    [Header("Slider References")]
    public Slider musicSlider;
    public Slider uiSlider;
    public Slider sfxSlider;
    public Slider firstPersonSlider;
    public Slider ambientSlider;

    [Header("Percentage Text")]
    public TMP_Text txtMusic;
    public TMP_Text txtUI;
    public TMP_Text txtSFX;
    public TMP_Text txtFP;
    public TMP_Text txtAmbient;

    [Header("Mixer")]
    public UnityEngine.Audio.AudioMixer mixer;

    private void OnEnable()
    {
        AudioSettingsManager.Load();

        // Load JSON → UI
        musicSlider.value = AudioSettingsManager.Data.music;
        uiSlider.value = AudioSettingsManager.Data.ui;
        sfxSlider.value = AudioSettingsManager.Data.sfx;
        firstPersonSlider.value = AudioSettingsManager.Data.firstPerson;
        ambientSlider.value = AudioSettingsManager.Data.ambient;

        ApplyImmediate();
        UpdateLabels();
    }

    public void OnSliderChanged()
    {
        UpdateLabels();
        ApplyImmediate();
    }

    private void ApplyImmediate()
    {
        ApplyToMixer("MusicVolume", musicSlider.value);
        ApplyToMixer("UIVolume", uiSlider.value);
        ApplyToMixer("SFXVolume", sfxSlider.value);
        ApplyToMixer("FirstPersonVolume", firstPersonSlider.value);
        ApplyToMixer("AmbientVolume", ambientSlider.value);
    }

    private void ApplyToMixer(string param, float v)
    {
        // SỬA THÀNH CÔNG THỨC LOGARITHMIC
        float db = VolumeToDB(v);
        mixer.SetFloat(param, db);
    }

    private float VolumeToDB(float volume)
    {
        // volume: 0 -> 2 (0% -> 200%)
        if (volume <= 0.01f) return -80f; // tắt tiếng

        // Chuyển đổi logarithmic
        // 0.5 = -12dB, 1.0 = 0dB, 2.0 = +6dB
        return Mathf.Log10(volume) * 20f;
    }

    public void SaveChanges()
    {
        AudioSettingsManager.Data.music = musicSlider.value;
        AudioSettingsManager.Data.ui = uiSlider.value;
        AudioSettingsManager.Data.sfx = sfxSlider.value;
        AudioSettingsManager.Data.firstPerson = firstPersonSlider.value;
        AudioSettingsManager.Data.ambient = ambientSlider.value;

        AudioSettingsManager.Save();
    }

    private void UpdateLabels()
    {
        txtMusic.text = $"{Mathf.RoundToInt(musicSlider.value * 100)}%";
        txtUI.text = $"{Mathf.RoundToInt(uiSlider.value * 100)}%";
        txtSFX.text = $"{Mathf.RoundToInt(sfxSlider.value * 100)}%";
        txtFP.text = $"{Mathf.RoundToInt(firstPersonSlider.value * 100)}%";
        txtAmbient.text = $"{Mathf.RoundToInt(ambientSlider.value * 100)}%";
    }

    public void ReloadFromData()
    {
        AudioSettingsManager.Load();

        musicSlider.value = AudioSettingsManager.Data.music;
        uiSlider.value = AudioSettingsManager.Data.ui;
        sfxSlider.value = AudioSettingsManager.Data.sfx;
        firstPersonSlider.value = AudioSettingsManager.Data.firstPerson;
        ambientSlider.value = AudioSettingsManager.Data.ambient;

        ApplyImmediate();
        UpdateLabels();
    }

}
