using System;
using System.IO;
using System.Text;

namespace WinOptPro.Services
{
    /// <summary>
    /// Simple file logger that stores trace files under logs\.
    /// Also stores LogFolder property for quick access.
    /// </summary>
    public class Logger
    {
        private readonly string _appRoot;
        public string LogFolder { get; }
        private readonly string _currentLogFile;

        public Logger(string appRoot)
        {
            _appRoot = appRoot;
            LogFolder = Path.Combine(_appRoot, "logs");
            if (!Directory.Exists(LogFolder)) Directory.CreateDirectory(LogFolder);
            _currentLogFile = Path.Combine(LogFolder, $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            Log("Logger initialized.");
        }

        public void Log(string message)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
                File.AppendAllText(_currentLogFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Best effort - do not throw from logger
            }
        }
    }
}