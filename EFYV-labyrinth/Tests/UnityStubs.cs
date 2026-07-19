using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string header) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HideInInspector : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TextAreaAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ContextMenu : Attribute
    {
        public ContextMenu(string menuItem) { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireComponent : Attribute
    {
        public RequireComponent(Type requiredComponent) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string fileName;
        public string menuName;
    }

    public class Object
    {
        private static int nextId = 1;
        internal bool IsDestroyed;
        private readonly int instanceId = nextId++;
        public string name;

        public int GetInstanceID() => instanceId;

        // Unity 6.6 replacement for the deprecated GetInstanceID (CS0619 in
        // 6000.6.0b4). Real EntityId converts implicitly to int; the stub
        // returns the same monotonic id so pool keys stay stable either way.
        public int GetEntityId() => instanceId;

        public static T Instantiate<T>(T original) where T : Object
        {
            return (T)TestRuntime.CloneObject(original, null);
        }

        public static T Instantiate<T>(T original, Transform parent) where T : Object
        {
            return (T)TestRuntime.CloneObject(original, parent);
        }

        public static void Destroy(Object target)
        {
            TestRuntime.DestroyObject(target);
        }

        public static void DontDestroyOnLoad(Object target) { }

        // Monotonic instrumentation counter (batch2/game-managers agent): tests
        // assert deltas to prove hot paths negative-cache scene sweeps (#24,
        // Singleton.TryGetInstance) instead of re-scanning per call. Never reset;
        // compare before/after snapshots only (mirrors GetComponentCalls).
        public static long FindObjectOfTypeCalls;

        public static T FindObjectOfType<T>() where T : Object
        {
            FindObjectOfTypeCalls++;
            return Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(item => !item.IsDestroyed);
        }

        public static T[] FindObjectsOfType<T>() where T : Object
        {
            return Resources.FindObjectsOfTypeAll<T>().Where(item => !item.IsDestroyed).ToArray();
        }

        // Unity 6 replacements for the CS0618-deprecated finders. The counter is
        // shared with FindObjectOfTypeCalls so negative-cache tests keep working
        // unchanged whichever entry point product code uses.
        public static T FindFirstObjectByType<T>() where T : Object
        {
            FindObjectOfTypeCalls++;
            return Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(item => !item.IsDestroyed);
        }

        // Unity 6.6 deprecates FindFirstObjectByType as well (instance-ID-ordering
        // dependent); FindAnyObjectByType is the recommended replacement and the
        // one product code uses.
        public static T FindAnyObjectByType<T>() where T : Object
        {
            FindObjectOfTypeCalls++;
            return Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(item => !item.IsDestroyed);
        }

        public static T[] FindObjectsByType<T>() where T : Object
        {
            return Resources.FindObjectsOfTypeAll<T>().Where(item => !item.IsDestroyed).ToArray();
        }
    }

    public class ScriptableObject : Object
    {
        public ScriptableObject()
        {
            TestRuntime.Register(this);
        }

        public static T CreateInstance<T>() where T : ScriptableObject
        {
            return (T)CreateInstance(typeof(T));
        }

        public static ScriptableObject CreateInstance(Type type)
        {
            return (ScriptableObject)Activator.CreateInstance(type, true);
        }
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject?.transform;

        public T GetComponent<T>() => gameObject == null ? default : gameObject.GetComponent<T>();
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; } = true;
    }

    public class MonoBehaviour : Behaviour
    {
        protected Coroutine StartCoroutine(IEnumerator routine)
        {
            if (routine == null) return null;
            int remaining = 100000;
            while (remaining-- > 0 && routine.MoveNext()) { }
            if (remaining <= 0) throw new InvalidOperationException("Coroutine did not terminate in the test runtime.");
            return new Coroutine();
        }
    }

    public sealed class Coroutine { }

    public class GameObject : Object
    {
        private readonly List<Component> components = new List<Component>();
        public bool activeSelf { get; private set; } = true;
        public bool activeInHierarchy => activeSelf && !IsDestroyed;
        public Transform transform { get; }
        public SceneManagement.Scene scene { get; set; } = new SceneManagement.Scene(true, true);

        public GameObject(string objectName = "GameObject")
        {
            name = objectName;
            transform = new Transform { gameObject = this };
            components.Add(transform);
            TestRuntime.Register(this);
            TestRuntime.Register(transform);
        }

        public T AddComponent<T>() where T : Component
        {
            return (T)AddComponent(typeof(T), true);
        }

        public Component AddComponent(Type type)
        {
            return AddComponent(type, true);
        }

        internal Component AddComponent(Type type, bool invokeAwake)
        {
            if (!typeof(Component).IsAssignableFrom(type)) throw new ArgumentException(nameof(type));
            var component = (Component)Activator.CreateInstance(type, true);
            component.gameObject = this;
            components.Add(component);
            TestRuntime.Register(component);
            if (invokeAwake) TestRuntime.InvokeLifecycle(component, "Awake");
            return component;
        }

        // Monotonic instrumentation counter: tests assert deltas to prove hot paths
        // memoize component lookups (e.g. the Projectile trigger memo) instead of
        // re-scanning per call. Never reset; compare before/after snapshots only.
        public static long GetComponentCalls;

        public T GetComponent<T>()
        {
            GetComponentCalls++;
            Type requested = typeof(T);
            for (int index = 0; index < components.Count; index++)
            {
                if (requested.IsAssignableFrom(components[index].GetType())) return (T)(object)components[index];
            }
            return default;
        }

        internal IReadOnlyList<Component> Components => components;
        public void SetActive(bool active) => activeSelf = active;
    }

    public class Transform : Component
    {
        public Vector3 position;
        public Vector3 localPosition;
        public Quaternion rotation;
        public Transform parent { get; private set; }
        public Matrix4x4 localToWorldMatrix => Matrix4x4.identity;

        public void SetParent(Transform newParent) => parent = newParent;
        public Vector3 TransformPoint(Vector3 point) => position + point;
    }

    public struct Vector2
    {
        public float x;
        public float y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float magnitude => MathF.Sqrt((x * x) + (y * y));
        public Vector2 normalized => magnitude == 0f ? default : new Vector2(x / magnitude, y / magnitude);
        public static Vector2 zero => default;
        public static Vector2 operator +(Vector2 left, Vector2 right) => new Vector2(left.x + right.x, left.y + right.y);
        public static Vector2 operator -(Vector2 left, Vector2 right) => new Vector2(left.x - right.x, left.y - right.y);
        public static Vector2 operator *(Vector2 value, float scalar) => new Vector2(value.x * scalar, value.y * scalar);
        public static implicit operator Vector2(Vector3 value) => new Vector2(value.x, value.y);
        public static implicit operator Vector3(Vector2 value) => new Vector3(value.x, value.y, 0f);
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
        public Vector3(float x, float y, float z = 0f) { this.x = x; this.y = y; this.z = z; }
        public float magnitude => MathF.Sqrt((x * x) + (y * y) + (z * z));
        public Vector3 normalized => magnitude == 0f ? default : new Vector3(x / magnitude, y / magnitude, z / magnitude);
        public static Vector3 zero => default;
        public static Vector3 operator +(Vector3 left, Vector3 right) => new Vector3(left.x + right.x, left.y + right.y, left.z + right.z);
        public static Vector3 operator -(Vector3 left, Vector3 right) => new Vector3(left.x - right.x, left.y - right.y, left.z - right.z);
        public static Vector3 operator *(Vector3 value, float scalar) => new Vector3(value.x * scalar, value.y * scalar, value.z * scalar);
        public static Vector3 operator *(float scalar, Vector3 value) => value * scalar;
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => new Quaternion { w = 1f };
    }

    public struct Rect
    {
        public float x;
        public float y;
        public float width;
        public float height;
        public Rect(float x, float y, float width, float height)
        {
            this.x = x; this.y = y; this.width = width; this.height = height;
        }
    }

    public struct Matrix4x4
    {
        public static Matrix4x4 identity => default;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color cyan => new Color(0f, 1f, 1f);
        public static Color magenta => new Color(1f, 0f, 1f);
        // batch3.4 agent (item #7): additive members for authored effect colors.
        public static Color white => new Color(1f, 1f, 1f);
        public static Color Lerp(Color a, Color b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(
                a.r + (b.r - a.r) * t,
                a.g + (b.g - a.g) * t,
                a.b + (b.b - a.b) * t,
                a.a + (b.a - a.a) * t);
        }
    }

    // batch2/unity-project agent: additive member for GameBootstrap placeholder art.
    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public static class Mathf
    {
        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
        public static float Min(float left, float right) => MathF.Min(left, right);
        public static bool Approximately(float left, float right)
        {
            return MathF.Abs(left - right) <= MathF.Max(1e-6f * MathF.Max(MathF.Abs(left), MathF.Abs(right)), 1e-6f);
        }
    }

    public static class Time
    {
        public static float deltaTime = 1f / 60f;
        public static float unscaledTime;
    }

    public static class Input
    {
        private static readonly Dictionary<string, float> axes = new Dictionary<string, float>(StringComparer.Ordinal);
        public static float GetAxisRaw(string name) => axes.TryGetValue(name, out float value) ? value : 0f;
        public static void SetAxisRaw(string name, float value) => axes[name] = value;
        internal static void Reset() => axes.Clear();
    }

    public static class Debug
    {
        public static readonly List<string> Messages = new List<string>();
        public static void Log(object value) => Messages.Add(value?.ToString());
        public static void LogWarning(object value) => Messages.Add(value?.ToString());
        public static void LogError(object value) => Messages.Add(value?.ToString());
        public static void LogException(Exception exception) => Messages.Add(exception?.GetType().Name + ": " + exception?.Message);
        public static void LogFormat(string format, params object[] args) => Messages.Add(string.Format(format, args));
        public static void LogWarningFormat(string format, params object[] args) => Messages.Add(string.Format(format, args));
    }

    public class Sprite : Object
    {
        // batch2/unity-project agent: additive members for GameBootstrap sprite generation.
        public Texture2D texture { get; private set; }
        public Rect rect { get; private set; }
        public Vector2 pivot { get; private set; }
        public float pixelsPerUnit { get; private set; }

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit)
        {
            var sprite = new Sprite
            {
                texture = texture,
                rect = rect,
                pivot = pivot,
                pixelsPerUnit = pixelsPerUnit,
            };
            TestRuntime.Register(sprite);
            return sprite;
        }
    }

    public class SpriteRenderer : Component
    {
        public Sprite sprite;
        // batch3.4 agent (item #7): authored flash/tint effects recolor here.
        public Color color = Color.white;
    }

    public class Camera : Behaviour
    {
        public static Camera main;
        public float orthographicSize = 5f;
        public float aspect = 16f / 9f;
    }

    public class Collider2D : Component { }

    // Item #14: LivingEntity syncs the designer Hurtbox onto this collider's
    // offset/size (local units); tests inspect those fields after a sync.
    public class BoxCollider2D : Collider2D
    {
        public Vector2 offset;
        public Vector2 size;
    }

    public class Collision2D
    {
        public GameObject gameObject;
        public Collision2D(GameObject gameObject) { this.gameObject = gameObject; }
    }

    public enum TextureFormat { RGBA32 }
    public enum FilterMode { Point, Bilinear, Trilinear }

    public class Texture2D : Object
    {
        private Array rawData;
        public int width { get; }
        public int height { get; }
        public Texture2D(int width, int height, TextureFormat format, bool mipmaps)
        {
            this.width = width; this.height = height;
        }
        public void ReadPixels(Rect rect, int x, int y) { }
        public void Apply(bool updateMipmaps) { }
        // batch2/unity-project agent: additive members for GameBootstrap sprite generation.
        public FilterMode filterMode;
        private Color32[] pixels32;
        public void SetPixels32(Color32[] colors) => pixels32 = colors;
        public Color32[] GetPixels32() => pixels32;
        public Unity.Collections.NativeArray<T> GetRawTextureData<T>() where T : unmanaged
        {
            if (!(rawData is T[] values))
            {
                values = new T[checked(width * height)];
                rawData = values;
            }
            return new Unity.Collections.NativeArray<T>(values);
        }
    }

    public class RenderTexture : Object
    {
        public static RenderTexture active;
        public int width;
        public int height;
    }

    public static class Graphics
    {
        public static int BlitCount { get; private set; }
        public static void Blit(Object source, Object destination) => BlitCount++;
        internal static void Reset() => BlitCount = 0;
    }

    public static class Application
    {
        public static string persistentDataPath = System.IO.Path.GetTempPath();
    }

    public static class Resources
    {
        public static T[] FindObjectsOfTypeAll<T>() where T : Object
        {
            return TestRuntime.Objects.OfType<T>().Where(item => !item.IsDestroyed).ToArray();
        }
    }

    public static class Gizmos
    {
        public static Matrix4x4 matrix;
        public static Color color;
        public static readonly List<(Vector3 Center, Vector3 Size)> Cubes = new List<(Vector3, Vector3)>();
        public static void DrawWireCube(Vector3 center, Vector3 size) => Cubes.Add((center, size));
    }

    // Item #4: minimal IMGUI surface so the editor-only EFYVSpawnPaletteWindow
    // COMPILES in the headless harness (its list/selection logic lives in the
    // plain SpawnPaletteModel, which is what the tests exercise). These are
    // no-op signature stubs, never driven by a test.
    public sealed class GUIContent
    {
        public string text;
        public GUIContent() { }
        public GUIContent(string text) { this.text = text; }
    }

    public sealed class GUIStyle { }

    public static class GUILayout
    {
        public static bool Button(string text) => false;
        public static void Label(string text) { }
        public static void Space(float pixels) { }
        public static void BeginHorizontal() { }
        public static void EndHorizontal() { }
        public static void BeginVertical() { }
        public static void EndVertical() { }
    }

    public static class TestRuntime
    {
        internal static readonly List<Object> Objects = new List<Object>();
        public static void Register(Object value)
        {
            if (value != null && !Objects.Contains(value)) Objects.Add(value);
        }

        public static T CreateComponent<T>(string name = null, bool invokeAwake = true) where T : Component
        {
            var gameObject = new GameObject(name ?? typeof(T).Name);
            return (T)gameObject.AddComponent(typeof(T), invokeAwake);
        }

        internal static Object CloneObject(Object original, Transform parent)
        {
            if (original == null) return null;
            if (original is GameObject originalGameObject)
            {
                var clone = new GameObject((original.name ?? "GameObject") + "(Clone)");
                foreach (Component source in originalGameObject.Components)
                {
                    if (source is Transform) continue;
                    Component destination = clone.AddComponent(source.GetType(), false);
                    CopyFields(source, destination);
                    InvokeLifecycle(destination, "Awake");
                }
                clone.transform.SetParent(parent);
                return clone;
            }
            if (original is Component sourceComponent)
            {
                // Unity's Instantiate(component) duplicates the WHOLE GameObject
                // (every sibling component) and returns the clone's component of
                // the requested type. Mirror that so pooled clones keep their
                // SpriteRenderer / collider siblings - the item #13 runtime
                // flipbook and item #14 hurtbox sync read those on the clone.
                GameObject sourceObject = sourceComponent.gameObject;
                if (sourceObject == null)
                {
                    var loneObject = new GameObject(sourceComponent.GetType().Name + "(Clone)");
                    Component lone = loneObject.AddComponent(sourceComponent.GetType(), false);
                    CopyFields(sourceComponent, lone);
                    loneObject.transform.SetParent(parent);
                    InvokeLifecycle(lone, "Awake");
                    return lone;
                }

                var clonedObject = (GameObject)CloneObject(sourceObject, parent);
                Type requested = sourceComponent.GetType();
                foreach (Component candidate in clonedObject.Components)
                {
                    if (candidate.GetType() == requested) return candidate;
                }
                return null;
            }
            if (original is ScriptableObject)
            {
                ScriptableObject clone = ScriptableObject.CreateInstance(original.GetType());
                CopyFields(original, clone);
                return clone;
            }
            throw new NotSupportedException(original.GetType().FullName);
        }

        internal static void DestroyObject(Object target)
        {
            if (target == null || target.IsDestroyed) return;
            if (target is GameObject gameObject)
            {
                foreach (Component component in gameObject.Components.ToArray())
                {
                    InvokeLifecycle(component, "OnDestroy");
                    component.IsDestroyed = true;
                }
                gameObject.SetActive(false);
            }
            else
            {
                InvokeLifecycle(target, "OnDestroy");
                if (target is Component component) component.gameObject?.SetActive(false);
            }
            target.IsDestroyed = true;
        }

        internal static void CopyFields(object source, object destination)
        {
            for (Type type = source.GetType(); type != null; type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsStatic || field.Name == "gameObject" || field.Name == "<gameObject>k__BackingField") continue;
                    field.SetValue(destination, field.GetValue(source));
                }
            }
        }

        public static object InvokeLifecycle(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = null;
            for (Type type = target.GetType(); type != null && method == null; type = type.BaseType)
            {
                method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            }
            return method?.Invoke(target, arguments);
        }

        public static void Reset()
        {
            foreach (Object value in Objects.ToArray())
            {
                if (!value.IsDestroyed) DestroyObject(value);
            }
            Objects.Clear();
            Input.Reset();
            Debug.Messages.Clear();
            Graphics.Reset();
            Gizmos.Cubes.Clear();
            Camera.main = null;
            Time.deltaTime = 1f / 60f;
            Time.unscaledTime = 0f;
            UnityEditor.AssetDatabase.Reset();
            UnityEditor.AssetImporter.Reset();
            UnityEditor.EditorApplication.Reset();
        }
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        private readonly bool valid;
        public bool isLoaded { get; }
        public Scene(bool valid, bool loaded) { this.valid = valid; isLoaded = loaded; }
        public bool IsValid() => valid;
    }
}

