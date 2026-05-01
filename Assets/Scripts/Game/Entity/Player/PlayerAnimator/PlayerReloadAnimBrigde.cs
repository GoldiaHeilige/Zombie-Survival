using System.Collections;
using Fusion;
using UnityEngine;

/// <summary>
/// Bridge ILoadoutState.IsReloading -> Animator bool "IsReloading".
/// Hoạt động cho cả SP & MP.
/// - SP: đọc trực tiếp PlayerLoadoutStateSP.
/// - MP: đọc PlayerLoadoutStateMP (NetworkBehaviour) => IsReloading là NetworkBool đã sync.
/// </summary>
[DisallowMultipleComponent]
public class ReloadAnimBridge : MonoBehaviour
{
    [Header("References")]
    [AutoBindInParent, SerializeField]
    private PlayerStateProvider stateProvider;

    [Tooltip("Animator của world model (nhân vật 3rd person). Nếu để trống sẽ auto tìm trong con.")]
    [SerializeField] private Animator animator;

    [Header("Animator Parameter")]
    [SerializeField] private string isReloadingParam = "IsReloading";

    private ILoadoutState _loadout;
    private int _isReloadingHash;
    private bool _bound;

    private void Awake()
    {
        if (!animator)
            animator = GetComponentInChildren<Animator>(true);

        if (!stateProvider)
            stateProvider = GetComponentInParent<PlayerStateProvider>(true);

        if (!string.IsNullOrEmpty(isReloadingParam))
            _isReloadingHash = Animator.StringToHash(isReloadingParam);
    }

    private void OnEnable()
    {
        StartCoroutine(BindRoutine());
    }

    private void OnDisable()
    {
        _bound = false;
        _loadout = null;
    }

    private IEnumerator BindRoutine()
    {
        while (!_bound)
        {
            if (!stateProvider)
                yield break;

            _loadout = stateProvider.Loadout;
            if (_loadout != null)
            {
#if FUSION_WEAVER
                if (_loadout is NetworkBehaviour netLoadout)
                {
                    yield return new WaitUntil(() =>
                        netLoadout.Object != null &&
                        netLoadout.Object.IsValid &&
                        netLoadout.Runner != null);
                }
#endif
                _bound = true;
                yield break;
            }

            yield return null;
        }
    }


    public void SetAnimator(Animator newAnimator)
    {
        animator = newAnimator;

        if (!string.IsNullOrEmpty(isReloadingParam) && animator != null)
            _isReloadingHash = Animator.StringToHash(isReloadingParam);
    }


    private void Update()
    {
        if (!_bound || animator == null || _loadout == null || _isReloadingHash == 0)
            return;

#if FUSION_WEAVER
        // Nếu là MP: object chưa spawn / đã despawn thì KHÔNG được đụng vào Networked fields
        if (_loadout is NetworkBehaviour netLoadout)
        {
            if (netLoadout.Object == null || !netLoadout.Object.IsValid || netLoadout.Runner == null)
            {
                // Treat như unbound, khỏi đọc IsReloading nữa
                _bound = false;
                _loadout = null;
                return;
            }
        }
#endif

        bool isReloading = _loadout.IsReloading;
        animator.SetBool(_isReloadingHash, isReloading);
    }
}
