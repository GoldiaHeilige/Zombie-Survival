using UnityEngine;

public class WeaponInteractorHighlight: MonoBehaviour
{
    [SerializeField] private PlayerPickup playerPickup;

    private WorldWeapon _current;
    private Outline _currentOutline;

    void Awake()
    {
        if (!playerPickup) playerPickup = GetComponentInParent<PlayerPickup>();
    }

    void LateUpdate()
    {
        if (!playerPickup) return;

        // Dùng đúng logic “nhìn thấy & trong pickupRange” đã có sẵn
        var ww = playerPickup.GetLookedWeapon(); // MP: chỉ local owner mới trả về ww :contentReference[oaicite:4]{index=4}

        if (ww == _current) return;

        // Tắt cái cũ
        SetOutline(_current, false);

        // ✅ reset cache để không bật nhầm outline cũ
        _currentOutline = null;

        _current = ww;

        // Bật cái mới
        SetOutline(_current, true);
    }

    void OnDisable()
    {
        SetOutline(_current, false);
        _current = null;
        _currentOutline = null;
    }

    private void SetOutline(WorldWeapon ww, bool on)
    {
        if (!ww) return;

        // nếu WW bị block pickup thì đừng outline
        if (on && !playerPickup.CanPickupNow(ww)) // dùng đúng rule cooldown drop/pickup :contentReference[oaicite:5]{index=5}
            on = false;

        // cache outline component
        if (ww == _current && _currentOutline != null)
        {
            _currentOutline.enabled = on;
            return;
        }

        if (ww.TryGetComponent<Outline>(out var outline))
        {
            outline.enabled = on;
            if (ww == _current) _currentOutline = outline;
        }
    }
}