namespace Unity.Collections
{
    public readonly struct NativeArray<T> where T : unmanaged
    {
        internal readonly T[] Values;
        public NativeArray(T[] values) { Values = values; }
        public int Length => Values?.Length ?? 0;
        public T this[int index] { get => Values[index]; set => Values[index] = value; }
    }
}

namespace Unity.Collections.LowLevel.Unsafe
{
    public static class NativeArrayUnsafeUtility
    {
        public static unsafe void* GetUnsafePtr<T>(Unity.Collections.NativeArray<T> array) where T : unmanaged
        {
            return null;
        }
    }
}

namespace UnityEditor
{
    using UnityEngine;

    public class AssetPostprocessor
    {
        protected internal string assetPath;
        protected internal AssetImporter assetImporter;
    }

    public enum TextureImporterType { Default, Sprite }
    public enum SpriteImportMode { None, Single, Multiple }
    public enum TextureImporterCompression { Uncompressed, Compressed }
    public enum TextureImporterNPOTScale { None, ToNearest, ToLarger, ToSmaller }
    public enum SpriteAlignment { Center = 0 }

    public struct SpriteMetaData
    {
        public string name;
        public Rect rect;
        public int alignment;
        public Vector2 pivot;
    }

    public class AssetImporter : Object
    {
        private static readonly Dictionary<string, AssetImporter> importers = new Dictionary<string, AssetImporter>(StringComparer.OrdinalIgnoreCase);
        public string assetPath;
        public static AssetImporter GetAtPath(string path) => path != null && importers.TryGetValue(path, out AssetImporter value) ? value : null;
        public static void Register(string path, AssetImporter importer)
        {
            importer.assetPath = path;
            importers[path] = importer;
        }
        internal static void Reset() => importers.Clear();
    }

