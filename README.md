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

**Actor (Root Level):** ✨ NEW
- actor.cashier.loginId (cashier login identifier for ACO transactions)
- actor.customer.customerIdToken (customer number for SLFNUM) ✨ NEW

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
    - **jurisdiction** ✨ NEW (country, region, locality, authorityId, authorityName)
- tenders[] (payment methods: CASH, CARD, etc.)
  - tenderId, method, amount
  - card.scheme, card.last4, card.authCode ✨
  - card.emv.tags.magStrip (mag stripe flag) ✨ NEW
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
- Credit/debit card information
  - **TNFCCD**: Last4 padded LEFT with asterisks (e.g., `************1234`) ✨
  - **TNFAUT**: Authorization code
  - **TNFMSR**: Mag stripe flag from EMV tags ✨ NEW
  - **TNFRDC**: Always blank ✨
  - **TNFRDS**: Blank for credit/debit/flexiti, otherwise TenderId ✨
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
- **Tax Consolidation**: All item taxes combined into ONE tax record per transaction ✨
  - `ExtendedValue` and `ItemSellPrice` contain transaction-level tax total
  - Single tax line item representing HST or Partial HST across all SKUs

**Other Provinces:**
- **PST**: `SLFTX1=Y`
- **GST**: `SLFTX2=Y`
- **HST**: `SLFTX1=Y`, `SLFTX2=Y` (combined)
- **QST**: `SLFTX3=Y` (Quebec)
- **Municipal**: `SLFTX4=Y`
- **Tax Records**: Per-item tax records (one tax line per item)

### Tax Line Items

**Ontario Transactions:**
One consolidated tax line item per transaction:
- **LineType**: From TBLFLD TAXTXC mapping (e.g., "XH" for HON, "XI" for HON1)
- **Tax Flags**: All set to "N" (`SLFTX1-4=N`)
- **Tax Authority (SLFACD)**: From `taxes.jurisdiction.region`
- **Tax Rate Code (SLFTCD)**: From `taxes.taxCode` (first with jurisdiction)
- **ExtendedValue**: Sum of all item taxes in transaction
- **Sequence**: Gets its own sequence number after all item lines

**Non-Ontario Transactions:**
Per-item tax line items:
- **LineType**: From TBLFLD TAXTXC mapping (province-specific)
- **Tax Flags**: Province-specific flags
- **Tax Authority (SLFACD)**: From `taxes.jurisdiction.region`
- **Tax Rate Code (SLFTCD)**: From `taxes.taxCode` (first with jurisdiction)
- **ExtendedValue**: Individual item tax amount
- **Sequence**: One per item with taxes

**Example Ontario Transaction Structure:**
```
OrderRecords:
  [1] Item 1 - SLFLNT="01", SLFTX2="N", SLFTX3="Y", SLFACD="", SLFTCD="", SLFEXT="$50.00"
  [2] Item 2 - SLFLNT="01", SLFTX2="N", SLFTX3="Y", SLFACD="", SLFTCD="", SLFEXT="$30.00"
  [3] Tax    - SLFLNT="XH", SLFACD="ON", SLFTCD="HST13", SLFEXT="$10.40" (combined)
```

**Example Non-Ontario Transaction Structure:**
```
OrderRecords:
  [1] Item 1 - SLFLNT="01", SLFTX1="Y", SLFACD="", SLFTCD="", SLFEXT="$50.00"
  [2] Tax    - SLFLNT="XR", SLFACD="BC", SLFTCD="PST07", SLFEXT="$6.50" (item 1 tax)
  [3] Item 2 - SLFLNT="01", SLFTX1="Y", SLFACD="", SLFTCD="", SLFEXT="$30.00"
  [4] Tax    - SLFLNT="XR", SLFACD="BC", SLFTCD="PST07", SLFEXT="$3.90" (item 2 tax)
```

## EPP (Extended Protection Plan) ✨ NEW

### EPP Detection
The application detects EPP items using:
- **Item attribute**: `x-epp-coverage-identifier` in `transaction.items[i].attributes`

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
| Input Type      | Code | Description        |
|-----------------|------|--------------------|
| SALE            | 01   | Regular Sale       |
| SALE (Employee) | 04   | Employee Sale      |
| RETURN          | 11   | Return             |
| AR_PAYMENT      | 43   | AR Payment         |
| VOID            | 87   | Current Void       |
| POST_VOID       | 88   | Post Void          |

