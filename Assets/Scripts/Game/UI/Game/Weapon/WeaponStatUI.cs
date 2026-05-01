using UnityEngine;
using TMPro;
using DG.Tweening;

public class WeaponStatUI : MonoBehaviour
{
    [Header("Texts")]
    [SerializeField] private TMP_Text weaponNameText;   // NEW
    [SerializeField] private TMP_Text ammoInMagText;
    [SerializeField] private TMP_Text ammoReserveText;

    [Header("Low Ammo Warning (blink)")]
    [SerializeField] private TMP_Text lowAmmoText;
    [SerializeField, Range(0.01f, 0.99f)] private float lowAmmoThreshold01 = 0.20f;
    [SerializeField] private string lowAmmoLabel = "LOW AMMO";
    [SerializeField] private Color lowAmmoColorA = Color.white;
    [SerializeField] private Color lowAmmoColorB = Color.red;
    [SerializeField] private float lowAmmoBlinkHalfPeriod = 0.18f;
    private Tween _lowAmmoTween;

    [Header("Tween (DOTween)")]
    [SerializeField] private float punchScale = 0.15f;
    [SerializeField] private float punchDuration = 0.12f;
    [SerializeField] private int punchVibrato = 8;
    [SerializeField] private float punchElasticity = 0.9f;

    [Header("WeaponName Tween (optional)")]
    [SerializeField] private bool punchWeaponNameOnChange = true;
    [SerializeField] private float weaponNamePunchScale = 0.10f;

    private ILoadoutState _state;

    private int _lastMag = int.MinValue;
    private int _lastReserve = int.MinValue;
    private int _lastWeaponKey = int.MinValue; // NEW

    private Vector3 _magBaseScale = Vector3.one;
    private Vector3 _reserveBaseScale = Vector3.one;
    private Vector3 _nameBaseScale = Vector3.one;

    private Tween _magTween;
    private Tween _reserveTween;
    private Tween _nameTween;

    void Awake()
    {
        if (ammoInMagText) _magBaseScale = ammoInMagText.transform.localScale;
        if (ammoReserveText) _reserveBaseScale = ammoReserveText.transform.localScale;
        if (weaponNameText) _nameBaseScale = weaponNameText.transform.localScale;

        StopLowAmmoBlink();
    }

    void OnDisable()
    {
        Unhook();
        KillTweens();
        StopLowAmmoBlink();
    }

    public void Bind(ILoadoutState state)
    {
        if (state == _state) { Refresh(force: true); return; }

        Unhook();
        _state = state;
        Hook();

        // reset cache để lần refresh đầu hiện đúng
        _lastMag = int.MinValue;
        _lastReserve = int.MinValue;
        _lastWeaponKey = int.MinValue;

        Refresh(force: true);
    }

    private void Hook()
    {
        if (_state == null) return;
        _state.OnActiveSlotChanged += OnActiveSlotChanged;
        _state.OnSlotChanged += OnSlotChanged;
    }

    private void Unhook()
    {
        if (_state == null) return;
        _state.OnActiveSlotChanged -= OnActiveSlotChanged;
        _state.OnSlotChanged -= OnSlotChanged;
    }

    private void OnActiveSlotChanged(int slot) => Refresh(force: true);

    private void OnSlotChanged(int slot)
    {
        if (_state != null && slot == _state.ActiveSlot)
            Refresh(force: false); // chỉ punch khi thật sự đổi số / đổi súng
    }

    private void Refresh(bool force)
    {
        if (!ammoInMagText || !ammoReserveText) return;

        // default (no weapon)
        if (_state == null || _state.SlotCount <= 0 || _state.ActiveSlot < 0)
        {
            SetTexts("--", "--", "--", forceNoPunch: true);
            StopLowAmmoBlink();
            return;
        }

        var slot = _state.GetSlot(_state.ActiveSlot);
        if (slot.IsEmpty || slot.weaponKey == 0)
        {
            SetTexts("--", "--", "--", forceNoPunch: true);
            StopLowAmmoBlink();
            return;
        }

        // ===== Weapon name =====
        int weaponKey = slot.weaponKey;
        string wName = "--";

        // WeaponIdRegistry bạn đang dùng ở WeaponController.ReadAmmo()
        // nên ở đây cũng dùng y chang.
        var def = WeaponIdRegistry.GetDef(weaponKey);
        if (def != null && !string.IsNullOrWhiteSpace(def.weaponName))
            wName = def.weaponName;

        bool weaponChanged = force || weaponKey != _lastWeaponKey;
        if (weaponNameText != null)
            weaponNameText.text = wName;

        // ===== Ammo =====
        int mag = slot.mag;
        int reserve = slot.reserve;

        // Format giống UI cũ: mag 2 số, reserve 3 số
        string magStr = $"{mag:00}";
        string reserveStr = $"{reserve:000}";

        int magSize = def != null ? def.magSize : 0;
        UpdateLowAmmoBlink(mag, magSize);

        bool magChanged = force || mag != _lastMag;
        bool reserveChanged = force || reserve != _lastReserve;

        ammoInMagText.text = magStr;
        ammoReserveText.text = reserveStr;

        if (!force)
        {
            if (magChanged) PunchMag();
            if (reserveChanged) PunchReserve();
            if (weaponChanged && punchWeaponNameOnChange) PunchWeaponName();
        }

        _lastMag = mag;
        _lastReserve = reserve;
        _lastWeaponKey = weaponKey;
    }

