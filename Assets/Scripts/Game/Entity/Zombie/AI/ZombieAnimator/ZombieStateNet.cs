#if FUSION_WEAVER
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class ZombieStateNet : NetworkBehaviour
{
    [Networked] public byte State { get; private set; } // cast từ ZombieBrain.State

    public event System.Action<byte, byte> OnStateChanged;

    [Networked] public ushort DeathSeq { get; private set; }

    public event System.Action OnDeathPulse;

    ushort _lastDeathSeq;

    [Networked] public ushort HitPlayerSeq { get; private set; }
    public event System.Action OnHitPlayerPulse;
    ushort _lastHitPlayerSeq;


    byte _last;

    public override void Spawned()
    {
        _last = State;
        _lastHitPlayerSeq = HitPlayerSeq;
        _lastDeathSeq = DeathSeq;
    }

    // Render chạy trên mọi máy, hợp để detect change & fire event (giống RoundStateNet)
    public override void Render()
    {
        if (_last != State)
        {
            var prev = _last;
            _last = State;
            OnStateChanged?.Invoke(prev, State);
        }

        if (_lastDeathSeq != DeathSeq)
        {
            _lastDeathSeq = DeathSeq;
            OnDeathPulse?.Invoke();
        }

        if (_lastHitPlayerSeq != HitPlayerSeq)
        {
            _lastHitPlayerSeq = HitPlayerSeq;
            OnHitPlayerPulse?.Invoke();
        }
    }

    // Host/StateAuthority gọi
    public void SetStateAuthority(byte s)
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
        State = s;
    }

    public void PulseDeath()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
        DeathSeq++;
    }

    public void PulseHitPlayer()
    {
        if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
        HitPlayerSeq++;
    }

}
#endif
