using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using msfsclient.Contracts.Services;

namespace msfsclient.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly string _settingsFilePath;
        private readonly bool _isPackaged;
        private Dictionary<string, object> _portableSettings = new();

        public SettingsService()
        {
            string localFolder;
            try
            {
                // Check if packaged
                localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                _isPackaged = true;
            }
            catch (Exception)
            {
                // Fallback for unpackaged mode
                localFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "msfsclient");
                if (!Directory.Exists(localFolder))
                {
                    Directory.CreateDirectory(localFolder);
                }
                _isPackaged = false;
            }

            _settingsFilePath = Path.Combine(localFolder, "settings.json");
            
            if (!_isPackaged)
            {
                LoadPortableSettings();
            }
        }

        private void LoadPortableSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    _portableSettings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsService: Error loading settings: {ex.Message}");
            }
        }

        private void SavePortableSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(_portableSettings);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsService: Error saving settings: {ex.Message}");
            }
        }

        public T GetValue<T>(string key, T defaultValue)
        {
            if (_isPackaged)
            {
                try
                {
                    var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    if (localSettings.Values.TryGetValue(key, out var value))
                    {
                        return (T)value;
                    }
                }
                catch { }
            }
            else
            {
                if (_portableSettings.TryGetValue(key, out var value))
                {
                    if (value is JsonElement element)
                    {
                        return element.Deserialize<T>() ?? defaultValue;
                    }
                    return (T)value;
                }
            }

            return defaultValue;
        }

        public void SetValue<T>(string key, T value)
        {
            if (_isPackaged)
            {
                try
                {
                    var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                    localSettings.Values[key] = value;
                }
                catch { }
            }
            else
            {
                _portableSettings[key] = value!;
                SavePortableSettings();
            }
        }
    }
}