    private void SetTexts(string weaponName, string mag, string reserve, bool forceNoPunch)
    {
        if (weaponNameText) weaponNameText.text = weaponName;
        ammoInMagText.text = mag;
        ammoReserveText.text = reserve;

        _lastMag = int.MinValue;
        _lastReserve = int.MinValue;
        _lastWeaponKey = int.MinValue;

        if (forceNoPunch)
        {
            if (weaponNameText) weaponNameText.transform.localScale = _nameBaseScale;
            if (ammoInMagText) ammoInMagText.transform.localScale = _magBaseScale;
            if (ammoReserveText) ammoReserveText.transform.localScale = _reserveBaseScale;
        }
    }

    private void PunchMag()
    {
        if (!ammoInMagText) return;
        _magTween?.Kill();
        ammoInMagText.transform.localScale = _magBaseScale;
        _magTween = ammoInMagText.transform.DOPunchScale(
            Vector3.one * punchScale,
            punchDuration,
            punchVibrato,
            punchElasticity
        ).SetUpdate(true);
    }

    private void PunchReserve()
    {
        if (!ammoReserveText) return;
        _reserveTween?.Kill();
        ammoReserveText.transform.localScale = _reserveBaseScale;
        _reserveTween = ammoReserveText.transform.DOPunchScale(
            Vector3.one * punchScale,
            punchDuration,
            punchVibrato,
            punchElasticity
        ).SetUpdate(true);
    }

    private void PunchWeaponName()
    {
        if (!weaponNameText) return;
        _nameTween?.Kill();
        weaponNameText.transform.localScale = _nameBaseScale;
        _nameTween = weaponNameText.transform.DOPunchScale(
            Vector3.one * weaponNamePunchScale,
            punchDuration,
            punchVibrato,
            punchElasticity
        ).SetUpdate(true);
    }

    private void KillTweens()
    {
        _magTween?.Kill();
        _reserveTween?.Kill();
        _nameTween?.Kill();
        _magTween = null;
        _reserveTween = null;
        _nameTween = null;
        _lowAmmoTween?.Kill();
        _lowAmmoTween = null;
    }

    private void UpdateLowAmmoBlink(int mag, int magSize)
    {
        if (!lowAmmoText) return;

        bool low =
            magSize > 0 &&
            mag > 0 && // nếu muốn “0” cũng nháy thì bỏ dòng này
            (mag / (float)magSize) < lowAmmoThreshold01;

        if (low) StartLowAmmoBlink();
        else StopLowAmmoBlink();
    }

    private void StartLowAmmoBlink()
    {
        if (!lowAmmoText) return;

        if (!lowAmmoText.gameObject.activeSelf)
            lowAmmoText.gameObject.SetActive(true);

        if (!string.IsNullOrEmpty(lowAmmoLabel))
            lowAmmoText.text = lowAmmoLabel;

        // nếu đang chạy rồi thì thôi
        if (_lowAmmoTween != null && _lowAmmoTween.IsActive() && _lowAmmoTween.IsPlaying())
            return;

        _lowAmmoTween?.Kill();
        lowAmmoText.color = lowAmmoColorA;

        _lowAmmoTween = lowAmmoText
            .DOColor(lowAmmoColorB, Mathf.Max(0.01f, lowAmmoBlinkHalfPeriod))
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true);
    }

    private void StopLowAmmoBlink()
    {
        _lowAmmoTween?.Kill();
        _lowAmmoTween = null;

        if (lowAmmoText)
        {
            lowAmmoText.color = lowAmmoColorA;
            if (lowAmmoText.gameObject.activeSelf)
                lowAmmoText.gameObject.SetActive(false);
        }
    }
}
