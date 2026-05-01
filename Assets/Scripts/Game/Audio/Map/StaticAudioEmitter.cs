using UnityEngine;
using TT;

public class StaticAudioEmitter : MonoBehaviour
{
    [SerializeField] private int audioEventID;

    private AudioHandle _handle;

    private AudioManager _am;


    private void OnEnable()
    {
        _am = AudioManager.Instance;
        if (!_am) return;

        if (!_handle.IsValid)
            _handle = _am.Play3DAttachedHandle(audioEventID, transform);
    }

    private void OnDisable()
    {
        if (!_am) return;
        _am.FadeOutAndStop(_handle, 0.5f);
        _handle = default;
    }
}
