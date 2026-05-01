using TT;
using UnityEngine;

public class ZoneUnlockVisual : MonoBehaviour
{
    [Header("Animator")]
    [SerializeField] private Animator animator;
    [SerializeField] private string unlockTrigger = "Unlock";

    [Header("Objects will disable/destroy AFTER animation")]
    [Tooltip("Nếu để trống -> disable/destroy chính object này (root visual).")]
    [SerializeField] private GameObject[] objectsToDisable;
    [SerializeField] private bool destroyInsteadOfDisable = false;

    [System.Serializable]
    public class UnlockFX
    {
        public GameObject vfxPrefab;
        public int sfxEventId;
        public Transform spawnPoint;   // optional
    }

    [Header("Unlock FX (multiple events)")]
    [SerializeField] private UnlockFX[] unlockFx;
    [SerializeField] private AudioSource audioSource;


    bool _played;

    void Awake()
    {
        if (!animator) animator = GetComponentInChildren<Animator>(true);
        if (!audioSource) audioSource = GetComponent<AudioSource>();
    }

    public void PlayUnlock(GameObject opener)
    {
        if (_played) return;
        _played = true;

        if (animator)
        {
            animator.ResetTrigger(unlockTrigger);
            animator.SetTrigger(unlockTrigger);
        }
        else
        {
            // Không có animator thì coi như "xong" luôn
            FinishAndDisable();
        }
    }

    /// <summary>
    /// Gọi từ Animation Event ở cuối anim unlock (khuyến nghị).
    /// </summary>
    public void AnimEvent_FinishUnlock()
    {
        FinishAndDisable();
    }

    void FinishAndDisable()
    {
        var targets = (objectsToDisable != null && objectsToDisable.Length > 0)
            ? objectsToDisable
            : new[] { gameObject };

        foreach (var go in targets)
        {
            if (!go) continue;

            if (destroyInsteadOfDisable) Destroy(go);
            else go.SetActive(false);
        }
    }

    /// <summary>
    /// Gọi từ Animation Event ở các mốc khác nhau.
    /// Parameter là index trong mảng unlockFx.
    /// </summary>
    public void AnimEvent_PlayFX(int id)
    {
        if (unlockFx == null || id < 0 || id >= unlockFx.Length) return;

        var fx = unlockFx[id];
        var t = fx.spawnPoint ? fx.spawnPoint : transform;

        // VFX
        if (fx.vfxPrefab)
        {
            Instantiate(fx.vfxPrefab, t.position, t.rotation);
        }

        // SFX
        if (fx.sfxEventId != 0)
        {
            // world 3D, tự lo SP/MP
            AudioEvents.PlayWorld3D(fx.sfxEventId, t.position);
        }
    }
}