### SLFLNT (Line Type) Codes
| Input Type                     | Code   | Description              |
|--------------------------------|--------|--------------------------|
| SALE                           | 01     | Regular Sale             |
| SALE (Customer)                | 02     | Customer Sale            |
| SALE (Employee)                | 04     | Employee Sale            |
| RETURN                         | 11     | Regular Return           |
| RETURN (Customer)              | 12     | Customer Return          |
| SALE/RETURN (Gift Card)        | 45     | Gift Card Transaction    |
| VOID                           | 87     | Current Void             |
| POST_VOID                      | 01     | Post Void                |
| **TAX LINE (TBLFLD TAXTXC)** ✨ | **varies** | **Tax Record** (auto-generated) |

### Tax Line Type Mapping (TBLFLD TAXTXC) ✨ NEW
| Tax Authority | Line Type | Description          |
|---------------|-----------|----------------------|
| BC            | XR        | British Columbia     |
| FED           | XG        | Federal GST          |
| HNB           | XN        | New Brunswick HST    |
| HNF           | XF        | Newfoundland HST     |
| HNS           | XV        | Nova Scotia HST      |
| HON           | XH        | Ontario HST          |
| HON1          | XI        | Ontario GST Portion  |
| HPE           | XP        | PEI HST              |
| MB            | XM        | Manitoba PST         |
| PQ            | XQ        | Quebec QST           |
| SK            | XS        | Saskatchewan PST     |
| (default)     | XH        | Default              |

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

### v1.0.36 (02/02/26) ✨ Current
**SLFTTP/SLFLNT Mapping Corrections & New Model Enhancements:**
- ✨ **SLFTTP Corrected** - Proper transaction type codes:
  - RETURN → "11" (was "02"), VOID → "87" (was "11"), POST_VOID → "88", AR_PAYMENT → "43"
  - Employee SALE → "04" (restored from temporary "01")
- ✨ **SLFLNT Corrected** - Line type mappings updated:
  - Employee → "04", VOID → "87", POST_VOID → "01"
  - Gift card tender → "45", Customer → "02"/"12"
- ✨ **MapTaxAuthToLineType** - Tax line types from TBLFLD TAXTXC mapping table
  - BC→XR, FED→XG, HNB→XN, HNF→XF, HNS→XV, HON→XH, HON1→XI, HPE→XP, MB→XM, PQ→XQ, SK→XS
- ✨ **SLFNUM (Customer Number)** - 10-digit padded from `actor.customer.customerIdToken`
- ✨ **SLFACD** - Blank for order records; `taxes.jurisdiction.region` for tax records
- ✨ **SLFTCD** - Blank for order records; `taxes.taxCode` (first with jurisdiction) for tax records
- ✨ **SLFRSN (Reason Code)** - Default 16 blank spaces; RRT0 (return), VOD0 (void), POV0 (override), IDS0 (manual discount)
- ✨ **SLFSCN (Item Scanned)** - Y if item.gtin > 0, N otherwise; blank for tax records
- ✨ **SLFUPC** - Populated from `item.gtin` (13-digit padded), all zeros if no value
- ✨ **TNFRDS** - Always blank for all tender types

**New Model Classes:**
- ✨ **Customer** - `customerIdToken` property for customer number
- ✨ **TaxJurisdiction** - `country`, `region`, `locality`, `authorityId`, `authorityName`
- ✨ **Item.Gtin** - UPC code from ORAE item data
- ✨ **TransactionItem.Attributes** - Dictionary for EPP detection via `x-epp-coverage-identifier`

**Validation & Error Handling:**
- ✨ **ValidateOraeCompliance** - Updated for ORAE 2.0.0 schema with Money validation, ISO-4217 currency check
- ✨ **ACK on validation failure** - Invalid messages now ACKed to prevent redelivery loops
- ✨ **Original tx fields** - Updated references to use corrected SLFTTP codes (01/04/43 → zeros, 11/87/88 → populated)

### v1.3.0 (01/26/26)
**Ontario Tax Consolidation & Actor Structure:**
- ✨ **Ontario Tax Consolidation** - All item taxes combined into ONE tax record per transaction
  - Province detection via `GetProvince()` method
  - Transaction-level tax total calculation across all SKUs
  - `TaxAuthCode`: "HON" (Full HST 13%) or "HON1" (Partial HST/GST 5%)
  - `ExtendedValue` and `ItemSellPrice` contain combined tax amount
  - Non-Ontario provinces maintain per-item tax records
- ✨ **Actor/Cashier Structure** - Moved to root level of RetailEvent
  - `actor.cashier.loginId` parsed from root instead of transaction
  - Supports ACO (Assisted Checkout) salesperson logic
- ✨ **SLFSPS (SalesPerson) Logic** - SCO vs ACO determination
  - **SCO** (Self-Checkout): Register starts with "8" → uses `Workstation.RegisterID` padded to 5 digits
  - **ACO** (Assisted Checkout): Register doesn't start with "8" → uses `actor.cashier.loginId` padded to 5 digits
