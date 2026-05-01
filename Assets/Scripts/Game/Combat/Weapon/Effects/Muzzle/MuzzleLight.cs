using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Light))]
public class MuzzleLight : MonoBehaviour
{
    [Tooltip("Thời gian bật đèn mỗi lần flash (giây).")]
    public float duration = 0.03f;

    [Tooltip("(Tuỳ chọn) Đồ thị 0..1 nhân với intensity gốc.")]
    public AnimationCurve intensityCurve;

    private Light _light;
    private Coroutine _co;
    private float _baseIntensity;

    void Awake()
    {
        _light = GetComponent<Light>();
        _baseIntensity = _light.intensity;
        _light.enabled = false;
    }

    void OnDisable()
    {
        if (_co != null) StopCoroutine(_co);
        if (_light) _light.enabled = false;
    }

    public void Flash(float customDuration = -1f)
    {
        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(FlashRoutine(customDuration > 0 ? customDuration : duration));
    }

    IEnumerator FlashRoutine(float d)
    {
        _light.enabled = true;

        if (intensityCurve != null && intensityCurve.keys.Length > 0)
        {
            float t = 0f;
            while (t < d)
            {
                _light.intensity = _baseIntensity * intensityCurve.Evaluate(t / d);
                t += Time.deltaTime;
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(d);
        }

        _light.enabled = false;
        _co = null;
    }
}
