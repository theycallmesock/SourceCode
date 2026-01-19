using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WinOptPro.Models;
using WinOptPro.Services;

namespace WinOptPro
{
    public partial class MainWindow : Window
    {
        private readonly string AppRoot;
        private readonly LanguageManager _lang;
        private readonly ScriptRunner _runner;
        private readonly Logger _logger;

        // In-memory grouped view model for the ItemsControl
        private List<ScriptCategoryViewModel> _categories = new();

        public Dictionary<string, string> L { get; private set; } = new();

        public MainWindow()
        {
            InitializeComponent();

            AppRoot = AppDomain.CurrentDomain.BaseDirectory;
            _logger = new Logger(AppRoot);
            _lang = new LanguageManager(Path.Combine(AppRoot, "lang"), _logger);
            _runner = new ScriptRunner(_logger);

            // Load languages for the selector
            foreach (var code in _lang.GetAvailableLanguageCodes())
            {
                LanguageSelector.Items.Add(code);
            }
            LanguageSelector.SelectedItem = _lang.DefaultLanguage;

            LoadLanguageStrings(_lang.DefaultLanguage);

            // Load scripts
            LoadScriptCategories();

            // Populate UI list
            RebuildScriptsListUI();

            _logger.Log("WinOptPro started.");
        }

        private void LoadLanguageStrings(string code)
        {
            try
            {
                L = _lang.LoadLanguage(code);

                // Set UI texts programmatically to avoid invalid XAML indexer syntax
                this.Title = L.GetValueOrDefault("app_title", "WinOptPro - Windows Optimization");
                LanguageLabel.Text = L.GetValueOrDefault("label_language", "Language:");
                GroupTweaks.Header = L.GetValueOrDefault("group_available_tweaks", "Available Tweaks");
                GroupOutput.Header = L.GetValueOrDefault("group_output", "Console / Log Output");
                RunButton.Content = L.GetValueOrDefault("btn_run_selected", "Run Selected Tweaks");
                RunAllButton.Content = L.GetValueOrDefault("btn_run_all", "Run All Tweaks");
                RestoreButton.Content = L.GetValueOrDefault("btn_restore_selected", "Restore Selected (undo)");
                OpenLogsButton.Content = L.GetValueOrDefault("btn_open_logs", "Open Logs Folder");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load language files: " + ex.Message);
            }
        }

        private void LoadScriptCategories()
        {
            _categories.Clear();
            var scriptsRoot = Path.Combine(AppRoot, "scripts");
            if (!Directory.Exists(scriptsRoot))
            {
                _logger.Log($"Scripts folder not found, creating: {scriptsRoot}");
                Directory.CreateDirectory(scriptsRoot);
            }

            foreach (var dir in Directory.GetDirectories(scriptsRoot))
            {
                var catName = new DirectoryInfo(dir).Name;
                var category = new ScriptCategoryViewModel { Category = catName, Scripts = new List<ScriptItem>() };

                foreach (var file in Directory.GetFiles(dir, "*.ps1"))
                {
                    var si = new ScriptItem
                    {
                        Category = catName,
                        DisplayName = Path.GetFileNameWithoutExtension(file),
                        FilePath = file,
                        IsSelected = false,
                        HasUndo = CheckHasUndoScript(file)
                    };
                    category.Scripts.Add(si);
                }

                if (category.Scripts.Any())
                {
                    _categories.Add(category);
                }
            }
        }

        private bool CheckHasUndoScript(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath) ?? "";
            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var alt1 = Path.Combine(dir, baseName + ".undo.ps1");
            var alt2 = Path.Combine(dir, baseName + "-undo.ps1");
            return File.Exists(alt1) || File.Exists(alt2);
        }

