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

#### OrderRecord (SDISLF)
Line item details with 70+ fields including:
- Transaction information (type, date, time, register)
- Product details (SKU, quantity, pricing)
- Discounts and promotions
- Tax information (4 tax levels, exemptions)
- Customer information
- Sales person tracking
- Original transaction references

**Required Fields:**
- `SLFTTP` (Transaction Type) - 2 chars
- `SLFLNT` (Line Type) - 2 chars
- `SLFTDT` (Transaction Date) - YYMMDD format (6 chars)
- `SLFTTM` (Transaction Time) - HHMMSS format (6 chars)

#### TenderRecord (SDITNF)
Payment/tender information with 30+ fields including:
- Transaction identification
- Payment method details (fund code)
- Credit/debit card information (masked card number, auth number)
- Gift card tracking
- Employee sale identification
- Customer/member information
- eReceipt email capture
- Payment hash for security
- Poll and create timestamps

**Required Fields:**
- `TNFTDT` (Transaction Date) - YYMMDD format (6 chars)
- `TNFTTM` (Transaction Time) - HHMMSS format (6 chars)

### Schema Validation
All fields include:
- **StringLength validation** for fixed-length fields
- **Range validation** for numeric fields
- **JsonPropertyName** attributes for proper serialization
- **Nullable types** for optional fields

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

### SLFTTP (Transaction Type) Codes
| Input Type | Code | Description      |
|------------|------|------------------|
| SALE       | 01   | Sales            |
| RETURN     | 04   | Return           |
| VOID       | 11   | Void             |
| OPEN       | 87   | Open             |
| CLOSE      | 88   | Close            |

### SLFLNT (Line Type) Codes
| Input Type        | Code | Description         |
|-------------------|------|---------------------|
| SALE              | 01   | Regular Sales       |
| RETURN            | 02   | Regular Trade       |
| AR_PAYMENT        | 43   | AR Payment          |
| LAYAWAY_PAYMENT   | 45   | Layaway Payment     |
| LAYAWAY_SALE      | 50   | Layaway Sale        |
| LAYAWAY_PICKUP    | 51   | Layaway Pickup      |
| LAYAWAY_DELETE    | 52   | Layaway Delete      |
| LAYAWAY_FORFEIT   | 53   | Layaway Forfeit     |
| SPECIAL_ORDER     | 69   | Special Order       |
| NO_SALE           | 98   | No Sale             |
| PAID_OUT          | 90   | Paid Out            |

## Version

Current version: v1.0.1 (12/03/25)