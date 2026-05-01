// AudioManager.cs
using System.Collections.Generic;
using NIX.Core.DesignPatterns;
using UnityEngine;
using UnityEngine.Audio;

namespace TT
{
    /// <summary>
    /// Core Audio Manager: giữ list AudioEvent, lookup, pool AudioSource,
    /// cung cấp Play2D / Play3D cho toàn game (SP & MP).
    /// </summary>
    [DisallowMultipleComponent]
    // (tuỳ chọn) cho AudioManager awake cực sớm để giảm case script khác gọi Instance trước
    [DefaultExecutionOrder(-10000)]
    public class AudioManager : SingletonBehaviour<AudioManager>, IAutoDontDestroy, NIX.Core.DesignPatterns.INoAutoCreate
    {
        [Header("Mixer")]
        [SerializeField] private AudioMixer _masterMixer;
        [SerializeField] private AudioMixerGroup _masterGroup;
        [SerializeField] private AudioMixerGroup _sfxGroup;
        [SerializeField] private AudioMixerGroup _uiGroup;
        [SerializeField] private AudioMixerGroup _musicGroup;
        [SerializeField] private AudioMixerGroup _voiceGroup;
        [SerializeField] private AudioMixerGroup _ambientGroup;
        [SerializeField] private AudioMixerGroup _firstPersonGroup;

        [Header("Events Library")]
        [SerializeField] private List<AudioEventSO> _events = new List<AudioEventSO>();

        [Tooltip("Các bộ AudioEventSO khác nhau—Fire, Reload, UI, Ambient...")]
        [SerializeField] private List<AudioEventCollection> _collections = new List<AudioEventCollection>();


        private readonly Dictionary<AudioSource, AudioCategory> _activeCategories = new Dictionary<AudioSource, AudioCategory>();
        private readonly Dictionary<int, AudioEventSO> _byId = new Dictionary<int, AudioEventSO>();
        private readonly Dictionary<string, AudioEventSO> _byName =
            new Dictionary<string, AudioEventSO>(System.StringComparer.OrdinalIgnoreCase);

        [Header("Pooling")]
        [SerializeField] private int _initial2DPool = 8;
        [SerializeField] private int _initial3DPool = 16;

        private readonly List<AudioSource> _pool2D = new List<AudioSource>();
        private readonly List<AudioSource> _pool3D = new List<AudioSource>();

        // === Handle tracking ===
        private int _nextHandleToken = 1;
        private readonly Dictionary<AudioSource, int> _activeHandles = new Dictionary<AudioSource, int>();

        protected override void Awake()
        {
            // Base Awake sẽ xử lý singleton logic và DontDestroyOnLoad
            base.Awake();

            // Chỉ khởi tạo nếu đây là instance chính
            if (Instance == this)
            {
                RebuildLookup();
                EnsurePools();
                Debug.Log("[AudioManager] Initialized as DontDestroyOnLoad");
            }
        }

        private void EnsureSourceReusable(AudioSource src)
        {
            if (!src) return;

            // Nếu component bị disable bởi ai đó → bật lại
            if (!src.enabled) src.enabled = true;

            // Nếu đang nằm dưới 1 parent bị inactive (ví dụ zombie pooled) → kéo về lại AudioManager
            var p = src.transform.parent;
            if (p != null && !p.gameObject.activeInHierarchy)
                src.transform.SetParent(transform, false);

            // Đảm bảo object self active (chỉ có tác dụng sau khi parent OK)
            if (!src.gameObject.activeSelf)
                src.gameObject.SetActive(true);
        }

        private void OnValidate()
        {
            RebuildLookup();
        }

        #region Lookup / Mixer helpers

