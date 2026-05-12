using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace TransactionReport;

public class Program
{
    public static void Main(string[] args)
    {
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

        Console.WriteLine($"ORAE Transaction Report");
        Console.WriteLine($"=======================");
        Console.WriteLine($"Scanning: {inputPath}");
        Console.WriteLine($"Pattern:  {filePattern}");
        Console.WriteLine($"Subdirs:  {(searchSubdirs ? "Yes" : "No")}");
        Console.WriteLine();

        var searchOption = searchSubdirs ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var jsonFiles = Directory.GetFiles(inputPath, filePattern, searchOption);

        if (jsonFiles.Length == 0)
        {
            Console.WriteLine("No files found matching the pattern.");
            return;
        }

        // StoreId -> (RegisterId -> count)
        var storeRegisterCounts = new SortedDictionary<string, SortedDictionary<string, int>>();
        int totalTransactions = 0;
        int parseErrors = 0;
        var eventTypes = new SortedDictionary<string, int>();

        foreach (var file in jsonFiles)
        {
            try
            {
                string json = File.ReadAllText(file);
                var retailEvent = JsonSerializer.Deserialize<RetailEventMinimal>(json);

                if (retailEvent?.BusinessContext?.Store?.StoreId == null)
                {
                    parseErrors++;
                    continue;
                }

                totalTransactions++;

                string storeId = retailEvent.BusinessContext.Store.StoreId;
                string registerId = retailEvent.BusinessContext.Workstation?.RegisterId ?? "UNKNOWN";

                if (!storeRegisterCounts.ContainsKey(storeId))
                    storeRegisterCounts[storeId] = new SortedDictionary<string, int>();

                if (!storeRegisterCounts[storeId].ContainsKey(registerId))
                    storeRegisterCounts[storeId][registerId] = 0;

                storeRegisterCounts[storeId][registerId]++;

                string eventType = retailEvent.EventType ?? "UNKNOWN";
                if (!eventTypes.ContainsKey(eventType))
                    eventTypes[eventType] = 0;
                eventTypes[eventType]++;
            }
            catch (JsonException)
            {
                parseErrors++;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error reading {Path.GetFileName(file)}: {ex.Message}");
                parseErrors++;
            }
        }

        // Report Header
        Console.WriteLine($"Files scanned:     {jsonFiles.Length}");
        Console.WriteLine($"Valid transactions: {totalTransactions}");
        if (parseErrors > 0)
            Console.WriteLine($"Parse errors:      {parseErrors}");
        Console.WriteLine();

        // Store Summary
        Console.WriteLine("STORE SUMMARY");
        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"{"Store ID",-15} {"Transaction Count",20}");
        Console.WriteLine(new string('-', 60));

        foreach (var store in storeRegisterCounts)
        {
            int storeTotal = store.Value.Values.Sum();
            Console.WriteLine($"{store.Key,-15} {storeTotal,20:N0}");
        }

        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"{"TOTAL",-15} {totalTransactions,20:N0}");
        Console.WriteLine();

        // Register Detail by Store
        Console.WriteLine("REGISTER DETAIL BY STORE");
        Console.WriteLine(new string('-', 60));

        foreach (var store in storeRegisterCounts)
        {
            int storeTotal = store.Value.Values.Sum();
            Console.WriteLine();
            Console.WriteLine($"Store: {store.Key}  (Total: {storeTotal:N0})");
            Console.WriteLine($"  {"Register",-15} {"Count",20}");
            Console.WriteLine($"  {new string('-', 55)}");

            foreach (var register in store.Value)
            {
                Console.WriteLine($"  {register.Key,-15} {register.Value,20:N0}");
            }
        }

        Console.WriteLine();

        // Event Type Breakdown
        if (eventTypes.Count > 0)
        {
            Console.WriteLine("EVENT TYPE BREAKDOWN");
            Console.WriteLine(new string('-', 60));
            Console.WriteLine($"{"Event Type",-35} {"Count",20}");
            Console.WriteLine(new string('-', 60));

            foreach (var et in eventTypes)
            {
                Console.WriteLine($"{et.Key,-35} {et.Value,20:N0}");
            }

            Console.WriteLine(new string('-', 60));
        }

        Console.WriteLine();
        Console.WriteLine($"Report generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
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
    public DateTime? BusinessDay { get; set; }
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