    public class TextureImporter : AssetImporter
    {
        public TextureImporterType textureType;
        public bool mipmapEnabled;
        public FilterMode filterMode;
        public TextureImporterCompression textureCompression;
        public bool alphaIsTransparency;
        public float spritePixelsPerUnit;
        public int maxTextureSize;
        public TextureImporterNPOTScale npotScale;
        public SpriteImportMode spriteImportMode;
        public SpriteMetaData[] spritesheet;
    }

    [Flags]
    public enum ImportAssetOptions
    {
        Default = 0,
        ForceUpdate = 1,
        ForceSynchronousImport = 2
    }

    public static class AssetDatabase
    {
        private static readonly Dictionary<string, Object> mainAssets = new Dictionary<string, Object>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Object[]> allAssets = new Dictionary<string, Object[]>(StringComparer.OrdinalIgnoreCase);
        public static readonly List<(string Path, ImportAssetOptions Options)> Imports = new List<(string, ImportAssetOptions)>();
        // Item #27: counts SaveAssets() calls so tests can pin that the batch
        // postprocessor coalesces to ONE flush per group.
        public static int SaveAssetsCount { get; private set; }

        public static T LoadAssetAtPath<T>(string path) where T : Object
        {
            return path != null && mainAssets.TryGetValue(path, out Object value) ? value as T : null;
        }