        private void RebuildLookup()
        {
            _byId.Clear();
            _byName.Clear();

            if (_events == null)
                return;

            foreach (var ev in _events)
            {
                if (!ev)
                    continue;

                // ID
                if (ev.eventId != 0)
                {
                    if (_byId.ContainsKey(ev.eventId))
                    {
                        Debug.LogWarning($"[AudioManager] Duplicate eventId {ev.eventId} between '{_byId[ev.eventId].name}' and '{ev.name}'");
                    }
                    else
                    {
                        _byId.Add(ev.eventId, ev);
                    }
                }

                // Name
                if (!string.IsNullOrEmpty(ev.eventName))
                {
                    if (_byName.ContainsKey(ev.eventName))
                    {
                        Debug.LogWarning($"[AudioManager] Duplicate eventName '{ev.eventName}' between '{_byName[ev.eventName].name}' and '{ev.name}'");
                    }
                    else
                    {
                        _byName.Add(ev.eventName, ev);
                    }
                }
            }

            // ===== Load from collections (recursive) =====
            if (_collections != null)
            {
                foreach (var col in _collections)
                {
                    RegisterCollection(col);
                }
            }

        }

        private void RegisterCollection(AudioEventCollection col)
        {
            if (col == null)
                return;

            // ----- Load EVENTS -----
            if (col.events != null)
            {
                foreach (var e in col.events)
                {
                    if (e == null) continue;

                    if (e.eventId != 0 && !_byId.ContainsKey(e.eventId))
                        _byId.Add(e.eventId, e);

                    if (!string.IsNullOrEmpty(e.eventName) && !_byName.ContainsKey(e.eventName))
                        _byName.Add(e.eventName, e);
                }
            }

            // ----- Load NESTED COLLECTIONS -----
            if (col.nestedCollections != null)
            {
                foreach (var sub in col.nestedCollections)
                    RegisterCollection(sub);     // <--- ĐỆ QUY
            }
        }

        private void EnsurePools()
        {
            EnsurePoolInternal(_pool2D, _initial2DPool, false, "2D");
            EnsurePoolInternal(_pool3D, _initial3DPool, true, "3D");
        }

        private void EnsurePoolInternal(List<AudioSource> pool, int count, bool is3D, string label)
        {
            if (pool == null)
                return;

            int toCreate = count - pool.Count;
            for (int i = 0; i < toCreate; i++)
            {
                var src = CreateSource(is3D);
                src.name = $"{label}_Audio_{pool.Count}";
                pool.Add(src);
            }
        }

        private AudioMixerGroup GetGroupForCategory(AudioCategory cat)
        {
            return cat switch
            {
                AudioCategory.SFX => _sfxGroup ? _sfxGroup : _masterGroup,
                AudioCategory.UI => _uiGroup ? _uiGroup : _masterGroup,
                AudioCategory.Music => _musicGroup ? _musicGroup : _masterGroup,
                AudioCategory.Voice => _voiceGroup ? _voiceGroup : _masterGroup,
                AudioCategory.Ambient => _ambientGroup ? _ambientGroup : _masterGroup,
                AudioCategory.FirstPerson => _firstPersonGroup ? _firstPersonGroup : (_sfxGroup ? _sfxGroup : _masterGroup),
                _ => _masterGroup
            };
        }

        #endregion

        #region Public API - Lookup

        public bool TryGetEvent(int id, out AudioEventSO ev) => _byId.TryGetValue(id, out ev);

        public bool TryGetEvent(string name, out AudioEventSO ev)
        {
            if (string.IsNullOrEmpty(name))
            {
                ev = null;
                return false;
            }

            return _byName.TryGetValue(name, out ev);
        }

        #endregion

        #region Public API - Play by int id (fire-and-forget)

        public void Play2D(int eventId)
        {
            if (!TryGetEvent(eventId, out var ev))
            {
                Debug.LogWarning($"[AudioManager] Play2D: eventId {eventId} not found");
                return;
            }

            Play2DInternal(ev);
        }

        public void Play3DAtPoint(int eventId, Vector3 position)
        {
            if (!TryGetEvent(eventId, out var ev))
            {
                Debug.LogWarning($"[AudioManager] Play3DAtPoint: eventId {eventId} not found");
                return;
            }

            Play3DAtPointInternal(ev, position);
        }

        public void Play3DAttached(int eventId, Transform target)
        {
            if (!TryGetEvent(eventId, out var ev))
            {
                Debug.LogWarning($"[AudioManager] Play3DAttached: eventId {eventId} not found");
                return;
            }

            Play3DAttachedInternal(ev, target);
        }

        #endregion

        #region Public API - Play by name (fire-and-forget)

