using System;

namespace PubSubApp
{
    public static class SimpleLogger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath = "c:\\opt\\transactiontree\\pubsub\\log\\pubsub.log";

        public static void SetLogPath(string path, string projectId)
        {
            string? directory = Path.GetDirectoryName(_logFilePath);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(_logFilePath);
            string ext = Path.GetExtension(_logFilePath);
            _logFilePath = Path.Combine(directory ?? ".", $"{fileNameWithoutExt}_{projectId}{ext}");
        }

        public static void Log(string message)
        {
            lock (_lock)
            {
                try
                {
                    string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}";
                    Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath) ?? ".");
                    File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
                    Console.WriteLine(logMessage);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Logging error: {ex.Message}");
                }
            }
        }

        public static void LogError(string message, Exception? ex = null)
        {
            string errorMsg = ex != null ? $"ERROR: {message} - {ex.Message}" : $"ERROR: {message}";
            Log(errorMsg);
        }

        public static void LogInfo(string message)
        {
            Log($"INFO: {message}");
        }

        public static void LogDebug(string message)
        {
            Log($"DEBUG: {message}");
        }

        public static void LogWarning(string message)
        {
            Log($"WARNING: {message}");
        }
    }
}
