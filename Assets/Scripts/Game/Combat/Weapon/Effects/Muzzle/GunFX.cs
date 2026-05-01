// GunFX.cs – phiên bản sửa để bật/tắt ROOT FX và điều khiển cả Particle + Light + Sprite
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GunFX : MonoBehaviour
{
    Transform _muzzle;
    GameObject _fxRoot;                   // <— giữ root instance
    List<ParticleSystem> _particles = new();
    List<Renderer> _spriteLike = new();   // SpriteRenderer hoặc bất cứ Renderer “2D FX” nào
    MuzzleLight _muzzleLight;

    float _life = 0.06f; // thời lượng tổng ước lượng (sẽ cập nhật từ particles)
    Coroutine _autoOff;

    public void Configure(Transform muzzleSocket, GameObject muzzleFlashPrefab)
    {
        if (_autoOff != null) { StopCoroutine(_autoOff); _autoOff = null; }
        if (_fxRoot != null) { Destroy(_fxRoot); _fxRoot = null; }

        _muzzle = muzzleSocket;
        if (_muzzle == null || muzzleFlashPrefab == null) return;

        _fxRoot = Instantiate(muzzleFlashPrefab, _muzzle);
        _fxRoot.transform.localPosition = Vector3.zero;
        _fxRoot.transform.localRotation = Quaternion.identity;

        // Đồng bộ layer "culling" với súng (Weapon)
        SetLayerRecursive(_fxRoot, LayerMask.NameToLayer("MuzzleLight"));

        // Thu thập components
        _particles.Clear();
        _particles.AddRange(_fxRoot.GetComponentsInChildren<ParticleSystem>(true));

        _spriteLike.Clear();
        // Ưu tiên SpriteRenderer; nếu FX dùng MeshRenderer/Renderer đơn giản cũng gom luôn
        _spriteLike.AddRange(_fxRoot.GetComponentsInChildren<SpriteRenderer>(true));
        // Nếu Sparks dùng MeshRenderer cho quad, ta cũng gom (không hại)
        foreach (var r in _fxRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (!(r is ParticleSystemRenderer) && !_spriteLike.Contains(r))
                _spriteLike.Add(r);
        }

        _muzzleLight = _fxRoot.GetComponentInChildren<MuzzleLight>(true);

        // Chuẩn hoá Particle
        foreach (var ps in _particles)
        {
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.stopAction = ParticleSystemStopAction.None; // ta tự tắt root

            // Ướm thời lượng
            float dur = main.duration;
            float lifeMax = 0.05f;
#if UNITY_2022_3_OR_NEWER
            switch (main.startLifetime.mode)
            {
                case ParticleSystemCurveMode.Constant: lifeMax = main.startLifetime.constant; break;
                case ParticleSystemCurveMode.TwoConstants: lifeMax = main.startLifetime.constantMax; break;
                default: lifeMax = Mathf.Max(0.05f, lifeMax); break;
            }
#endif
            _life = Mathf.Max(_life, dur + lifeMax);
        }

        _fxRoot.SetActive(false);
    }

    public void PlayMuzzle()
    {
        if (_fxRoot == null) return;

        // Bật ROOT để mọi child (Particle + Sprite + Light) cùng hoạt động
        if (!_fxRoot.activeSelf) _fxRoot.SetActive(true);

        // Particles one-shot
        foreach (var ps in _particles)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }

        // Light flash song song (rất ngắn)
        if (_muzzleLight) _muzzleLight.Flash(Mathf.Min(_life, 0.05f));

        // Sprite-like (SpriteRenderer/Mesh quad) -> bật renderer rồi sẽ tắt theo _life
        foreach (var r in _spriteLike)
            if (r) r.enabled = true;

        if (_autoOff != null) StopCoroutine(_autoOff);
        _autoOff = StartCoroutine(AutoOffRoutine());
    }

    IEnumerator AutoOffRoutine()
    {
        yield return new WaitForSeconds(_life);
        // Tắt renderer “2D FX”
        foreach (var r in _spriteLike)
            if (r) r.enabled = false;

        // Tắt root (để lần sau bật lại đồng bộ tất cả)
        if (_fxRoot) _fxRoot.SetActive(false);

        _autoOff = null;
    }

    static void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform c in obj.transform)
            SetLayerRecursive(c.gameObject, layer);
    }
}
