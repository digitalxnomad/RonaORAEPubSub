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

**Event Metadata:**
- schemaVersion (e.g., "2.0.0")
- messageType (e.g., "RetailEvent")
- eventType (e.g., "ORIGINAL")
- eventCategory (e.g., "TRANSACTION")
- eventSubType (e.g., "transactionSale")
- eventId (unique transaction identifier)
- occurredAt (transaction timestamp)
- ingestedAt (ingestion timestamp)

**Business Context:**
- businessDay (business date)
- store (storeId, timeZone, currency, taxArea)
- workstation (registerId, type, sequenceNumber)
- channel (e.g., "STORE")
- fulfillment (e.g., "CARRYOUT")

**Transaction Details:**
- transactionType (SALE, RETURN, VOID, etc.)
- items[] (line items with SKU, quantity, pricing)
  - Quantity supports random weight items (randomWeight, tareWeight)
  - Pricing includes unitPrice and extendedPrice
  - **Taxes[] (item-level tax details)** ✨ NEW
    - taxType, taxCategory (PST/GST/HST/QST/Municipal)
    - taxRate (decimal percentage)
    - taxAmount (currency amount)
    - taxAuthority (jurisdiction code)
    - taxCode (rate code)
    - taxExempt (boolean flag)
    - exemptReason (exemption description)
- tenders[] (payment methods: CASH, CARD, etc.)
  - tenderId, method, amount
- totals (gross, discounts, tax, net, tendered, changeDue)

### Output: RecordSet
Structured format for database insertion:

#### OrderRecord (RIMSLF)
Line item details with 70+ fields including:
- Transaction information (type, date, time, register)
- Product details (SKU, quantity, pricing)
- Discounts and promotions
- **Tax information (4 tax levels, exemptions)** ✨ ENHANCED
  - Province-aware tax flag logic (Ontario: SLFTX2=N, SLFTX3=Y for HST)
  - Tax authority codes (HON for 13% HST, HON1 for 5% GST)
  - Item-level tax parsing with fallback to transaction-level
  - Tax exemption tracking (TaxExemptId1, TaxExemptId2)
- Customer information
- **EPP (Extended Protection Plan) tracking** ✨ NEW
  - SLFZIP field includes EPP eligibility digit (0-9)
  - EPP detection by SKU/description patterns
  - Coverage tracking (single item vs multi-item)
- Sales person tracking
- Original transaction references

**Required Fields:**
- `SLFTTP` (Transaction Type) - 2 chars
- `SLFLNT` (Line Type) - 2 chars (includes **"XH" for tax line items** ✨)
- `SLFTDT` (Transaction Date) - YYMMDD format (6 chars)
- `SLFTTM` (Transaction Time) - HHMMSS format (6 chars)

#### TenderRecord (RIMTNF)
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
- **StringLength validation** for fixed-length fields ✨ ENHANCED
  - Blank/empty fields can be zero length or properly padded
  - Non-blank fields must match exact length (MinimumLength == MaxLength)
  - Reflection-based validation inspects all properties with StringLength attributes
  - Detailed error messages with field names and actual vs expected lengths
- **Range validation** for numeric fields
- **JsonPropertyName** attributes for proper serialization
- **Nullable types** for optional fields

## Tax Logic ✨ NEW

### Item-Level Tax Parsing
The application now supports comprehensive item-level tax parsing from ORAE v2.0.0:

- **TaxDetail Class**: Captures granular tax information per line item
  - `taxType`, `taxCategory` (PST/GST/HST/QST/Municipal)
  - `taxRate` (decimal percentage, e.g., 0.13 for 13%)
  - `taxAmount` (currency amount)
  - `taxAuthority` (jurisdiction code)
  - `taxCode` (rate code)
  - `taxExempt` (boolean flag)
  - `exemptReason` (exemption description)

### Province-Aware Tax Logic

**Ontario-Specific Rules:**
- **Actual Items**: `SLFTX2=N`, `SLFTX3=Y` (HST)
- **Tax Authority**: Automatically determined based on rate
  - 13% → "HON" (Ontario HST)
  - 5% → "HON1" (Federal GST)

**Other Provinces:**
- **PST**: `SLFTX1=Y`
- **GST**: `SLFTX2=Y`
- **HST**: `SLFTX1=Y`, `SLFTX2=Y` (combined)
- **QST**: `SLFTX3=Y` (Quebec)
- **Municipal**: `SLFTX4=Y`

