using UnityEngine;

namespace EFYV.Core.Utils
{
    // Shared version stamp backing the Singleton<T> negative cache (#24).
    //
    // INVALIDATION DESIGN (explicit): a cached "no instance exists" answer is only
    // trusted while this stamp matches the value recorded at miss time. The stamp
    // is bumped:
    //   1. Automatically whenever ANY Singleton<T> registers itself in Awake. This
    //      covers scene loads end-to-end: a singleton can only become discoverable
    //      by FindObjectOfType once its Awake has run (active scene objects Awake
    //      on load; inactive objects are invisible to FindObjectOfType anyway), and
    //      that Awake bumps the stamp before anything can observe a stale miss.
    //   2. Explicitly via Invalidate() at scene-transition seams that bypass Awake
    //      (MapManager map switches call it; test harnesses call it between groups
    //      after constructing components without invoking Awake).
    public static class SingletonSearchCache
    {
        private static int version;

        public static int Version => version;

        public static void Invalidate() => version++;
    }

    public abstract class Singleton<T> : MonoBehaviour where T : Component
    {
        private static T _instance;

        // Negative cache (#24): remembers that a FindObjectOfType sweep came up
        // empty so hot paths (SpawnManager.Update calls TryGetInstance every frame)
        // stop paying for a full scene scan per call. See SingletonSearchCache for
        // the invalidation contract.
        private static bool missedSearch;
        private static int missedSearchVersion;

        protected bool IsSingletonInstance => _instance == this;

        public static T Instance
        {
            get
            {
                if (_instance == null)
                {
                    if (missedSearch && missedSearchVersion == SingletonSearchCache.Version)
                    {
                        return null; // Cached miss: skip FindObjectOfType entirely.
                    }

                    _instance = FindAnyObjectByType<T>();
                    if (_instance == null)
                    {
                        missedSearch = true;
                        missedSearchVersion = SingletonSearchCache.Version;
                    }
                    else
                    {
                        missedSearch = false;
                    }
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
                missedSearch = false;
                // A new singleton registered: refresh every type's negative cache
                // (see SingletonSearchCache invalidation rule 1).
                SingletonSearchCache.Invalidate();
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
