using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LumiSqlFormatter.Models;
using Newtonsoft.Json;

namespace LumiSqlFormatter.Services
{
    public class SettingsService
    {
        private static readonly Lazy<SettingsService> _instance = new Lazy<SettingsService>(() => new SettingsService());
        public static SettingsService Instance => _instance.Value;

        private readonly string _appDataFolder;
        private readonly string _settingsFilePath;
        private readonly object _syncLock = new object();

        public FormatterSettings Settings { get; private set; }
        public List<SnippetItem> Snippets { get; private set; }

        public event Action SettingsChanged;
        public event Action SnippetsChanged;

        private SettingsService()
        {
            _appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "LumiSqlFormatter");

            _settingsFilePath = Path.Combine(_appDataFolder, "Settings.json");

            EnsureDirectoryExists(_appDataFolder);
            LoadSettings();
            LoadSnippets();
        }

        public string GetSnippetsDirectory()
        {
            if (!string.IsNullOrWhiteSpace(Settings?.SnippetsDirectoryPath) && Directory.Exists(Settings.SnippetsDirectoryPath))
            {
                return Settings.SnippetsDirectoryPath;
            }

            string defaultSnippetsDir = Path.Combine(_appDataFolder, "Snippets");
            EnsureDirectoryExists(defaultSnippetsDir);
            return defaultSnippetsDir;
        }

        public void SetSnippetsDirectory(string newDirectory)
        {
            if (string.IsNullOrWhiteSpace(newDirectory))
                return;

            EnsureDirectoryExists(newDirectory);
            Settings.SnippetsDirectoryPath = newDirectory;
            SaveSettings(Settings);
            LoadSnippets();
        }