### Tax Line Items
For every transaction with taxes, a separate tax line item is created:

- **LineType**: "XH" (tax record)
- **Tax Flags**: All set to "N" (`SLFTX1-4=N`)
- **Tax Authority**: HON or HON1 based on rate
- **ExtendedValue**: Contains the tax amount
- **Sequence**: Gets its own sequence number after item lines

**Example Transaction Structure:**
```
OrderRecords:
  [1] Item 1 - SLFLNT="01", SLFTX2="N", SLFTX3="Y", SLFZIP="         0"
  [2] Item 2 - SLFLNT="01", SLFTX2="N", SLFTX3="Y", SLFZIP="         0"
  [3] Tax    - SLFLNT="XH", SLFTX2="N", SLFTX3="N", SLFACD="HON"
```

## EPP (Extended Protection Plan) ✨ NEW

### EPP Detection
The application automatically detects EPP items based on:
- **SKU patterns**: Starts with "EPP", "WTY", "WARR"
- **Description keywords**: "PROTECTION PLAN", "WARRANTY", "EXTENDED PROTECTION"

### EPP Eligibility Tracking (SLFZIP)
The last digit of the `SLFZIP` field indicates EPP eligibility:

| Digit | Meaning |
|-------|---------|
| 0 | EPP not eligible for this SKU |
| 1 | EPP covering single item is included |
| 2 | EPP covering multiple items is included |
| 3 | EPP requested but denied |
| 9 | This line item IS the EPP |

**Format**: `SLFZIP = "         X"` (9 spaces + digit)

### EPP Business Logic
- `IsEPPItem()`: Detects EPP items by SKU/description
- `IsSKUEligibleForEPP()`: Checks if SKU can have EPP coverage
- `DetermineEPPCoveredItemCount()`: Counts items covered by EPP
- `DetermineEPPEligibility()`: Returns appropriate digit (0-9)

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
- System.ComponentModel.DataAnnotations (built-in)
- System.Linq (built-in)
- System.Reflection (built-in) ✨ NEW - For field validation

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
| **TAX LINE** ✨   | **XH** | **Tax Record** (auto-generated) |

### Tender Fund Codes (TNFFCD)
| Method            | Code | Description         |
|-------------------|------|---------------------|
| CASH              | CA   | Cash                |
| CHECK/CHEQUE      | CH   | Check               |
| DEBIT/DEBIT_CARD  | DC   | Debit Card          |
| VISA              | VI   | Visa Credit Card    |
| MASTERCARD        | MA   | Mastercard          |
| AMEX              | AX   | American Express    |
| GIFT_CARD         | PG   | Gift Card           |
| COUPON            | CP   | Coupon              |
| FLEXITI           | FX   | Flexiti Financing   |
| WEB_SALE          | PL   | PayLater            |
| PENNY_ROUNDING    | PR   | Penny Rounding      |
| CHANGE            | ZZ   | Change              |

## Version History

### v1.2.0 (01/19/26) ✨ Current
**Field Formatting & Validation Improvements:**
- ✨ **SLFRFD (ReferenceDesc)** - Right justified with zeros to 16 characters using transaction number
- ✨ **SLFUPC (UPCCode)** - Set to "0000000000000" (13 zeros) when blank
- ✨ **SLFSPS (SalesPerson)** - Set to "00000" (5 zeros) when blank
- ✨ **SLFQTN (QuantityNegativeSign)** - "-" for RETURN transactions, "" for all others (based on transaction type, not quantity sign)
- ✨ **SLFORG (OriginalPrice)** - Set to "000000000" for tax records
- ✨ **SLFORT (OriginalRetail)** - Uses OriginalUnitPrice for order records, "000000000" for tax records
- ✨ **SLFRFC (ReferenceCode)** - Always empty string "" for all records
- ✨ **SLFTE1, SLFTE2, SLFTEN** - Always empty string "" for all records (tax exemption fields)
- 🔧 **Negative sign fields** - All changed from " " (space) to "" (empty string) for non-negative values
  - Affects: SLFQTN, SLFORN, SLFADN, SLFOVN, SLFDSN, SLFSLN, SLFEXN, SLFOPN, TNFAMN

