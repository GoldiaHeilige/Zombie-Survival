using UnityEngine;

public class AudioSettingsBootstrap : MonoBehaviour
{
    public UnityEngine.Audio.AudioMixer mixer;

    void Awake()
    {
        // Load 1 file JSON chung
        SettingsManager.Load();

        // Apply AUDIO
        Apply("MusicVolume", SettingsManager.Data.audio.music);
        Apply("UIVolume", SettingsManager.Data.audio.ui);
        Apply("SFXVolume", SettingsManager.Data.audio.sfx);
        Apply("FirstPersonVolume", SettingsManager.Data.audio.firstPerson);
        Apply("AmbientVolume", SettingsManager.Data.audio.ambient);

        // Apply GRAPHICS
        GraphicsSettingsApplier.Apply(SettingsManager.Data.graphics);
    }

    private void Apply(string param, float v)
    {
        float db = VolumeToDB(v);
        mixer.SetFloat(param, db);
    }

    private float VolumeToDB(float volume)
    {
        if (volume <= 0.01f) return -80f;
        return Mathf.Log10(volume) * 20f;
    }
}
