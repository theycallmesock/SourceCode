using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WinOptPro.Services
{
    public class ScriptResult
    {
        public int ExitCode { get; set; }
        public string OutputText { get; set; } = "";
    }

    /// <summary>
    /// Runs local PowerShell scripts by launching powershell.exe and capturing stdout/stderr.
    /// We intentionally avoid running remote scripts or downloading code.
    /// </summary>
    public class ScriptRunner
    {
        private readonly Logger _logger;

        public ScriptRunner(Logger logger)
        {
            _logger = logger;
        }

        public async Task<ScriptResult> RunScriptAsync(string scriptPath)
        {
            if (!File.Exists(scriptPath))
            {
                return new ScriptResult { ExitCode = -1, OutputText = $"Script not found: {scriptPath}" };
            }

            // Build a command to run the script with bypass execution policy and no profile.
            // Important: This runs the script file on disk. Do NOT let this app download scripts automatically.
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var sb = new StringBuilder();
            try
            {
                using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                process.Start();

                // Read output streams asynchronously
                var outTask = process.StandardOutput.ReadToEndAsync();
                var errTask = process.StandardError.ReadToEndAsync();

                await Task.WhenAll(outTask, errTask);

                sb.AppendLine(outTask.Result);
                if (!string.IsNullOrWhiteSpace(errTask.Result))
                {
                    sb.AppendLine("--- STDERR ---");
                    sb.AppendLine(errTask.Result);
                }

                process.WaitForExit();
                return new ScriptResult { ExitCode = process.ExitCode, OutputText = sb.ToString() };
            }
            catch (Exception ex)
            {
                _logger.Log($"Script execution error: {ex}");
                return new ScriptResult { ExitCode = -1, OutputText = "Execution error: " + ex.Message };
            }
        }
    }
}