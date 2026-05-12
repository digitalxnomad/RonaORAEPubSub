using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace TransactionReport;

public static class ReportLogger
{
    private static string _logFilePath = "";
    private static readonly object _lock = new();

    public static string Initialize()
    {
        string exeDir = AppContext.BaseDirectory;
        _logFilePath = Path.Combine(exeDir, $"TransactionReport_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        File.WriteAllText(_logFilePath, "");
        return _logFilePath;
    }

    public static void Log(string message)
    {
        if (string.IsNullOrEmpty(_logFilePath)) return;
        lock (_lock)
        {
            File.AppendAllText(_logFilePath, message + Environment.NewLine);
        }
    }
}

public class Program
{
    static void Output(string message)
    {
        Console.WriteLine(message);
        ReportLogger.Log(message);
    }

    public static void Main(string[] args)
    {
        string logPath = ReportLogger.Initialize();

        string configPath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        if (!File.Exists(configPath))
        {
            string msg = $"Configuration file not found: {configPath}";
            Console.Error.WriteLine(msg);
            ReportLogger.Log($"ERROR: {msg}");
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
            return;
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var config = new ReportConfiguration();
        configuration.GetSection("ReportConfiguration").Bind(config);

        string inputPath = args.Length > 0 ? args[0] : config.InputPath;
        string filePattern = config.FilePattern;
        bool searchSubdirs = config.SearchSubdirectories;

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            Console.Error.WriteLine("Usage: TransactionReport [directory-path]");
            Console.Error.WriteLine("  Or configure InputPath in appsettings.json");
            return;
        }

        if (!Directory.Exists(inputPath))
        {
            Console.Error.WriteLine($"Directory not found: {inputPath}");
            return;
        }

        ReportLogger.Log($"Log file: {logPath}");
        ReportLogger.Log("");

        Output($"Assisted Checkout (ACO) Totals");
        Output($"==============================");
        Output($"Scanning: {inputPath}");
        Output($"Pattern:  {filePattern}");
        Output($"Subdirs:  {(searchSubdirs ? "Yes" : "No")}");
        Output($"Log file: {logPath}");
        Output("");

        var searchOption = searchSubdirs ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var jsonFiles = Directory.GetFiles(inputPath, filePattern, searchOption);

        if (jsonFiles.Length == 0)
        {
            Output("No files found matching the pattern.");
            return;
        }


        // StoreId -> (RegisterId -> count)
        var storeRegisterCounts = new SortedDictionary<string, SortedDictionary<string, int>>();
        int totalTransactions = 0;
        int parseErrors = 0;
        var eventTypes = new SortedDictionary<string, int>();
        var parseErrorFiles = new List<(string file, string reason)>();

        int fileCount = 0;
        foreach (var file in jsonFiles)
        {
            fileCount++;
            string fileName = Path.GetFileName(file);
            Console.WriteLine($"  [{fileCount}/{jsonFiles.Length}] {fileName}");

            try
            {
                string json = File.ReadAllText(file);
                var retailEvent = JsonSerializer.Deserialize<RetailEventMinimal>(json);

                if (retailEvent?.BusinessContext?.Store?.StoreId == null)
                {
                    string reason = "Missing businessContext or storeId";
                    parseErrorFiles.Add((fileName, reason));
                    ReportLogger.Log($"ERROR: {fileName} - {reason}");
                    parseErrors++;
                    continue;
                }

                string registerId = retailEvent.BusinessContext.Workstation?.RegisterId ?? "UNKNOWN";

                // ACO only: skip SCO transactions (register starts with "8")
                if (registerId.StartsWith("8"))
                    continue;

                totalTransactions++;

                string storeId = retailEvent.BusinessContext.Store.StoreId;
                string eventType = retailEvent.EventType ?? "UNKNOWN";

                if (!storeRegisterCounts.ContainsKey(storeId))
                    storeRegisterCounts[storeId] = new SortedDictionary<string, int>();

                if (!storeRegisterCounts[storeId].ContainsKey(registerId))
                    storeRegisterCounts[storeId][registerId] = 0;

                storeRegisterCounts[storeId][registerId]++;

                if (!eventTypes.ContainsKey(eventType))
                    eventTypes[eventType] = 0;
                eventTypes[eventType]++;
            }
            catch (JsonException jex)
            {
                string reason = $"JSON parse error: {jex.Message}";
                parseErrorFiles.Add((fileName, reason));
                ReportLogger.Log($"ERROR: {fileName} - {reason}");
                parseErrors++;
            }
            catch (Exception ex)
            {
                string reason = $"{ex.GetType().Name}: {ex.Message}";
                parseErrorFiles.Add((fileName, reason));
                ReportLogger.Log($"ERROR: {fileName} - {reason}");
                if (ex.StackTrace != null)
                    ReportLogger.Log($"  StackTrace: {ex.StackTrace}");
                parseErrors++;
            }
        }

        Console.WriteLine();

        // Report Header
        Output($"Files scanned:     {jsonFiles.Length}");
        Output($"Valid transactions: {totalTransactions}");
        if (parseErrors > 0)
            Output($"Parse errors:      {parseErrors}");
        Output("");

        // Store Summary
        Output("STORE SUMMARY");
        Output(new string('-', 60));
        Output($"{"Store ID",-15} {"Transaction Count",20}");
        Output(new string('-', 60));

        foreach (var store in storeRegisterCounts)
        {
            int storeTotal = store.Value.Values.Sum();
            Output($"{store.Key,-15} {storeTotal,20:N0}");
        }

        Output(new string('-', 60));
        Output($"{"TOTAL",-15} {totalTransactions,20:N0}");
        Output("");

        // Register Detail by Store
        Output("REGISTER DETAIL BY STORE");
        Output(new string('-', 60));

        foreach (var store in storeRegisterCounts)
        {
            int storeTotal = store.Value.Values.Sum();
            Output("");
            Output($"Store: {store.Key}  (Total: {storeTotal:N0})");
            Output($"  {"Register",-15} {"Count",20}");
            Output($"  {new string('-', 55)}");

            foreach (var register in store.Value)
            {
                Output($"  {register.Key,-15} {register.Value,20:N0}");
            }
        }

        Output("");

        // Event Type Breakdown
        if (eventTypes.Count > 0)
        {
            Output("EVENT TYPE BREAKDOWN");
            Output(new string('-', 60));
            Output($"{"Event Type",-35} {"Count",20}");
            Output(new string('-', 60));

            foreach (var et in eventTypes)
            {
                Output($"{et.Key,-35} {et.Value,20:N0}");
            }

            Output(new string('-', 60));
        }

        if (parseErrorFiles.Count > 0)
        {
            Output("");
            Output("PARSE ERRORS");
            Output(new string('-', 60));
            foreach (var (file, reason) in parseErrorFiles)
            {
                Output($"  {file}");
                Output($"    {reason}");
            }
        }

        Output("");
        Output($"Report generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Output($"Log file:         {logPath}");

        Console.WriteLine();
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}

public class ReportConfiguration
{
    public string InputPath { get; set; } = "";
    public string FilePattern { get; set; } = "*.json";
    public bool SearchSubdirectories { get; set; } = true;
}

public class RetailEventMinimal
{
    [JsonPropertyName("businessContext")]
    public BusinessContextMinimal? BusinessContext { get; set; }

    [JsonPropertyName("eventType")]
    public string? EventType { get; set; }

    [JsonPropertyName("eventSubType")]
    public string? EventSubType { get; set; }

    [JsonPropertyName("eventId")]
    public string? EventId { get; set; }
}

public class BusinessContextMinimal
{
    [JsonPropertyName("store")]
    public StoreMinimal? Store { get; set; }

    [JsonPropertyName("workstation")]
    public WorkstationMinimal? Workstation { get; set; }

    [JsonPropertyName("businessDay")]
    public string? BusinessDay { get; set; }
}

public class StoreMinimal
{
    [JsonPropertyName("storeId")]
    public string? StoreId { get; set; }
}

public class WorkstationMinimal
{
    [JsonPropertyName("registerId")]
    public string? RegisterId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