        private void EnsureDirectoryExists(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }
            }
            catch (Exception)
            {
                // Fallback / ignore directory creation failures
            }
        }

        public void LoadSettings()
        {
            lock (_syncLock)
            {
                try
                {
                    if (File.Exists(_settingsFilePath))
                    {
                        string json = File.ReadAllText(_settingsFilePath);
                        Settings = JsonConvert.DeserializeObject<FormatterSettings>(json) ?? new FormatterSettings();

                        // Automatically migrate legacy default Ctrl+Shift+R (conflicts with SSMS IntelliSense) to Ctrl+Alt+R
                        if (string.IsNullOrWhiteSpace(Settings.RefreshDbCacheShortcut) ||
                            string.Equals(Settings.RefreshDbCacheShortcut, "Ctrl+Shift+R", StringComparison.OrdinalIgnoreCase))
                        {
                            Settings.RefreshDbCacheShortcut = "Ctrl+Alt+R";
                            SaveSettings(Settings);
                        }

                        // Ensure default snippets dir is set if empty
                        if (string.IsNullOrWhiteSpace(Settings.SnippetsDirectoryPath))
                        {
                            Settings.SnippetsDirectoryPath = Path.Combine(_appDataFolder, "Snippets");
                        }
                        return;
                    }
                }
                catch (Exception)
                {
                    // Fallback to default
                }

                Settings = new FormatterSettings
                {
                    SnippetsDirectoryPath = Path.Combine(_appDataFolder, "Snippets")
                };
                SaveSettings(Settings);
            }
        }

        public void SaveSettings(FormatterSettings newSettings)
        {
            lock (_syncLock)
            {
                try
                {
                    EnsureDirectoryExists(_appDataFolder);
                    Settings = newSettings ?? new FormatterSettings();
                    string json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                    File.WriteAllText(_settingsFilePath, json);
                }
                catch (Exception)
                {
                    // Log or handle
                }
            }

            SettingsChanged?.Invoke();
        }

        public void DeleteSnippetFile(string prefix)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(prefix)) return;
                string snippetsDir = GetSnippetsDirectory();
                string safeName = string.Join("_", prefix.Split(Path.GetInvalidFileNameChars()));
                string itemPath = Path.Combine(snippetsDir, safeName + ".json");
                if (File.Exists(itemPath))
                {
                    File.Delete(itemPath);
                }
            }
            catch { }
        }

        public void LoadSnippets()
        {
            lock (_syncLock)
            {
                string snippetsDir = GetSnippetsDirectory();
                EnsureDirectoryExists(snippetsDir);

                var loadedList = new Dictionary<string, SnippetItem>(StringComparer.OrdinalIgnoreCase);

                // Helper local function to register snippet
                void RegisterSnippet(SnippetItem item)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Prefix))
                        return;

                    if (string.IsNullOrWhiteSpace(item.Id))
                        item.Id = Guid.NewGuid().ToString();

                    loadedList[item.Prefix] = item;
                }

                // 1. Try loading from Snippets.json inside the snippets folder
                string batchFile = Path.Combine(snippetsDir, "Snippets.json");
                // Fallback check old location %APPDATA%\LumiSqlFormatter\Snippets.json
                string legacyBatchFile = Path.Combine(_appDataFolder, "Snippets.json");

                string targetBatch = File.Exists(batchFile) ? batchFile : (File.Exists(legacyBatchFile) ? legacyBatchFile : null);

                if (targetBatch != null)
                {
                    try
                    {
                        string json = File.ReadAllText(targetBatch);
                        if (!string.IsNullOrWhiteSpace(json))
                        {
                            string trimmed = json.Trim();
                            if (trimmed.StartsWith("["))
                            {
                                var list = JsonConvert.DeserializeObject<List<SnippetItem>>(trimmed);
                                if (list != null)
                                {
                                    foreach (var item in list)
                                        RegisterSnippet(item);
                                }
                            }
                            else
                            {
                                var single = JsonConvert.DeserializeObject<SnippetItem>(trimmed);
                                RegisterSnippet(single);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore read errors
                    }
                }

                // 2. Scan individual *.json snippet files in the directory
                try
                {
                    var individualFiles = Directory.GetFiles(snippetsDir, "*.json");
                    foreach (var file in individualFiles)
                    {
                        if (string.Equals(Path.GetFileName(file), "Snippets.json", StringComparison.OrdinalIgnoreCase))
                            continue;

                        try
                        {
                            string json = File.ReadAllText(file);
                            if (string.IsNullOrWhiteSpace(json))
                                continue;

                            string trimmed = json.Trim();
                            if (trimmed.StartsWith("["))
                            {
                                var list = JsonConvert.DeserializeObject<List<SnippetItem>>(trimmed);
                                if (list != null)
                                {
                                    foreach (var item in list)
                                        RegisterSnippet(item);
                                }
                            }
                            else
                            {
                                var item = JsonConvert.DeserializeObject<SnippetItem>(trimmed);
                                RegisterSnippet(item);
                            }
                        }
                        catch (Exception)
                        {
                            // Skip invalid file
                        }
                    }
                }
                catch (Exception)
                {
                    // Ignore directory scan errors
                }

                Snippets = loadedList.Values.OrderBy(s => s.Prefix).ToList();
            }
        }

        public void SaveSnippets(List<SnippetItem> snippets)
        {
            lock (_syncLock)
            {
                try
                {
                    string snippetsDir = GetSnippetsDirectory();
                    EnsureDirectoryExists(snippetsDir);

                    Snippets = snippets ?? new List<SnippetItem>();

                    // 1. Save main batch Snippets.json
                    string batchFile = Path.Combine(snippetsDir, "Snippets.json");
                    string json = JsonConvert.SerializeObject(Snippets, Formatting.Indented);
                    File.WriteAllText(batchFile, json);

                    // Also save legacy location for backward compatibility
                    string legacyBatch = Path.Combine(_appDataFolder, "Snippets.json");
                    if (!string.Equals(batchFile, legacyBatch, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.WriteAllText(legacyBatch, json); } catch { }
                    }

                    // 2. Save individual .json files for each snippet
                    foreach (var s in Snippets)
                    {
                        if (string.IsNullOrWhiteSpace(s.Prefix))
                            continue;

                        if (string.IsNullOrWhiteSpace(s.Id))
                            s.Id = Guid.NewGuid().ToString();

                        // Sanitize filename
                        string safeName = string.Join("_", s.Prefix.Split(Path.GetInvalidFileNameChars()));
                        string itemPath = Path.Combine(snippetsDir, safeName + ".json");
                        try
                        {
                            string itemJson = JsonConvert.SerializeObject(s, Formatting.Indented);
                            File.WriteAllText(itemPath, itemJson);
                        }
                        catch { }
                    }
                }
                catch (Exception)
                {
                    // Handle write error
                }
            }

            SnippetsChanged?.Invoke();
        }

        public List<SnippetItem> GetDefaultSnippets()
        {
            return new List<SnippetItem>();
        }
    }
}
