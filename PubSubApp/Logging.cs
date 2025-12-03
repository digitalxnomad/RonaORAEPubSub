using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PubSubApp
{
    public static class SimpleLogger
    {
        private static readonly object _lock = new object();
        private static string _logFilePath = "c:\\opt\\transactiontree\\pubsub\\log\\pubsub.log";

        public static void SetLogPath(string path,string projectId)
        {
            _logFilePath = Path.GetDirectoryName(_logFilePath) + "\\" + Path.GetFileName(_logFilePath) + "_" + projectId + ".log";
        }

        public static void Log(string message)
        {
            lock (_lock)
            {
                try
                {
                    string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}";
                    File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
                    Console.WriteLine(logMessage);  // Also write to console
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
    }
}
