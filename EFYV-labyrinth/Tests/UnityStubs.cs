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

        public static T FindObjectOfType<T>() where T : Object
        {
            return Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(item => !item.IsDestroyed);
        }

        public static T[] FindObjectsOfType<T>() where T : Object
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

        public T GetComponent<T>()
        {
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

    public class Sprite : Object { }

    public class SpriteRenderer : Component
    {
        public Sprite sprite;
    }

    public class Camera : Behaviour
    {
        public static Camera main;
        public float orthographicSize = 5f;
        public float aspect = 16f / 9f;
    }

    public class Collider2D : Component { }
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
                var cloneObject = new GameObject((sourceComponent.gameObject?.name ?? sourceComponent.GetType().Name) + "(Clone)");
                Component destination = cloneObject.AddComponent(sourceComponent.GetType(), false);
                CopyFields(sourceComponent, destination);
                cloneObject.transform.SetParent(parent);
                InvokeLifecycle(destination, "Awake");
                return destination;
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
                    if (field.IsStatic || field.Name == "gameObject") continue;
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

        public static T LoadAssetAtPath<T>(string path) where T : Object
        {
            return path != null && mainAssets.TryGetValue(path, out Object value) ? value as T : null;
        }

        public static void CreateAsset(Object asset, string path) => mainAssets[path] = asset;
        public static void SaveAssets() { }
        public static void ImportAsset(string path, ImportAssetOptions options) => Imports.Add((path, options));
        public static Object[] LoadAllAssetsAtPath(string path) => path != null && allAssets.TryGetValue(path, out Object[] values) ? values : Array.Empty<Object>();
        public static void SetAllAssetsAtPath(string path, params Object[] values) => allAssets[path] = values ?? Array.Empty<Object>();
        public static void SetMainAsset(string path, Object value) => mainAssets[path] = value;
        internal static void Reset() { mainAssets.Clear(); allAssets.Clear(); Imports.Clear(); }
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
        internal static void Reset() { isPlaying = false; delayCall = null; }
    }

    [Flags]
    public enum GizmoType { Selected = 1, Active = 2 }

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
