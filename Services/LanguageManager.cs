using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WinOptPro.Services
{
    /// <summary>
    /// Loads UI strings from JSON files in the lang folder.
    /// Each JSON is a simple key -> value object.
    /// Example en.json: { "app_title": "WinOptPro - Windows Optimization", "btn_run_selected": "Run Selected Tweaks" }
    /// </summary>
    public class LanguageManager
    {
        private readonly string _langFolder;
        private readonly Logger _logger;
        public string DefaultLanguage { get; private set; } = "en";

        public LanguageManager(string langFolder, Logger logger)
        {
            _langFolder = langFolder;
            _logger = logger;
            if (!Directory.Exists(_langFolder))
            {
                Directory.CreateDirectory(_langFolder);
                _logger.Log($"Created language folder: {_langFolder}");
            }
        }

        public IEnumerable<string> GetAvailableLanguageCodes()
        {
            foreach (var f in Directory.GetFiles(_langFolder, "*.json"))
            {
                yield return Path.GetFileNameWithoutExtension(f);
            }
        }

        public Dictionary<string, string> LoadLanguage(string code)
        {
            var path = Path.Combine(_langFolder, $"{code}.json");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Language file not found: " + path);
            }

            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
            _logger.Log($"Loaded language: {code}");
            return dict;
        }
    }
}