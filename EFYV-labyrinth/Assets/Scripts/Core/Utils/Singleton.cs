using UnityEngine;

namespace EFYV.Core.Utils
{
    public abstract class Singleton<T> : MonoBehaviour where T : Component
    {
        private static T _instance;
        protected bool IsSingletonInstance => _instance == this;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<T>();
                }
                return _instance;
            }
        }

        public static bool TryGetInstance(out T instance)
        {
            instance = Instance;
            return instance != null;
        }

        protected virtual void Awake()
        {
            if (_instance == null)
            {
                _instance = this as T;
                DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                Destroy(gameObject);
            }
        }

        protected virtual void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