- ✨ **SLFTCD (TaxRateCode)** - Blank for order records; maps from `taxes.taxCode` (first with jurisdiction) for tax records
- ✨ **All Date/Time Fields** - Timezone adjustment applied consistently
  - `SLFTDT`, `SLFTTM`, `TNFTDT`, `TNFTTM` all use `ApplyTimezoneAdjustment()`
  - Western region (BC, AB, SK, MB): -8 hours
  - Eastern region (ON, QC): -5 hours

**Tender Field Enhancements:**
- ✨ **TNFRDC (ReferenceCode)** - Always blank ("") for all tender types
- ✨ **TNFRDS (ReferenceDesc)** - Always blank for all tender types
- ✨ **TNFCCD (CreditCardNumber)** - Last4 only, padded LEFT with asterisks
  - Format: `************1234` (19 chars total)
  - Security-enhanced format for PCI compliance
- ✨ **TNFMSR (MagStripeFlag)** - Added from `card.emv.tags.magStrip`
  - JSON path: `Transaction.Tenders[n].card.emv.tags.magStrip`
  - Padded/truncated to 1 character
  - Default: " " (1 space) when no card data

**Bug Fixes:**
- 🔧 **Pub/Sub Subscriber Async Fix** - Messages now received immediately
  - Changed from awaiting `StartAsync` directly to storing Task
  - Added 1-second initialization delay
  - Resolves issue where messages weren't processed until Ctrl+C pressed
- 🔧 **MapRetailEventToRecordSet** - Fixed missing closing braces
  - Added missing brace in non-Ontario tax processing section
  - Resolves "not all code paths return a value" compilation error
- 🔧 **Indentation Error** - Fixed closing brace alignment on line 2004

**Technical Improvements:**
- Added `Actor` and `Cashier` classes to data model
- Added `Emv` and `EmvTags` classes for EMV data
- Enhanced `DetermineTaxAuthority()` for Ontario-specific logic
- Province-based branching for tax record generation
- Improved code structure and error handling

### v1.2.0 (01/19/26)
**Field Formatting & Validation Improvements:**
- ✨ **SLFRFD (ReferenceDesc)** - Right justified with zeros to 16 characters using transaction number
- ✨ **SLFUPC (UPCCode)** - Set to "0000000000000" (13 zeros) when blank
- ✨ **SLFSPS (SalesPerson)** - Set to "00000" (5 zeros) when blank
- ✨ **SLFQTN (QuantityNegativeSign)** - "-" for RETURN transactions, "" for all others (based on transaction type, not quantity sign)
- ✨ **SLFORG (OriginalPrice)** - Set to "000000000" for tax records
- ✨ **SLFORT (OriginalRetail)** - Uses OriginalUnitPrice for order records, "000000000" for tax records
- ✨ **SLFRFC (ReferenceCode)** - Always empty string "" for all records
- ✨ **SLFTE1, SLFTE2, SLFTEN** - Always empty string "" for all records (tax exemption fields)
- ✨ **SLFADC (AdCode)** - Conditional logic based on pricing:
  - "####" when POS price ≠ regular retail (unitPrice ≠ originalUnitPrice)
  - "0000" when at regular price (unitPrice = originalUnitPrice)
  - Always "0000" for tax records
- ✨ **SLFEXT (ExtendedValue)** - Calculated value instead of using extendedPrice directly:
  - Formula: Quantity × Override (if pricing.override exists)
  - Fallback: Quantity × UnitPrice (if no override)
  - Formatted as 11-digit field with sign
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

**ORAE Data Structure Enhancements:**
- ✨ **pricing.override field** - Added optional `override` field to Pricing class (CurrencyAmount type)
  - Allows incoming ORAE data to specify price overrides
  - Used in SLFEXT calculation when present
  - JSON property name: "override"

**Error Handling & Reliability:**
- ✨ **Invalid JSON handling** - Messages with invalid JSON are now ACKed instead of NACKed
  - Prevents infinite redelivery of malformed messages
  - Logs error details for debugging
  - Returns `SubscriberClient.Reply.Ack` for `JsonException`
  - Other exceptions continue to NACK for proper retry handling

**Technical Improvements:**
- Added `ApplyTimezoneAdjustment()` method for timezone conversion
- Added `IsWesternRegionStore()` helper to identify store regions by ID
- Changed from `PadOrTruncate()` to `PadNumeric()` for numeric fields requiring zero-padding
- Enhanced field initialization logic for tax line items
- Added try-catch for `JsonException` in message processing pipeline

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