        public static void CreateAsset(Object asset, string path) => mainAssets[path] = asset;
        public static void SaveAssets() => SaveAssetsCount++;
        // Item #4: signature stubs for the spawn-palette window's editor-only
        // asset discovery (never driven by a test; the palette MODEL is what the
        // harness exercises).
        public static string[] FindAssets(string filter) => Array.Empty<string>();
        public static string[] FindAssets(string filter, string[] searchInFolders) => Array.Empty<string>();
        public static string GUIDToAssetPath(string guid) => null;
        public static void ImportAsset(string path, ImportAssetOptions options) => Imports.Add((path, options));
        public static Object[] LoadAllAssetsAtPath(string path) => path != null && allAssets.TryGetValue(path, out Object[] values) ? values : Array.Empty<Object>();
        public static void SetAllAssetsAtPath(string path, params Object[] values) => allAssets[path] = values ?? Array.Empty<Object>();
        public static void SetMainAsset(string path, Object value) => mainAssets[path] = value;
        internal static void Reset() { mainAssets.Clear(); allAssets.Clear(); Imports.Clear(); SaveAssetsCount = 0; }
    }

    public static class EditorUtility
    {
        public static readonly HashSet<Object> DirtyObjects = new HashSet<Object>();
        public static void SetDirty(Object target) => DirtyObjects.Add(target);
    }

