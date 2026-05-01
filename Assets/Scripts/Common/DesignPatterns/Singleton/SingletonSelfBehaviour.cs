using UnityEngine;

namespace NIX.Core.DesignPatterns
{
    public class SingletonSelfBehaviour<T> where T : MonoBehaviour
    {
        private static T instance;

        public static T Instance
        {
            get
            {
                if (instance == null)
                {
#if UNITY_6000_0_OR_NEWER
                    instance = GameObject.FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
                    instance = GameObject.FindObjectOfType<T>();
#endif
                }
                if (instance == null)
                {
                    Debug.LogError(typeof(T).Name + " == null");
                }
                return instance;
            }
        }

        public virtual void OnDestroy()
        {
            instance = null;
        }
    }
}
