using UnityEngine;

namespace NIX.Core.DesignPatterns
{
    public interface IAutoDontDestroy { }

    // ✅ NEW: Marker - type nào implement cái này thì SingletonBehaviour sẽ KHÔNG auto-create
    public interface INoAutoCreate { }

    public class SingletonBehaviour<T> : MonoBehaviour where T : Component
    {
        private static T _instance;

        // ✅ NEW: chặn auto-create lúc đang quit / teardown (thoát playmode)
        private static bool _isQuitting;

        public static T Instance
        {
            get
            {
                // ✅ Nếu đang quitting thì tuyệt đối không tạo thêm gì
                if (_isQuitting)
                    return _instance;

                if (_instance != null) return _instance;

                var objs = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                if (objs is { Length: > 0 })
                    _instance = objs[0];
                if (objs is { Length: > 1 })
                {
                    Debug.LogError("There is more than one " + typeof(T).Name + " in the scene.");
                }

                if (_instance != null) return _instance;

                // ✅ NEW: Nếu type cấm auto-create thì return null (không đẻ)
                if (typeof(INoAutoCreate).IsAssignableFrom(typeof(T)))
                {
                    Debug.LogWarning($"[Singleton] {typeof(T).Name} not found and auto-create is DISABLED (INoAutoCreate).");
                    return null;
                }

                if (typeof(T).Name == nameof(FusionInputProvider))
                {
                    Debug.LogWarning(typeof(T).Name + " not found in scene and auto-create is disabled.");
                    return null;
                }

                GameObject obj = new GameObject { name = typeof(T).Name + "_AutoCreated" };

                if (typeof(T) == typeof(InputHub))
                {
                    Debug.LogWarning("[Singleton] InputHub auto-create blocked.");
                    Object.Destroy(obj);
                    return null;
                }

                _instance = obj.AddComponent<T>();
                return _instance;
            }
            protected set
            {
                _instance = value;
            }
        }

        protected virtual void Awake()
        {
            InitializeSingleton();
        }

        protected virtual void InitializeSingleton()
        {
            if (!Application.isPlaying)
                return;

            if (_instance == null)
            {
                _instance = this as T;

                if (this is IAutoDontDestroy)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (_instance != this)
            {
                Debug.LogWarning($"[Singleton] Destroy duplicate {typeof(T).Name}. keep={_instance.name} kill={gameObject.name}");
                Destroy(gameObject);
            }
        }

        // ✅ NEW: Clear instance đúng chuẩn
        protected virtual void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        // ✅ NEW: Khi quit thì không auto-create nữa
        protected virtual void OnApplicationQuit()
        {
            _isQuitting = true;
        }
    }
}