**Transaction Type-Based Field Logic:**
- ✨ **SLFOTS (OriginalTxStore)**
  - SALE (01): "00000" (5 zeros)
  - VOID (11) / RETURN (04): StoreId (padded to 5 chars)
- ✨ **SLFOTD (OriginalTxDate)**
  - SALE (01): "000000" (6 zeros)
  - VOID (11) / RETURN (04): Occurred date in YYMMDD format
- ✨ **SLFOTR (OriginalTxRegister)**
  - SALE (01): "000" (3 zeros)
  - VOID (11) / RETURN (04): Register number (padded to 3 chars)
- ✨ **SLFOTT (OriginalTxNumber)**
  - SALE (01): "00000" (5 zeros)
  - VOID (11): Transaction number from sequenceNumber (zero-padded to 5 chars)
  - RETURN (04): Source transaction ID (padded to 5 chars)

**Timezone & Date/Time Adjustments:**
- ✨ **SLFPDT (PollDate)** - Changed from DateTime.Now to transaction occurred date
- ✨ **Timezone adjustments** - Applied to SLFCTM, SLFCDT, SLFTTM, SLFTDT
  - Western region stores (BC, AB, SK, MB): Subtract 8 hours from occurredAt
  - Eastern region stores (ON, QC): Subtract 5 hours from occurredAt
  - Store region detection based on store ID patterns
  - Covers 200+ stores across all provinces

**Technical Improvements:**
- Added `ApplyTimezoneAdjustment()` method for timezone conversion
- Added `IsWesternRegionStore()` helper to identify store regions by ID
- Changed from `PadOrTruncate()` to `PadNumeric()` for numeric fields requiring zero-padding
- Enhanced field initialization logic for tax line items

### v1.1.0 (01/14/26)
**Major Features:**
- ✨ **Item-level tax parsing** - Comprehensive TaxDetail class with rate, amount, authority, and exemption tracking
- ✨ **Province-aware tax logic** - Ontario-specific rules (SLFTX2=N, SLFTX3=Y) with automatic province detection
- ✨ **Tax line items** - Separate LineType='XH' records for taxes with HON/HON1 authority codes
- ✨ **EPP (Extended Protection Plan) tracking** - Automatic detection and SLFZIP eligibility digit (0-9)
- ✨ **Enhanced field validation** - Reflection-based StringLength validation with detailed error messages
- 🔧 **Tax authority determination** - Intelligent mapping based on rates (13% HST → HON, 5% GST → HON1)
- 🔧 **Subtotal calculation** - Proper calculation from Net/Gross fields for tax rate determination
- 📝 **SLFRFD fix** - Always blank for item records per specification
- 📝 **Comprehensive documentation** - Validation tables HTML document for reference

**Technical Improvements:**
- Added System.Linq for LINQ operations
- Added System.Reflection for property introspection
- GetProvince() method for jurisdiction detection
- IsEPPItem(), IsSKUEligibleForEPP(), DetermineEPPEligibility() helper methods
- DetermineTaxAuthority() for HON/HON1 mapping
- ValidateStringFieldLengths() for comprehensive field validation

### v1.0.1 (12/03/25)
- Initial release with basic transformation logic
- ORAE v2.0.0 compliance validation
- OrderRecord and TenderRecord mapping
- Transaction type and line type mapping
- Fund code mapping for tenders

## Testing

### Test Mode
Run the application in test mode to process a single JSON file:

```bash
cd PubSubApp
dotnet run --test path/to/test-file.json
```

This will:
1. Read the JSON file
2. Validate ORAE v2.0.0 compliance
3. Transform to RecordSet
4. Validate output
5. Save output to file
6. Display validation results

### Sample Output Validation
The validation output includes:
- ✓ ORAE v2.0.0 compliance check
- ✓ RecordSet output validation
- ✓ Field length validation for all string fields
- ✓ Required field checks
- ✓ Tax logic validation (SLFTX flags, tax authority codes)
- ✓ EPP eligibility tracking validation

## Contributing

When contributing to this project:
1. Follow existing code style and patterns
2. Update validation documentation for schema changes
3. Add tests for new features
4. Update README.md with feature descriptions
5. Ensure all validation passes before committing

## License

Copyright © 2025-2026. All rights reserved.