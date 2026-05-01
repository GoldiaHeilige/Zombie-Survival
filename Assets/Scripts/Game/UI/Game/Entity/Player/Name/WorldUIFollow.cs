using UnityEngine;

public class WorldUIFollow : MonoBehaviour
{
    [Header("Targets")]
    public Transform target;                    // đầu player
    public RectTransform rootRect;              // Canvas root (không scale)
    public RectTransform scalableContainer;     // CONTAINER scale chung cho BG + Text

    [Header("State Icons")]
    public GameObject downedIcon;

    [Header("Scale Settings")]
    public float baseScale = 0.1f;
    public float scaleMultiplier = 0.20f;

    [Header("Height Settings")]
    public float baseHeight = 0.3f;
    public float heightMultiplier = 0.15f;

    private Camera _cam;

    private PlayerLifeController _life;

    void Start()
    {
        _cam = Camera.main;
    }

    public void Init(PlayerLifeController life)
    {
        // Hủy đăng ký cũ (nếu có)
        if (_life != null)
        {
            _life.OnDowned -= OnLifeDowned;
            _life.OnRevived -= OnLifeRevivedOrRespawned;
            _life.OnRespawned -= OnLifeRevivedOrRespawned;
            _life.OnDead -= OnLifeDead;
        }

        _life = life;

        if (_life != null)
        {
            _life.OnDowned += OnLifeDowned;
            _life.OnRevived += OnLifeRevivedOrRespawned;
            _life.OnRespawned += OnLifeRevivedOrRespawned;
            _life.OnDead += OnLifeDead;

            RefreshDownedIcon();
        }
        else
        {
            SetDownedIcon(false);
        }
    }

    private void OnLifeDowned(PlayerLifeController who)
    {
        SetDownedIcon(true);
    }

    private void OnLifeRevivedOrRespawned(PlayerLifeController who)
    {
        SetDownedIcon(false);
    }

    private void OnLifeDead(PlayerLifeController who)
    {
        // Dead thì cũng không còn trạng thái "đang downed"
        SetDownedIcon(false);
    }

    private void RefreshDownedIcon()
    {
        if (_life != null)
            SetDownedIcon(_life.state == LifeState.Downed);
        else
            SetDownedIcon(false);
    }

    private void SetDownedIcon(bool active)
    {
        if (downedIcon != null)
            downedIcon.SetActive(active);
    }



    void LateUpdate()
    {
        if (!target) return;

        if (!_cam)
        {
            _cam = Camera.main;
            if (!_cam) return;
        }

        float dist = Vector3.Distance(_cam.transform.position, target.position);

        // TEXT & BACKGROUND SCALE
        float worldScale = baseScale + dist * scaleMultiplier;

        // HEIGHT OFFSET Scale nhẹ
        float dynamicHeight = baseHeight + dist * heightMultiplier;

        // Position UI over head
        rootRect.position = target.position + Vector3.up * dynamicHeight;

        // Billboard
        rootRect.forward = _cam.transform.forward;

        // Scale container (text + background)
        scalableContainer.localScale = Vector3.one * worldScale;

        // Root stays small (stable)
        rootRect.localScale = Vector3.one * 0.01f;
    }

    private void OnDestroy()
    {
        if (_life != null)
        {
            _life.OnDowned -= OnLifeDowned;
            _life.OnRevived -= OnLifeRevivedOrRespawned;
            _life.OnRespawned -= OnLifeRevivedOrRespawned;
            _life.OnDead -= OnLifeDead;
        }
    }

}
