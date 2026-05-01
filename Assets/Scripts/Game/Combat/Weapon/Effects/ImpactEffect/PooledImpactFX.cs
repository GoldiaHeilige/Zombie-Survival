using UnityEngine;
using System.Collections;

public class PooledImpactFX : MonoBehaviour
{
    [Tooltip("Nếu <= 0 sẽ auto-tính theo particle; nếu > 0 dùng giá trị này (giây).")]
    public float lifeOverride = -1f;

    [Tooltip("Xoay ngẫu nhiên quanh normal để tránh lặp pattern.")]
    public bool randomYaw = true;

    ParticleSystem[] _particles;
    AudioSource[] _audios;
    Coroutine _co;

    void Awake()
    {
        _particles = GetComponentsInChildren<ParticleSystem>(true);
        _audios = GetComponentsInChildren<AudioSource>(true);
        gameObject.SetActive(false);
    }

    public void Activate(Vector3 pos, Vector3 normal, float life = -1f)
    {
        if (!this || !gameObject) return; // guard an toàn

        transform.position = pos;
        var rot = Quaternion.LookRotation(normal, Vector3.up);
        if (randomYaw) rot *= Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.forward);
        transform.rotation = rot;

        gameObject.SetActive(true);

        // play particles one-shot
        if (_particles != null)
        {
            foreach (var ps in _particles)
            {
                var m = ps.main;
                m.loop = false;
                m.playOnAwake = false;
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
            }
        }

        if (_audios != null)
            foreach (var a in _audios) if (a) a.Play();

        if (_co != null) StopCoroutine(_co);
        _co = StartCoroutine(AutoDisable(GetLifeSeconds(life)));
    }

    float GetLifeSeconds(float ext)
    {
        if (ext > 0f) return ext;
        if (lifeOverride > 0f) return lifeOverride;

        float maxLife = 0.5f; // fallback
        if (_particles != null)
        {
            foreach (var ps in _particles)
            {
                var m = ps.main;
                float dur = m.duration;
                float lf = 0.05f;
#if UNITY_2022_3_OR_NEWER
                switch (m.startLifetime.mode)
                {
                    case ParticleSystemCurveMode.Constant: lf = m.startLifetime.constant; break;
                    case ParticleSystemCurveMode.TwoConstants: lf = m.startLifetime.constantMax; break;
                    default: lf = Mathf.Max(lf, 0.05f); break;
                }
#endif
                maxLife = Mathf.Max(maxLife, dur + lf);
            }
        }
        return maxLife + 0.05f; // margin nhỏ
    }

    IEnumerator AutoDisable(float secs)
    {
        yield return new WaitForSeconds(secs);
        gameObject.SetActive(false);
        _co = null;
    }
}
