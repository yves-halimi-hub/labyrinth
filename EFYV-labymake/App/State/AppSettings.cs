using System;
using System.IO;
using System.Text.Json;

namespace EFYVLabyMake.App.State
{
    // Cross-project shell preferences persisted outside any single project
    // (item #5: the Unity project path is remembered between sessions and
    // applied to newly created/opened projects). A plain POCO so it round-trips
    // through System.Text.Json with no framework dependency.
    public sealed class AppSettings
    {
        public string UnityProjectPath { get; set; } = "";
    }

    // Reads/writes AppSettings as a small JSON file under LocalApplicationData.
    // Load NEVER throws (a missing/corrupt file yields defaults); Save swallows
    // I/O failures - a settings write is best-effort and must not break editing.
    public sealed class AppSettingsStore
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly string settingsPath;

        public string SettingsPath => settingsPath;

        public AppSettingsStore()
            : this(DefaultSettingsPath())
        {
        }

        public AppSettingsStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(nameof(path));
            settingsPath = path;
        }

        public AppSettings Load()
        {
            try
            {
                if (!File.Exists(settingsPath)) return new AppSettings();
                string json = File.ReadAllText(settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
            }
            catch (Exception exception) when (
                exception is IOException ||
                exception is UnauthorizedAccessException ||
                exception is JsonException)
            {
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            try
            {
                string directory = Path.GetDirectoryName(settingsPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, SerializerOptions));
            }
            catch (Exception exception) when (
                exception is IOException || exception is UnauthorizedAccessException)
            {
            }
        }

        private static string DefaultSettingsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EFYVLabyMake",
                "settings.json");
        }
    }
}