    public static class EditorApplication
    {
        public static bool isPlaying;
        public static event Action delayCall;
        public static void InvokeDelayCalls()
        {
            Action pending = delayCall;
            delayCall = null;
            pending?.Invoke();
        }
        internal static void Reset() { isPlaying = false; delayCall = null; timeSinceStartup = 0d; }

        // b2-pipeline-contract agent: minimal additive stubs for the RawArt
        // watcher ([InitializeOnLoad] poller over EditorApplication.update).
        public static double timeSinceStartup;
        public static event Action update;
        public static void InvokeUpdate()
        {
            update?.Invoke();
        }
    }

    // b2-pipeline-contract agent: additive stub for editor classes that hook
    // EditorApplication.update at load time.
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InitializeOnLoadAttribute : Attribute { }

    // Item #4: minimal EditorWindow / IMGUI surface so EFYVSpawnPaletteWindow
    // COMPILES in the headless harness. The window is never instantiated by a
    // test - its list/selection state machine lives in the plain
    // SpawnPaletteModel, which is what the tests exercise.
    public enum MessageType { None, Info, Warning, Error }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }
    }

    public class EditorWindow : ScriptableObject
    {
        public GUIContent titleContent;
        public void Show() { }
        public void Repaint() { }
        public void Close() { }
        protected static T GetWindow<T>() where T : EditorWindow => CreateInstance<T>();
        protected static T GetWindow<T>(string title) where T : EditorWindow => CreateInstance<T>();
    }

    public static class EditorGUILayout
    {
        public static void HelpBox(string message, MessageType type) { }
        public static void LabelField(string label) { }
        public static void Space() { }
        public static bool Toggle(string label, bool value) => value;
        public static Vector2 Vector2Field(string label, Vector2 value) => value;
        public static Vector3 Vector3Field(string label, Vector3 value) => value;
    }

    [Flags]
    public enum GizmoType { Selected = 1, Active = 2, NonSelected = 4 }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class DrawGizmo : Attribute
    {
        public DrawGizmo(GizmoType gizmo) { }
    }

    public static class Handles
    {
        public static readonly List<(Vector3 Position, string Text)> Labels = new List<(Vector3, string)>();
        public static void Label(Vector3 position, string text) => Labels.Add((position, text));
    }
}

