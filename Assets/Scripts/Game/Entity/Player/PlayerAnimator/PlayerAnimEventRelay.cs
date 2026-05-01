using UnityEngine;

[DisallowMultipleComponent]
public class PlayerAnimEventRelay : MonoBehaviour
{
    [Tooltip("Driver audio movement của player (auto-find nếu trống).")]
    public PlayerMovementAudioDriver movementAudio;

    [Tooltip("Controller downed để nhận event đứng dậy sau revive (auto-find nếu trống).")]
    public PlayerDownedController downedController;

    void Awake()
    {
        if (!movementAudio)
            movementAudio = GetComponentInParent<PlayerMovementAudioDriver>();
        if (!downedController)
            downedController = GetComponentInParent<PlayerDownedController>();
    }

    public void AnimEvent_Footstep()
    {
        movementAudio?.AnimEvent_Footstep();
    }

    public void AnimEvent_CrouchDown()
    {
        movementAudio?.AnimEvent_CrouchDown();
    }

    public void AnimEvent_StandUp()
    {
        movementAudio?.AnimEvent_StandUp();
    }

    // Jump / Land
    public void AnimEvent_JumpStart()
    {
        movementAudio?.AnimEvent_JumpStart();
    }

    public void AnimEvent_Land()
    {
        movementAudio?.AnimEvent_Land();
    }

    /// <summary>
    /// Gọi từ Animation Event ở cuối anim đứng dậy sau revive.
    /// </summary>
    public void AnimEvent_ReviveStandupFinished()
    {
        downedController?.NotifyReviveStandupAnimFinished();
    }

}
