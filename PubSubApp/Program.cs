 using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using PubSubApp;
using PubSubApp.Models;
using PubSubApp.Validation;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using System.Net.Http;

public partial class Program
{
    static string Version = $"PubSubApp v{typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

    public static async Task Main(string[] args)
    {
        // Load configuration from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        var pubSubConfig = new PubSubConfiguration();
        configuration.GetSection("PubSubConfiguration").Bind(pubSubConfig);

        // Set console window title with project ID and version
        Console.Title = $"{pubSubConfig.ProjectId} - {Version}";

        // Check for test mode
        if (args.Length > 0 && args[0] == "--test")
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: dotnet run --test <json-file-path>");
                return;
            }

            string jsonPath = args[1];
            await TestJsonFile(jsonPath);
            return;
        }




        string projectId = pubSubConfig.ProjectId;
        string topicId = pubSubConfig.TopicId;
        string subscriptionId = pubSubConfig.SubscriptionId;

        var topicName = TopicName.FromProjectTopic(projectId, topicId);
        var subscriptionName = SubscriptionName.FromProjectSubscription(projectId, subscriptionId);

        // Create publisher with immediate publish (no batching delay)
        var publisherBuilder = new PublisherClientBuilder
        {
            TopicName = topicName,
            Settings = new PublisherClient.Settings
            {
                BatchingSettings = new Google.Api.Gax.BatchingSettings(
                    elementCountThreshold: 1,
                    byteCountThreshold: null,
                    delayThreshold: TimeSpan.FromMilliseconds(10)
                )
            }
        };
        var publisher = await publisherBuilder.BuildAsync();

        SimpleLogger.SetLogPath(pubSubConfig.LogPath, projectId);

        SimpleLogger.LogInfo(Version);
        SimpleLogger.LogInfo("ProjectId: " + projectId);
        SimpleLogger.LogInfo("TopicId: " + topicId);
        SimpleLogger.LogInfo("✓ Publisher initialized");

        bool debugLog = pubSubConfig.EnableDebugLogging;
        if (debugLog)
        {
            SimpleLogger.LogDebug("Debug logging is ENABLED");
        }

        // Graceful shutdown: Ctrl+C, SIGTERM, or process exit will trigger clean shutdown
        var shutdownCts = new CancellationTokenSource();
        SubscriberClient? activeSubscriber = null;

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // Prevent immediate process termination
            SimpleLogger.LogInfo("⏹ Shutdown requested (Ctrl+C). Finishing current message...");
            shutdownCts.Cancel();
        };

        AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
        {
            if (!shutdownCts.IsCancellationRequested)
            {
                SimpleLogger.LogInfo("⏹ Shutdown requested (SIGTERM/process exit). Finishing current message...");
                shutdownCts.Cancel();
            }
        };

        // Resilient subscriber loop - recreates the subscriber if it stops after idle/disconnect
        while (!shutdownCts.IsCancellationRequested)
        {
            SubscriberClient? subscriber = null;
            try
            {
                // Create a fresh subscriber with gRPC keepalive to prevent idle disconnects
                var keepAlivePingDelay = TimeSpan.FromSeconds(60);
                var keepAlivePingTimeout = TimeSpan.FromSeconds(30);
                var ackExtensionWindow = TimeSpan.FromSeconds(30);

                if (debugLog) { SimpleLogger.LogDebug("Building SubscriberClient..."); }

                var subscriberBuilder = new SubscriberClientBuilder
                {
                    SubscriptionName = subscriptionName,
                    Settings = new SubscriberClient.Settings
                    {
                        AckExtensionWindow = ackExtensionWindow,
                        FlowControlSettings = new Google.Api.Gax.FlowControlSettings(
                            maxOutstandingElementCount: 100,
                            maxOutstandingByteCount: 10_000_000 // 10MB
                        )
                    },
                    ClientCount = 1,
                    GrpcAdapter = Google.Api.Gax.Grpc.GrpcNetClientAdapter.Default
                        .WithAdditionalOptions(options =>
                        {
                            options.HttpHandler = new SocketsHttpHandler
                            {
                                KeepAlivePingDelay = keepAlivePingDelay,
                                KeepAlivePingTimeout = keepAlivePingTimeout,
                                EnableMultipleHttp2Connections = true
                            };
                        })
                };
                subscriber = await subscriberBuilder.BuildAsync();
                activeSubscriber = subscriber; // Track for graceful shutdown

                if (debugLog) { SimpleLogger.LogDebug("SubscriberClient built successfully"); }

                SimpleLogger.LogInfo("✓ Subscriber started. Listening for messages...");
                SimpleLogger.LogInfo($"  Heartbeat: PingDelay={keepAlivePingDelay.TotalSeconds}s, PingTimeout={keepAlivePingTimeout.TotalSeconds}s, AckExtension={ackExtensionWindow.TotalSeconds}s");
                SimpleLogger.LogInfo($"  FlowControl: MaxElements=100, MaxBytes=10MB, ClientCount=1");
                SimpleLogger.LogInfo($"  IdleWatchdog: {(pubSubConfig.IdleTimeoutMinutes > 0 ? pubSubConfig.IdleTimeoutMinutes : 30)} minutes (reconnects if no messages)");

                if (debugLog) { SimpleLogger.LogDebug("Calling subscriber.StartAsync - waiting for messages..."); }

                // Idle watchdog: if no messages received within this window, force reconnect
                // This prevents silent gRPC stream death where StartAsync never completes
                var idleTimeoutMinutes = pubSubConfig.IdleTimeoutMinutes > 0 ? pubSubConfig.IdleTimeoutMinutes : 30;
                DateTime lastMessageTime = DateTime.UtcNow;
                // Link idle watchdog to shutdown token so both can stop the subscriber
                var idleWatchdogCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCts.Token);

                // Background task that monitors for idle timeout or shutdown
                var watchdogTask = Task.Run(async () =>
                {
                    while (!idleWatchdogCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromMinutes(1), idleWatchdogCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { break; }

                        if (shutdownCts.IsCancellationRequested)
                        {
                            // Graceful shutdown requested — stop the subscriber
                            try { await subscriber.StopAsync(CancellationToken.None); } catch { }
                            break;
                        }

                        var idleMinutes = (DateTime.UtcNow - lastMessageTime).TotalMinutes;
                        if (idleMinutes >= idleTimeoutMinutes)
                        {
                            SimpleLogger.LogWarning($"⚠ Idle watchdog: No messages for {idleMinutes:F0} minutes. Forcing reconnect...");
                            try { await subscriber.StopAsync(CancellationToken.None); } catch { }
                            break;
                        }
                    }
                }, idleWatchdogCts.Token);

                // Start subscribing - this Task completes when the subscriber stops
                await subscriber.StartAsync(async (message, cancellationToken) =>
                {
                    try
                    {
                        lastMessageTime = DateTime.UtcNow; // Reset idle watchdog on each message

                        if (debugLog) { SimpleLogger.LogDebug($">>> Message callback invoked for MessageId={message.MessageId}"); }

                        // Process the message
                        string data = message.Data.ToStringUtf8();
                        SimpleLogger.LogInfo($"✓ Received: {message.MessageId}");
                        SimpleLogger.LogInfo($"Data: {data.Substring(0, Math.Min(200, data.Length))}...");

                        // Save incoming message to file
                        if (!string.IsNullOrEmpty(pubSubConfig.InputSavePath))
                        {
                            try
                            {
                                Directory.CreateDirectory(pubSubConfig.InputSavePath);
                                string inputFilePath = Path.Combine(pubSubConfig.InputSavePath,
                                    $"Input_{DateTime.Now:yyyyMMddHHmmss}_{message.MessageId}.json");
                                File.WriteAllText(inputFilePath, data);
                                SimpleLogger.LogInfo($"✓ Saved input to: {inputFilePath} ({data.Length} bytes)");
                            }
                            catch (Exception ex)
                            {
                                SimpleLogger.LogError($"✗ Failed to save input file: {ex.Message}", ex);
                                // Don't throw - continue with processing
                            }
                        }

                        if (debugLog) { SimpleLogger.LogDebug($"Parsing JSON ({data.Length} bytes)..."); }

                        var mapper = new RetailEventMapper();

                        RetailEvent retailEvent;
                        try
                        {
                            retailEvent = mapper.ReadRecordSetFromString(data);
                        }
                        catch (JsonException jsonEx)
                        {
                            // Invalid JSON - log and ACK to prevent redelivery
                            SimpleLogger.LogError($"JSON parsing error: {jsonEx.Message}");
                            SimpleLogger.LogInfo($"✓ Message {message.MessageId} acknowledged (invalid JSON, will not be redelivered)");
                            return SubscriberClient.Reply.Ack; // ACK invalid JSON to prevent redelivery
                        }

                        if (debugLog) { SimpleLogger.LogDebug("JSON parsed successfully"); }

                        // Validate ORAE v2.0.0 compliance (if not disabled)
                        if (!pubSubConfig.DisableOraeValidation)
                        {
                            var validationErrors = OraeValidator.ValidateOraeCompliance(retailEvent);
                            if (validationErrors.Count > 0)
                            {
                                string errorMessage = $"ORAE validation failed with {validationErrors.Count} error(s):\n" +
                                                    string.Join("\n", validationErrors);
                                SimpleLogger.LogError(errorMessage);
                                SimpleLogger.LogInfo($"✓ Message {message.MessageId} acknowledged (ORAE validation failed, will not be redelivered)");
                                return SubscriberClient.Reply.Ack; // ACK to prevent redelivery of invalid ORAE data
                            }
                            SimpleLogger.LogInfo("✓ ORAE v2.0.0 validation passed");
                        }
                        else
                        {
                            SimpleLogger.LogWarning("⚠ ORAE validation skipped (disabled in configuration)");
                        }

                        // Map to RecordSet
                        if (debugLog) { SimpleLogger.LogDebug("Mapping RetailEvent to RecordSet..."); }
                        RecordSet recordSet = mapper.MapRetailEventToRecordSet(retailEvent);
                        if (debugLog) { SimpleLogger.LogDebug("RecordSet mapping complete"); }

                        // Validate output RecordSet
                        var outputErrors = RecordSetValidator.ValidateRecordSetOutput(recordSet);
                        if (outputErrors.Count > 0)
                        {
                            string errorMessage = $"RecordSet validation failed with {outputErrors.Count} error(s):\n" +
                                                string.Join("\n", outputErrors);
                            SimpleLogger.LogError(errorMessage);
                            // ACK the message to prevent infinite redelivery, but skip publishing
                            SimpleLogger.LogWarning($"⚠ ACK-ing invalid message {message.MessageId} to prevent redelivery");
                            return SubscriberClient.Reply.Ack;
                        }
                        SimpleLogger.LogInfo("✓ RecordSet output validation passed");

                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                        };

                        string jsonString = JsonSerializer.Serialize(recordSet, options);

                        // Log summary instead of full JSON (can be very large)
                        int orderCount = recordSet.OrderRecords?.Count ?? 0;
                        int tenderCount = recordSet.TenderRecords?.Count ?? 0;
                        SimpleLogger.LogInfo($"✓ Mapped to RecordSet: {orderCount} OrderRecords, {tenderCount} TenderRecords");

                        // Save output RecordSet to file
                        if (!string.IsNullOrEmpty(pubSubConfig.OutputSavePath))
                        {
                            try
                            {
                                Directory.CreateDirectory(pubSubConfig.OutputSavePath);
                                string outputFilePath = Path.Combine(pubSubConfig.OutputSavePath,
                                    $"RecordSet_{DateTime.Now:yyyyMMddHHmmss}_{message.MessageId}.json");
                                File.WriteAllText(outputFilePath, jsonString);
                                SimpleLogger.LogInfo($"✓ Saved output to: {outputFilePath} ({jsonString.Length} bytes)");
                            }
                            catch (Exception ex)
                            {
                                SimpleLogger.LogError($"✗ Failed to save output file: {ex.Message}", ex);
                                // Don't throw - continue with publishing
                            }
                        }
                        else
                        {
                            SimpleLogger.LogWarning("⚠ OutputSavePath not configured - output file not saved");
                        }

                        // Publish response with attributes
                        if (debugLog) { SimpleLogger.LogDebug($"Publishing response ({jsonString.Length} bytes)..."); }
                        var responseMessage = new PubsubMessage
                        {
                            Data = ByteString.CopyFromUtf8(jsonString)
                        };

                        // Forward all original message attributes
                        foreach (var attr in message.Attributes)
                        {
                            responseMessage.Attributes[attr.Key] = attr.Value;
                        }

                        // Add response-specific attributes
                        responseMessage.Attributes["responseFor"] = message.MessageId;
                        responseMessage.Attributes["transformedAt"] = DateTime.UtcNow.ToString("O");

                        if (debugLog) { SimpleLogger.LogDebug("Calling publisher.PublishAsync..."); }
                        string publishedId = await publisher.PublishAsync(responseMessage);
                        if (debugLog) { SimpleLogger.LogDebug($"PublishAsync returned, MessageId={publishedId}"); }
                        SimpleLogger.LogInfo($"✓ Published response: {publishedId} with {responseMessage.Attributes.Count} attributes");

                        if (debugLog) { SimpleLogger.LogDebug($"Returning Ack for MessageId={message.MessageId}"); }
                        return SubscriberClient.Reply.Ack;
                    }
                    catch (Exception ex)
                    {
                        SimpleLogger.LogError($"✗ Error: {ex.Message}");
                        if (debugLog) { SimpleLogger.LogDebug($"Returning Nack for MessageId={message.MessageId} due to exception: {ex.Message}"); }
                        return SubscriberClient.Reply.Nack;
                    }
                });

                // Cancel the idle watchdog since subscriber has stopped
                idleWatchdogCts.Cancel();

                if (debugLog) { SimpleLogger.LogDebug("subscriber.StartAsync has returned (subscriber stopped)"); }

                // If StartAsync completes, the subscriber has stopped
                if (shutdownCts.IsCancellationRequested)
                {
                    SimpleLogger.LogInfo("⏹ Subscriber stopped for shutdown.");
                }
                else
                {
                    SimpleLogger.LogWarning("⚠ Subscriber stopped. Reconnecting...");
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.LogError($"✗ Subscriber error: {ex.Message}. Reconnecting in 5 seconds...", ex);
                try { await Task.Delay(TimeSpan.FromSeconds(5), shutdownCts.Token); }
                catch (OperationCanceledException) { } // Shutdown requested during delay
            }
            finally
            {
                // Clean up the subscriber before creating a new one or exiting
                activeSubscriber = null;
                if (subscriber != null)
                {
                    try
                    {
                        await subscriber.StopAsync(CancellationToken.None);
                    }
                    catch { }
                }
            }
        }

        // Graceful shutdown: flush any pending publishes and release resources
        SimpleLogger.LogInfo("⏹ Shutting down publisher...");
        try
        {
            await publisher.ShutdownAsync(TimeSpan.FromSeconds(10));
            SimpleLogger.LogInfo("✓ Publisher shut down cleanly.");
        }
        catch (Exception ex)
        {
            SimpleLogger.LogError("Publisher shutdown error", ex);
        }

        SimpleLogger.LogInfo($"⏹ {Version} stopped.");
    }

    static Task TestJsonFile(string jsonPath)
    {
        try
        {
            // Load configuration for logging
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var pubSubConfig = new PubSubConfiguration();
            configuration.GetSection("PubSubConfiguration").Bind(pubSubConfig);

            // Initialize logger
            SimpleLogger.SetLogPath(pubSubConfig.LogPath, pubSubConfig.ProjectId);

            SimpleLogger.LogInfo("=== Test Mode ===");
            SimpleLogger.LogInfo($"Reading: {jsonPath}");

            if (!File.Exists(jsonPath))
            {
                SimpleLogger.LogError($"✗ File not found: {jsonPath}");
                return Task.CompletedTask;
            }

            string jsonContent = File.ReadAllText(jsonPath);
            SimpleLogger.LogInfo($"✓ File read successfully ({jsonContent.Length} bytes)");

            // Save input file to InputSavePath
            if (!string.IsNullOrEmpty(pubSubConfig.InputSavePath))
            {
                try
                {
                    Directory.CreateDirectory(pubSubConfig.InputSavePath);
                    string inputSavePath = Path.Combine(pubSubConfig.InputSavePath,
                        $"Input_{DateTime.Now:yyyyMMddHHmmss}_test.json");
                    File.WriteAllText(inputSavePath, jsonContent);
                    SimpleLogger.LogInfo($"✓ Saved input to: {inputSavePath} ({jsonContent.Length} bytes)");
                }
                catch (Exception ex)
                {
                    SimpleLogger.LogError($"✗ Failed to save input file: {ex.Message}", ex);
                }
            }

            // Display input JSON to console only (too large for log file)
            Console.WriteLine("=== Input JSON ===");
            Console.WriteLine(jsonContent);
            Console.WriteLine();

            var mapper = new RetailEventMapper();

            SimpleLogger.LogInfo("Parsing RetailEvent...");
            RetailEvent retailEvent = mapper.ReadRecordSetFromString(jsonContent);
            SimpleLogger.LogInfo("✓ RetailEvent parsed successfully");

            // Validate ORAE v2.0.0 compliance (if not disabled)
            if (!pubSubConfig.DisableOraeValidation)
            {
                SimpleLogger.LogInfo("Validating ORAE v2.0.0 compliance...");
                var validationErrors = OraeValidator.ValidateOraeCompliance(retailEvent);
                if (validationErrors.Count > 0)
                {
                    string errorMessage = $"✗ ORAE validation failed with {validationErrors.Count} error(s):\n" +
                                        string.Join("\n  - ", validationErrors.Prepend(""));
                    SimpleLogger.LogError(errorMessage);
                    return Task.CompletedTask;
                }
                SimpleLogger.LogInfo("✓ ORAE v2.0.0 validation passed");
            }
            else
            {
                SimpleLogger.LogWarning("⚠ ORAE validation skipped (disabled in configuration)");
            }

            SimpleLogger.LogInfo("Mapping to RecordSet...");
            RecordSet recordSet = mapper.MapRetailEventToRecordSet(retailEvent);
            SimpleLogger.LogInfo("✓ RecordSet mapped successfully");

            // Validate output RecordSet
            SimpleLogger.LogInfo("Validating RecordSet output...");
            var outputErrors = RecordSetValidator.ValidateRecordSetOutput(recordSet);

            if (outputErrors.Count > 0)
            {
                string errorMessage = $"✗ RecordSet validation failed with {outputErrors.Count} error(s):\n" +
                                    string.Join("\n  - ", outputErrors.Prepend(""));
                SimpleLogger.LogError(errorMessage);
                return Task.CompletedTask;
            }
            SimpleLogger.LogInfo("✓ RecordSet output validation passed");

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            string jsonOutput = JsonSerializer.Serialize(recordSet, options);

            // Log summary instead of full JSON (can be very large)
            int orderCount = recordSet.OrderRecords?.Count ?? 0;
            int tenderCount = recordSet.TenderRecords?.Count ?? 0;
            SimpleLogger.LogInfo($"Output JSON generated: {orderCount} OrderRecords, {tenderCount} TenderRecords ({jsonOutput.Length} bytes)");

            // Display output JSON to console only (too large for log file)
            Console.WriteLine("=== Output JSON ===");
            Console.WriteLine(jsonOutput);
            Console.WriteLine();

            SimpleLogger.LogInfo("=== Validation ===");
            ValidateRecordSet(recordSet);

            // Write output to file using configured path if available
            if (!string.IsNullOrEmpty(pubSubConfig.OutputSavePath))
            {
                try
                {
                    Directory.CreateDirectory(pubSubConfig.OutputSavePath);
                    string outputPath = Path.Combine(pubSubConfig.OutputSavePath,
                        $"RecordSet_{DateTime.Now:yyyyMMddHHmmss}_test.json");
                    File.WriteAllText(outputPath, jsonOutput);
                    SimpleLogger.LogInfo($"✓ Output written to: {outputPath} ({jsonOutput.Length} bytes)");
                }
                catch (Exception ex)
                {
                    SimpleLogger.LogError($"✗ Failed to save output file: {ex.Message}", ex);
                }
            }
            else
            {
                try
                {
                    // Fallback to local directory if not configured
                    string outputPath = Path.Combine(Path.GetDirectoryName(jsonPath) ?? "", "output_" + Path.GetFileName(jsonPath));
                    File.WriteAllText(outputPath, jsonOutput);
                    SimpleLogger.LogInfo($"✓ Output written to: {outputPath} ({jsonOutput.Length} bytes)");
                }
                catch (Exception ex)
                {
                    SimpleLogger.LogError($"✗ Failed to save output file: {ex.Message}", ex);
                }
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.LogError($"✗ Error: {ex.Message}");
            SimpleLogger.LogError($"Stack trace: {ex.StackTrace}");
        }

        return Task.CompletedTask;
    }

    static void ValidateRecordSet(RecordSet recordSet)
    {
        if (recordSet == null)
        {
            SimpleLogger.LogError("✗ RecordSet is null");
            return;
        }

        // Validate OrderRecords
        if (recordSet.OrderRecords != null && recordSet.OrderRecords.Count > 0)
        {
            SimpleLogger.LogInfo($"✓ Found {recordSet.OrderRecords.Count} OrderRecord(s) in RIMSLF");

            for (int i = 0; i < recordSet.OrderRecords.Count; i++)
            {
                var orderRecord = recordSet.OrderRecords[i];
                // Tax records have LineType starting with "X" (XH, XI, XR, etc.)
                // EPP records have TransType = "21"
                bool isTaxRecord = !string.IsNullOrEmpty(orderRecord.LineType) && orderRecord.LineType.StartsWith("X");
                bool isEppRecord = orderRecord.TransType == "21";
                string recordLabel = isTaxRecord ? "TaxRecord" : isEppRecord ? "EPP" : "OrderRecord";
                SimpleLogger.LogInfo($"  {recordLabel} #{i + 1}:");

                // Required fields
                if (string.IsNullOrEmpty(orderRecord.TransType))
                    SimpleLogger.LogError($"    ✗ TransType (SLFTTP) is required");
                else
                    SimpleLogger.LogInfo($"    ✓ TransType: {orderRecord.TransType}");

                if (string.IsNullOrEmpty(orderRecord.LineType))
                    SimpleLogger.LogError($"    ✗ LineType (SLFLNT) is required");
                else
                    SimpleLogger.LogInfo($"    ✓ LineType: {orderRecord.LineType}");

                if (string.IsNullOrEmpty(orderRecord.TransDate))
                    SimpleLogger.LogError($"    ✗ TransDate (SLFTDT) is required");
                else
                    SimpleLogger.LogInfo($"    ✓ TransDate: {orderRecord.TransDate} (length: {orderRecord.TransDate.Length})");

                if (string.IsNullOrEmpty(orderRecord.TransTime))
                    SimpleLogger.LogError($"    ✗ TransTime (SLFTTM) is required");
                else
                    SimpleLogger.LogInfo($"    ✓ TransTime: {orderRecord.TransTime} (length: {orderRecord.TransTime.Length})");

                if (!string.IsNullOrEmpty(orderRecord.SKUNumber))
                    SimpleLogger.LogInfo($"    ✓ SKU: {orderRecord.SKUNumber}");

                if (!string.IsNullOrEmpty(orderRecord.Quantity))
                    SimpleLogger.LogInfo($"    ✓ Quantity: {orderRecord.Quantity} (sign: '{orderRecord.QuantityNegativeSign}')");

                if (!string.IsNullOrEmpty(orderRecord.ExtendedValue))
                    SimpleLogger.LogInfo($"    ✓ ExtendedValue: {orderRecord.ExtendedValue} (sign: '{orderRecord.ExtendedValueNegativeSign}')");

                if (!string.IsNullOrEmpty(orderRecord.TransSeq))
                    SimpleLogger.LogInfo($"    ✓ TransSeq: {orderRecord.TransSeq}");

                if (!string.IsNullOrEmpty(orderRecord.SourceLineId))
                    SimpleLogger.LogInfo($"    ✓ LineId: {orderRecord.SourceLineId}");

                if (!string.IsNullOrEmpty(orderRecord.SourceParentLineId))
                    SimpleLogger.LogInfo($"    ✓ ParentLineId: {orderRecord.SourceParentLineId}");
            }
        }
        else
        {
            SimpleLogger.LogWarning("⚠ No OrderRecords found in RIMSLF");
        }

        // Validate TenderRecords
        if (recordSet.TenderRecords != null && recordSet.TenderRecords.Count > 0)
        {
            SimpleLogger.LogInfo($"✓ Found {recordSet.TenderRecords.Count} TenderRecord(s) in RIMTNF");

            for (int i = 0; i < recordSet.TenderRecords.Count; i++)
            {
                var tenderRecord = recordSet.TenderRecords[i];
                SimpleLogger.LogInfo($"  TenderRecord #{i + 1}:");

                // Required fields
                if (string.IsNullOrEmpty(tenderRecord.TransactionDate))
                    SimpleLogger.LogError($"    ✗ TransactionDate (TNFTDT) is required");
                else
                    SimpleLogger.LogInfo($"    ✓ TransactionDate: {tenderRecord.TransactionDate} (length: {tenderRecord.TransactionDate.Length})");

                if (string.IsNullOrEmpty(tenderRecord.TransactionTime))
                    SimpleLogger.LogError($"    ✗ TransactionTime (TNFTTM) is required");
                else
                    SimpleLogger.LogInfo($"    ✓ TransactionTime: {tenderRecord.TransactionTime} (length: {tenderRecord.TransactionTime.Length})");

                if (!string.IsNullOrEmpty(tenderRecord.FundCode))
                    SimpleLogger.LogInfo($"    ✓ FundCode: {tenderRecord.FundCode}");

                if (!string.IsNullOrEmpty(tenderRecord.Amount))
                    SimpleLogger.LogInfo($"    ✓ Amount: {tenderRecord.Amount} (sign: '{tenderRecord.AmountNegativeSign}')");

                if (!string.IsNullOrEmpty(tenderRecord.TransactionSeq))
                    SimpleLogger.LogInfo($"    ✓ TransactionSeq: {tenderRecord.TransactionSeq}");
            }
        }
        else
        {
            SimpleLogger.LogWarning("⚠ No TenderRecords found in RIMTNF");
        }
    }
}