namespace UnityEditor
{
    // Unity 6.6 sprite-provider migration: minimal GUID mirror. Values only
    // need identity + uniqueness inside a test run.
    public struct GUID : IEquatable<GUID>
    {
        private static int nextValue = 1;
        private readonly int value;

        private GUID(int value) { this.value = value; }

        public static GUID Generate() => new GUID(nextValue++);

        public bool Empty() => value == 0;

        public bool Equals(GUID other) => value == other.value;

        public override bool Equals(object obj) => obj is GUID other && Equals(other);

        public override int GetHashCode() => value;
    }
}

namespace UnityEditor.U2D.Sprites
{
    using UnityEngine;

    // Mirrors the 2D Sprite package surface EFYVPixelArtImporter uses. The stub
    // provider reads and writes THROUGH TextureImporter.spritesheet so every
    // existing test that inspects or mutates that field keeps observing exactly
    // what the importer produced; spriteIDs are emulated per importer+name so
    // the reuse-by-name contract is exercisable.
    public class SpriteRect
    {
        public string name;
        public Rect rect;
        public SpriteAlignment alignment;
        public Vector2 pivot;
        public GUID spriteID;
    }

    public class SpriteNameFileIdPair
    {
        public SpriteNameFileIdPair(string name, GUID fileId)
        {
            this.name = name;
            this.fileId = fileId;
        }

