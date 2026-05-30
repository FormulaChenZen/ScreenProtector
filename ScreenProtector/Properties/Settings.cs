using System;
using System.IO;
using System.Text.Json;

namespace ScreenProtector.Properties
{
    public class Settings
    {
        private static readonly string AppDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenProtector");
        private static readonly string SettingsFile = Path.Combine(AppDataPath, "settings.json");

        public static Settings Default { get; } = Load();

        public int IdleSecondsThreshold { get; set; } = 60;
        public bool EnableIdleCheck { get; set; } = false;
        public bool EnableStartup { get; set; } = false;

        private static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var s = JsonSerializer.Deserialize<Settings>(json);
                    if (s != null) return s;
                }
            }
            catch
            {
                // ignore and return defaults
            }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(AppDataPath))
                    Directory.CreateDirectory(AppDataPath);
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch
            {
                // ignore errors
            }
        }
    }
}
