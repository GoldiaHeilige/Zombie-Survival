using UnityEngine;

[DisallowMultipleComponent]
public class ZombieAnimEventRelay : MonoBehaviour
{
    ZombieAudioDriver _audio;

    void Awake()
    {
        // Tìm driver ở cha (base zombie prefab)
        _audio = GetComponentInParent<ZombieAudioDriver>();
        if (!_audio)
        {
            Debug.LogWarning("[ZombieAnimEventRelay] No ZombieAudioDriver found in parents.", this);
        }
    }

    // Footstep từ anim event
    public void AnimEvent_Footstep()
    {
        _audio?.AnimEvent_Footstep();
    }

    // Attack swing từ anim event
    public void AnimEvent_AttackSwing()
    {
        _audio?.AnimEvent_AttackSwing();
    }

    public void AnimEvent_DeathBodyFall()
    {
        _audio?.AnimEvent_DeathBodyFall();
    }

    // public void AnimEvent_Whatever()    => _audio?.OnXxx();
}
