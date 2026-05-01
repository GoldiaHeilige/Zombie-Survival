using TT;
using UnityEngine;

public class CasingImpactSfx : MonoBehaviour
{
    [SerializeField] private AudioEventSO impactAudio;
    [SerializeField] private float minImpactSpeed = 0.6f;
    [SerializeField] private float rearmTime = 0.08f;

    bool _played;
    float _armedAt;

    public void Init(AudioEventSO audio)
    {
        impactAudio = audio;
        _played = false;
        _armedAt = Time.time + rearmTime; // tránh va chạm ngay lúc spawn
    }

    void OnCollisionEnter(Collision c)
    {
        if (_played) return;
        if (impactAudio == null) return;
        if (Time.time < _armedAt) return;

        // Chỉ play khi va chạm đủ mạnh để nghe “rơi xuống”
        if (c.relativeVelocity.magnitude < minImpactSpeed) return;

        _played = true;

        // FP only: chơi 2D/first-person event
        AudioEvents.PlayFirstPerson(impactAudio.eventId);
    }

    public void ResetState()
    {
        _played = false;
        _armedAt = Time.time + rearmTime;
    }

}