        private void RebuildScriptsListUI()
        {
            ScriptsList.Items.Clear();
            foreach (var cat in _categories)
            {
                var stack = new StackPanel { Margin = new Thickness(4), Orientation = Orientation.Vertical };
                var txt = new TextBlock { Text = $"{cat.Category}", FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.DarkSlateGray };
                stack.Children.Add(txt);

                foreach (var s in cat.Scripts)
                {
                    var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
                    var cb = new CheckBox { Width = 18, IsChecked = s.IsSelected };
                    cb.Tag = s; // keep ref for events
                    cb.Checked += (snder, ea) => ((ScriptItem)((CheckBox)snder).Tag).IsSelected = true;
                    cb.Unchecked += (snder, ea) => ((ScriptItem)((CheckBox)snder).Tag).IsSelected = false;

                    var sn = new TextBlock { Text = s.DisplayName, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6,0,0,0) };
                    sp.Children.Add(cb);
                    sp.Children.Add(sn);

                    if (s.HasUndo)
                    {
                        var undo = new TextBlock { Text = $" (undo available)", Foreground = System.Windows.Media.Brushes.Green, Margin = new Thickness(8,0,0,0) };
                        sp.Children.Add(undo);
                    }

                    stack.Children.Add(sp);
                }

                ScriptsList.Items.Add(stack);
            }
        }

        private List<ScriptItem> GetSelectedScripts()
        {
            return _categories.SelectMany(c => c.Scripts).Where(s => s.IsSelected).ToList();
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedScripts();
            if (!selected.Any())
            {
                MessageBox.Show(L.GetValueOrDefault("msg_no_selection", "No tweaks selected."));
                return;
            }

            if (!IsAdministrator())
            {
                var res = MessageBox.Show(L.GetValueOrDefault("msg_not_admin", "App is not running as administrator. Some tweaks may fail. Continue?"),
                    L.GetValueOrDefault("msg_warning", "Warning"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;
            }

            var confirm = MessageBox.Show(L.GetValueOrDefault("msg_confirm_run", "Run selected tweaks? This will execute local PowerShell scripts."), L.GetValueOrDefault("msg_confirm", "Confirm"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            await RunScriptsAsync(selected);
        }

        private async Task RunScriptsAsync(IEnumerable<ScriptItem> scripts)
        {
            RunButton.IsEnabled = false;
            RunAllButton.IsEnabled = false;
            RestoreButton.IsEnabled = false;
            Progress.Value = 0;
            var list = scripts.ToList();
            int total = list.Count;
            int done = 0;

            foreach (var s in list)
            {
                UpdateStatus($"Running: {s.DisplayName}");
                AppendOutput($">>> Running {s.FilePath}");
                _logger.Log($"Starting: {s.FilePath}");
                var result = await _runner.RunScriptAsync(s.FilePath);
                AppendOutput(result.OutputText);
                _logger.Log($"Completed: {s.FilePath} - ExitCode {result.ExitCode}");

                done++;
                Progress.Value = (double)done / total * 100;
            }

            UpdateStatus(L.GetValueOrDefault("msg_done", "Done"));
            RunButton.IsEnabled = true;
            RunAllButton.IsEnabled = true;
            RestoreButton.IsEnabled = true;
        }

        private async void RunAllButton_Click(object sender, RoutedEventArgs e)
        {
            var all = _categories.SelectMany(c => c.Scripts).ToList();
            var confirm = MessageBox.Show(L.GetValueOrDefault("msg_confirm_run_all", "Run ALL available tweaks? This will execute local PowerShell scripts."), L.GetValueOrDefault("msg_confirm", "Confirm"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            await RunScriptsAsync(all);
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = GetSelectedScripts();
            if (!selected.Any())
            {
                MessageBox.Show(L.GetValueOrDefault("msg_no_selection", "No tweaks selected."));
                return;
            }

            var undoScripts = new List<string>();
            foreach (var s in selected)
            {
                var alt1 = Path.Combine(Path.GetDirectoryName(s.FilePath) ?? "", Path.GetFileNameWithoutExtension(s.FilePath) + ".undo.ps1");
                var alt2 = Path.Combine(Path.GetDirectoryName(s.FilePath) ?? "", Path.GetFileNameWithoutExtension(s.FilePath) + "-undo.ps1");
                if (File.Exists(alt1)) undoScripts.Add(alt1);
                else if (File.Exists(alt2)) undoScripts.Add(alt2);
            }

            if (!undoScripts.Any())
            {
                MessageBox.Show(L.GetValueOrDefault("msg_no_undo", "No undo scripts found for selected tweaks."));
                return;
            }

            var confirm = MessageBox.Show(L.GetValueOrDefault("msg_confirm_restore", "Run undo scripts for selected tweaks?"), L.GetValueOrDefault("msg_confirm", "Confirm"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            await RunScriptsAsync(undoScripts.Select(p => new ScriptItem { FilePath = p, DisplayName = Path.GetFileNameWithoutExtension(p) }));
        }

        private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
        {
            var folder = _logger.LogFolder;
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
            {
                FileName = folder,
                UseShellExecute = true,
                Verb = "open"
            });
        }

        private void AppendOutput(string txt)
        {
            Dispatcher.Invoke(() =>
            {
                OutputBox.AppendText(txt + Environment.NewLine);
                OutputBox.ScrollToEnd();
            });
        }

        private void UpdateStatus(string txt)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = txt;
            });
        }

        private bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageSelector.SelectedItem is string code)
            {
                LoadLanguageStrings(code);
            }
        }
    }

    public class ScriptCategoryViewModel
    {
        public string Category { get; set; } = "";
        public List<ScriptItem> Scripts { get; set; } = new();
    }
}