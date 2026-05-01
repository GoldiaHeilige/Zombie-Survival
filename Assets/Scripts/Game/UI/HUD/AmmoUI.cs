using UnityEngine;
using TMPro;
using DG.Tweening;

public class AmmoUI : MonoBehaviour
{
    [Header("Texts")]
    [SerializeField] private TMP_Text ammoInMagText;
    [SerializeField] private TMP_Text ammoReserveText;

    [Header("Tween (DOTween)")]
    [SerializeField] private float punchScale = 0.15f;
    [SerializeField] private float punchDuration = 0.12f;
    [SerializeField] private int punchVibrato = 8;
    [SerializeField] private float punchElasticity = 0.9f;

    private ILoadoutState _state;

    private int _lastMag = int.MinValue;
    private int _lastReserve = int.MinValue;

    private Vector3 _magBaseScale = Vector3.one;
    private Vector3 _reserveBaseScale = Vector3.one;

    private Tween _magTween;
    private Tween _reserveTween;

    void Awake()
    {
        if (ammoInMagText) _magBaseScale = ammoInMagText.transform.localScale;
        if (ammoReserveText) _reserveBaseScale = ammoReserveText.transform.localScale;
    }

    void OnDisable()
    {
        Unhook();
        KillTweens();
    }

    public void Bind(ILoadoutState state)
    {
        if (state == _state) { Refresh(force: true); return; }

        Unhook();
        _state = state;
        Hook();

        // reset cache để lần refresh đầu hiện đúng + không punch bậy
        _lastMag = int.MinValue;
        _lastReserve = int.MinValue;

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
            Refresh(force: false); // chỉ punch khi thật sự đổi số
    }

    private void Refresh(bool force)
    {
        if (!ammoInMagText || !ammoReserveText) return;

        // default
        if (_state == null || _state.SlotCount <= 0 || _state.ActiveSlot < 0)
        {
            SetTexts("--", "--", forceNoPunch: true);
            return;
        }

        var slot = _state.GetSlot(_state.ActiveSlot);
        if (slot.IsEmpty)
        {
            SetTexts("--", "--", forceNoPunch: true);
            return;
        }

        int mag = slot.mag;
        int reserve = slot.reserve;

        // Format giống style cũ: mag 2 số, reserve 3 số
        string magStr = $"{mag:00}";
        string reserveStr = $"{reserve:000}";

        bool magChanged = force || mag != _lastMag;
        bool reserveChanged = force || reserve != _lastReserve;

        // Update text
        ammoInMagText.text = magStr;
        ammoReserveText.text = reserveStr;

        // Punch đúng cái thay đổi
        if (!force)
        {
            if (magChanged) PunchMag();
            if (reserveChanged) PunchReserve();
        }

        _lastMag = mag;
        _lastReserve = reserve;
    }

    private void SetTexts(string mag, string reserve, bool forceNoPunch)
    {
        ammoInMagText.text = mag;
        ammoReserveText.text = reserve;

        _lastMag = int.MinValue;
        _lastReserve = int.MinValue;

        if (forceNoPunch)
        {
            // reset scale (đỡ bị stuck nếu disable/enable)
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

    private void KillTweens()
    {
        _magTween?.Kill();
        _reserveTween?.Kill();
        _magTween = null;
        _reserveTween = null;
    }
}