        public string name;
        public GUID fileId;
    }

    public interface ISpriteNameFileIdDataProvider
    {
        void SetNameFileIdPairs(IEnumerable<SpriteNameFileIdPair> pairs);
    }

    public interface ISpriteEditorDataProvider
    {
        void InitSpriteEditorDataProvider();
        SpriteRect[] GetSpriteRects();
        void SetSpriteRects(SpriteRect[] rects);
        void Apply();
        T GetDataProvider<T>() where T : class;
    }

    public class SpriteDataProviderFactories
    {
        public void Init() { }

        public ISpriteEditorDataProvider GetSpriteEditorDataProviderFromObject(UnityEngine.Object target)
        {
            return new StubTextureImporterSpriteDataProvider((TextureImporter)target);
        }
    }

    internal sealed class StubTextureImporterSpriteDataProvider : ISpriteEditorDataProvider
    {
        private static readonly Dictionary<TextureImporter, Dictionary<string, GUID>> idsByImporter =
            new Dictionary<TextureImporter, Dictionary<string, GUID>>();

        private readonly TextureImporter importer;
        private SpriteRect[] staged;

        internal StubTextureImporterSpriteDataProvider(TextureImporter importer)
        {
            this.importer = importer;
        }

        public void InitSpriteEditorDataProvider() { }

        public SpriteRect[] GetSpriteRects()
        {
            SpriteMetaData[] slices = importer.spritesheet;
            if (slices == null) return new SpriteRect[0];
            var rects = new SpriteRect[slices.Length];
            for (int i = 0; i < slices.Length; i++)
            {
                rects[i] = new SpriteRect
                {
                    name = slices[i].name,
                    rect = slices[i].rect,
                    alignment = (SpriteAlignment)slices[i].alignment,
                    pivot = slices[i].pivot,
                    spriteID = GetStableId(slices[i].name)
                };
            }
            return rects;
        }

        public void SetSpriteRects(SpriteRect[] rects)
        {
            staged = rects;
        }

        public void Apply()
        {
            if (staged == null) return;
            var slices = new SpriteMetaData[staged.Length];
            for (int i = 0; i < staged.Length; i++)
            {
                slices[i] = new SpriteMetaData
                {
                    name = staged[i].name,
                    rect = staged[i].rect,
                    alignment = (int)staged[i].alignment,
                    pivot = staged[i].pivot
                };
                RecordId(staged[i].name, staged[i].spriteID);
            }
            importer.spritesheet = slices;
            staged = null;
        }

        public T GetDataProvider<T>() where T : class => null;

        private GUID GetStableId(string sliceName)
        {
            if (sliceName == null) return default;
            if (!idsByImporter.TryGetValue(importer, out Dictionary<string, GUID> ids))
            {
                ids = new Dictionary<string, GUID>(StringComparer.Ordinal);
                idsByImporter[importer] = ids;
            }
            if (!ids.TryGetValue(sliceName, out GUID id))
            {
                id = GUID.Generate();
                ids[sliceName] = id;
            }
            return id;
        }

        private void RecordId(string sliceName, GUID id)
        {
            if (sliceName == null) return;
            if (!idsByImporter.TryGetValue(importer, out Dictionary<string, GUID> ids))
            {
                ids = new Dictionary<string, GUID>(StringComparer.Ordinal);
                idsByImporter[importer] = ids;
            }
            ids[sliceName] = id;
        }
    }
}