        public void Play2D(string name)
        {
            if (!TryGetEvent(name, out var ev))
            {
                Debug.LogWarning($"[AudioManager] Play2D: eventName '{name}' not found");
                return;
            }

            Play2DInternal(ev);
        }

        public void Play3DAtPoint(string name, Vector3 position)
        {
            if (!TryGetEvent(name, out var ev))
            {
                Debug.LogWarning($"[AudioManager] Play3DAtPoint: eventName '{name}' not found");
                return;
            }

            Play3DAtPointInternal(ev, position);
        }

        public void Play3DAttached(string name, Transform target)
        {
            if (!TryGetEvent(name, out var ev))
            {
                Debug.LogWarning($"[AudioManager] Play3DAttached: eventName '{name}' not found");
                return;
            }

            Play3DAttachedInternal(ev, target);
        }

        #endregion

        #region Public API - Play with handle (music/ambient/loop)

        public AudioHandle Play2DHandle(int eventId)
        {
            if (!TryGetEvent(eventId, out var ev))
            {
                Debug.LogWarning($"[AudioManager] Play2DHandle: eventId {eventId} not found");
                return default;
            }

            return Play2DInternal(ev);
        }

        public AudioHandle Play3DAtPointHandle(int eventId, Vector3 position)
        {
            if (!TryGetEvent(eventId, out var ev))
            {
                Debug.LogWarning($"[AudioManager] Play3DAtPointHandle: eventId {eventId} not found");
                return default;
            }

            return Play3DAtPointInternal(ev, position);
        }

        public AudioHandle Play3DAttachedHandle(int eventId, Transform target)
        {
            if (!TryGetEvent(eventId, out var ev))
            {
                Debug.LogWarning($"[AudioManager] Play3DAttachedHandle: eventId {eventId} not found");
                return default;
            }

            return Play3DAttachedInternal(ev, target);
        }

        #endregion

        #region Handle control

        public void Stop(AudioHandle handle)
        {
            if (!handle.IsValid)
                return;

            if (!_activeHandles.TryGetValue(handle.source, out var token))
                return;

            if (token != handle.token)
                return; // handle cũ, source đã được tái sử dụng

            handle.source.Stop();
            handle.source.clip = null;
            _activeHandles.Remove(handle.source);
            _activeCategories.Remove(handle.source);
        }

        public void FadeOutAndStop(AudioHandle handle, float duration)
        {
            if (!handle.IsValid)
            {
                return;
            }

            if (duration <= 0f)
            {
                Stop(handle);
                return;
            }

            StartCoroutine(FadeOutCoroutine(handle, duration));
        }

        private System.Collections.IEnumerator FadeOutCoroutine(AudioHandle handle, float duration)
        {
            if (!handle.IsValid)
                yield break;

            if (!_activeHandles.TryGetValue(handle.source, out var token))
                yield break;

            if (token != handle.token)
                yield break;

            var src = handle.source;
            if (!src)
                yield break;

            float startVol = src.volume;
            float t = 0f;

            while (t < duration)
            {
                if (!src || !src.isPlaying)
                    break;

                if (!_activeHandles.TryGetValue(src, out var curToken) || curToken != token)
                    break;

                t += Time.unscaledDeltaTime;
                float k = 1f - Mathf.Clamp01(t / duration);
                src.volume = startVol * k;
                yield return null;
            }

            Stop(handle);
        }

        private AudioHandle RegisterHandle(AudioSource src, AudioCategory cat)
        {
            if (!src) return default;

            var handle = new AudioHandle
            {
                source = src,
                token = _nextHandleToken++
            };

            _activeHandles[src] = handle.token;
            _activeCategories[src] = cat; // ✅ NEW
            return handle;
        }

        public void StopAllExceptCategory(AudioCategory except)
        {
            // Stop những source đang play (ưu tiên source đang isPlaying)
            foreach (var src in _pool2D)
                StopIfNotExcept(src, except);

            foreach (var src in _pool3D)
                StopIfNotExcept(src, except);
        }

