using Fusion;
using UnityEngine;

public class RandomBoxNetworkAnimator : NetworkBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] string openTrigger = "Open";
    [SerializeField] string closeTrigger = "Close";

    [SerializeField] RandomWeaponBoxSpot boxSpot;

    int _hashOpen;
    int _hashClose;

    void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!boxSpot) boxSpot = GetComponent<RandomWeaponBoxSpot>();
        _hashOpen = Animator.StringToHash(openTrigger);
        _hashClose = Animator.StringToHash(closeTrigger);
    }

    // Host gọi
    public void PlayOpen()
    {
        if (Object != null && Object.HasStateAuthority)
        {
            RPC_PlayOpen();
        }
        else if (Object == null)
        {
            PlayOpenLocal(); // SP
        }
    }

    public void PlayClose()
    {
        if (Object != null && Object.HasStateAuthority)
        {
            RPC_PlayClose();
        }
        else if (Object == null)
        {
            PlayCloseLocal(); // SP
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlayOpen()
    {
        PlayOpenLocal();

        // Bật ánh sáng trên TẤT CẢ máy
        if (boxSpot != null)
        {
            boxSpot.ForceLight(true);
            boxSpot.OnRemoteOpen(); // 🔥 báo cho client bắt đầu Opening
        }
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    void RPC_PlayClose()
    {
        PlayCloseLocal();

        // Tắt ánh sáng trên TẤT CẢ máy
        if (boxSpot != null)
        {
            boxSpot.ForceLight(false);
            boxSpot.OnRemoteClose(); // 🔥 THÊM
        }
    }


    void PlayOpenLocal()
    {
        if (animator && _hashOpen != 0)
            animator.SetTrigger(_hashOpen);
    }

    void PlayCloseLocal()
    {
        if (animator && _hashClose != 0)
            animator.SetTrigger(_hashClose);
    }

}
