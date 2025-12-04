# RonaORAEPubSub

A high-throughput Google Cloud Pub/Sub application for processing retail transaction events. This project receives retail events from Google Cloud Pub/Sub, transforms them into a structured format for database insertion, and publishes the results back to a topic.

## Project Overview

This solution consists of two main components:

1. **PubSubApp** - Main Pub/Sub application that subscribes to retail events, transforms them, and publishes responses
2. **ProcessFiles** - Utility application for processing transaction files

## Architecture

The application follows this workflow:

1. **Subscribe** - Listens to Google Cloud Pub/Sub subscription for retail transaction events
2. **Transform** - Converts `RetailEvent` JSON into `RecordSet` format (OrderRecord + TenderRecord)
3. **Publish** - Sends transformed data back to a Pub/Sub topic for downstream processing

## Project Structure

```
RonaORAEPubSub/
├── PubSubApp/
│   ├── Program.cs                    # Main application entry point
│   ├── Logging.cs                    # Simple file-based logging
│   ├── appsettings.json             # Configuration (ProjectId, TopicId, SubscriptionId)
│   ├── Models/
│   │   └── PubSubConfiguration.cs   # Configuration model
│   ├── Services/
│   │   ├── PubSubPublisher.cs       # Publisher service
│   │   └── PubSubSubscriber.cs      # Subscriber service
│   └── Interfaces/
│       ├── IPubSubPublisher.cs      # Publisher interface
│       └── IPubSubSubscriber.cs     # Subscriber interface
├── ProcessFiles/
│   ├── Program.cs                    # File processing logic
│   └── ProcessFiles.csproj
└── build.bat                         # Build script
```

## Data Models

### Input: RetailEvent
Retail transaction event from Google Cloud Pub/Sub containing:
- Business context (store, workstation, business day)
- Transaction details (type, items, totals)
- Event metadata (eventId, timestamps)

### Output: RecordSet
Structured format for database insertion:
- **OrderRecord (SDISLF)** - Line item details
- **TenderRecord (SDITNF)** - Payment/tender information

## Configuration

Edit `PubSubApp/appsettings.json`:

```json
{
  "PubSubConfiguration": {
    "ProjectId": "your-gcp-project-id",
    "TopicId": "transaction-tree-output",
    "SubscriptionId": "transaction-tree-input"
  }
}
```

### Available Environments

- **Sandbox**: `prj-n-mime-app-f41w`
- **Development**: `prj-d-mime-app-tt3x`

## Prerequisites

- .NET 9.0 SDK (for PubSubApp)
- .NET 8.0 SDK (for ProcessFiles)
- Google Cloud credentials configured
- Access to Google Cloud Pub/Sub topics and subscriptions

## Building

### Using build.bat
```bash
build.bat
```

### Manual build
```bash
cd PubSubApp
dotnet restore
dotnet build
```

## Running

```bash
cd PubSubApp
dotnet run
```

The application will:
1. Initialize publisher and subscriber
2. Start listening for messages
3. Process incoming retail events
4. Transform and publish responses
5. Log all operations to `c:\opt\transactiontree\pubsub\log\pubsub_<projectId>.log`

## Dependencies

- Google.Cloud.PubSub.V1 (3.27.0)
- Microsoft.Extensions.Configuration (9.0.0)
- Microsoft.Extensions.Configuration.Json (9.0.0)
- Microsoft.Extensions.Configuration.Binder (9.0.0)
- Serilog (4.3.0)

## Logging

Logs are written to:
- Console output
- File: `c:\opt\transactiontree\pubsub\log\pubsub_<projectId>.log`

Log entries include:
- Message receipt acknowledgments
- Data transformation details
- Published response IDs
- Error messages and exceptions

## Transaction Type Mapping

| Input Type | SLFTTP/SLFLNT Code |
|------------|-------------------|
| SALE       | 01                |
| RETURN     | 11                |
| VOID       | 87                |

## Version

Current version: v1.0.1 (12/03/25)