        private void StopIfNotExcept(AudioSource src, AudioCategory except)
        {
            if (!src || !src.isPlaying) return;

            // Nếu không có record category (edge-case) -> coi như non-UI để chặn “lọt âm”
            if (!_activeCategories.TryGetValue(src, out var cat))
                cat = AudioCategory.SFX;

            if (cat == except) return;

            src.Stop();
            src.clip = null;
            _activeHandles.Remove(src);
            _activeCategories.Remove(src);
        }

        public bool IsHandleValid(AudioHandle handle)
        {
            if (!handle.IsValid) return false;
            return _activeHandles.TryGetValue(handle.source, out var token) && token == handle.token;
        }

        public bool IsHandlePlaying(AudioHandle handle)
        {
            return IsHandleValid(handle) && handle.source != null && handle.source.isPlaying;
        }


        #endregion

        #region Core internal play / pooling

        private AudioSource CreateSource(bool is3D)
        {
            var go = new GameObject(is3D ? "[Audio_3D]" : "[Audio_2D]");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();

            src.playOnAwake = false;
            src.spatialBlend = is3D ? 1f : 0f;
            src.rolloffMode = AudioRolloffMode.Linear;

            return src;
        }

        private AudioSource GetFreeSource(bool is3D)
        {
            var pool = is3D ? _pool3D : _pool2D;

            for (int i = 0; i < pool.Count; i++)
            {
                var src = pool[i];

                // Nếu slot trong pool đã bị destroy → tạo source mới thay thế
                if (!src)
                {
                    src = CreateSource(is3D);
                    pool[i] = src;
                    return src;
                }

                // Source tồn tại và không phát gì → tái sử dụng
                if (!src.isPlaying)
                    return src;
            }

            // Không còn source rảnh → tạo thêm
            var extra = CreateSource(is3D);
            pool.Add(extra);
            return extra;
        }

        private void ConfigureSource(AudioSource src, AudioEventSO ev, bool force2D = false, bool force3D = false)
        {
            if (!src || !ev)
                return;

            bool is3D = ev.is3D;
            if (force2D) is3D = false;
            if (force3D) is3D = true;

            src.outputAudioMixerGroup = GetGroupForCategory(ev.category);
            src.loop = ev.loop;
            src.volume = ev.volume;
            src.pitch = ev.GetRandomPitch();

            src.spatialBlend = is3D ? 1f : 0f;

            if (is3D)
            {
                src.minDistance = ev.minDistance;
                src.maxDistance = ev.maxDistance;
                src.rolloffMode = AudioRolloffMode.Linear;
            }

            src.clip = ev.GetRandomClip();
        }

        private AudioHandle Play2DInternal(AudioEventSO ev)
        {
            if (!ev)
                return default;

            var clip = ev.GetRandomClip();
            if (!clip)
                return default;

            var src = GetFreeSource(is3D: false);
            if (!src)
                return default;

            EnsureSourceReusable(src);
            src.transform.SetParent(transform, false);

            ConfigureSource(src, ev, force2D: true);
            src.transform.localPosition = Vector3.zero;
            src.gameObject.SetActive(true);
            src.Play();

            return RegisterHandle(src, ev.category);
        }

        private AudioHandle Play3DAtPointInternal(AudioEventSO ev, Vector3 position)
        {
            if (!ev)
                return default;

            var clip = ev.GetRandomClip();
            if (!clip)
                return default;

            var src = GetFreeSource(is3D: true);
            if (!src)
                return default;

            EnsureSourceReusable(src);
            src.transform.SetParent(transform, false);

            ConfigureSource(src, ev, force3D: true);
            src.transform.position = position;
            src.gameObject.SetActive(true);
            src.Play();

            return RegisterHandle(src, ev.category);
        }

        private AudioHandle Play3DAttachedInternal(AudioEventSO ev, Transform target)
        {
            if (!ev || !target)
                return default;

            var clip = ev.GetRandomClip();
            if (!clip)
                return default;

            var src = GetFreeSource(is3D: true);
            if (!src)
                return default;

            EnsureSourceReusable(src);

            ConfigureSource(src, ev, force3D: true);
            src.transform.SetParent(target, false);
            src.transform.localPosition = Vector3.zero;
            src.gameObject.SetActive(true);
            src.Play();

            return RegisterHandle(src, ev.category);
        }

        #endregion
    }
}
