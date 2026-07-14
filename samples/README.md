# Example Test Files

This directory contains sample JSON files for testing the RetailEvent processing functionality.

## Test File

### `test_retailevent.json`
Sample JSON file with RetailEvent v2.0.0 structure.

**Format:**
```json
{
  "businessContext": {...},
  "transaction": {...}
}
```

## How to Test

### Regression suite (automated)

```bash
dotnet test
```

`PubSubApp.Tests` runs every sample that has a committed baseline through the same three
gates `Program.cs` applies in production, one test each:

1. `OraeValidator.ValidateOraeCompliance` returns no errors (the pre-mapping gate).
2. Mapped output still matches its baseline, byte for byte.
3. `RecordSetValidator.ValidateRecordSetOutput` returns no errors (field lengths, required fields).

A case is any `<name>.json` with a sibling `output_<name>.json` in the same folder — drop a
new pair anywhere under `samples/` and all three tests pick it up with no code change.
Samples without an `output_*.json` are ignored.

When a mapping change is *intended*, regenerate the baselines and review the diff before
committing — this rewrites expectations, so an unreviewed run will happily bless a bug:

```bash
PUBSUB_UPDATE_BASELINES=1 dotnet test    # PowerShell: $env:PUBSUB_UPDATE_BASELINES=1; dotnet test
git diff samples/
```

### Test a single JSON RetailEvent by hand
```bash
cd PubSubApp
dotnet run --test ../samples/test_retailevent.json
```

Note that `--test` writes to `OutputSavePath` from `appsettings.json` (`C:\Data\Output`), and
only falls back to writing `output_<name>.json` next to the input when that setting is empty.

## What the Test Mode Does

1. **Loads** the JSON file (RetailEvent format)
2. **Parses** to RetailEvent structure
3. **Maps** to RecordSet (SDISLF/SDITNF)
4. **Validates** field lengths and required fields per CSV specs
5. **Outputs** RecordSet as JSON to console
6. **Saves** output to `*_output.json` file

## Output Example

```
=== JSON Test Mode ===
Reading JSON file: ../samples/test_retailevent.json

✓ JSON file loaded successfully

Parsing as RetailEvent and mapping to RecordSet...

=== RecordSet Output (JSON) ===
{
  "SDISLF": {
    "SLFTTP": "01",
    "SLFLNT": "01",
    "SLFTDT": "251215",
    "SLFTTM": "143000",
    ...
  },
  "SDITNF": {
    "TNFTTP": "01",
    "TNFTDT": "251215",
    "TNFTTM": "143000",
    ...
  }
}

=== Validation Results ===
Validating OrderRecord (SDISLF):
  ✓ All required fields present

Validating TenderRecord (SDITNF):
  ✓ All required fields present

=== Validation Summary ===
Errors: 0
Warnings: 0
✓ All validations passed!

✓ Output saved to: ../samples/test_retailevent_output.json
```

## Validation Checks

The test mode validates per CSV specifications:
- ✓ Required fields present (TransType, TransDate, TransTime, etc.)
- ✓ Date format: YYMMDD (6 digits)
- ✓ Time format: HHMMSS (6 digits)
- ✓ RegisterID: 3 digits with leading zeros
- ✓ TransNumber: 5 digits with leading zeros
- ✓ SKUNumber: 9 digits with leading zeros
- ✓ Quantity: 9 digits (multiply by 100)
- ✓ Prices: 9 digits (multiply by 100)
- ✓ Extended amounts: 11 digits (multiply by 100)
- ✓ Data types correct (int for PolledStore, etc.)

## Adding Your Own Test Files

1. Place JSON RetailEvent files in this directory
2. Run: `dotnet run --test ../samples/your_file.json`
3. Check the `*_output.json` file for results

## Expected JSON Structure

A valid input is a full ORAE v2.0.0 envelope. The required root fields below are
all checked by `OraeValidator` — omitting any of them fails validation:

```json
{
  "schemaVersion": "2.0.0",
  "messageType": "RetailEvent",
  "eventType": "ORIGINAL",
  "eventCategory": "TRANSACTION",
  "eventId": "1",
  "occurredAt": "2025-12-15T14:30:00Z",
  "ingestedAt": "2025-12-15T14:30:00Z",
  "businessContext": {
    "businessDay": "2025-12-15T00:00:00Z",
    "channel": "POS",
    "store": { "storeId": "100" },
    "workstation": { "registerId": "5" }
  },
  "transaction": {
    "transactionType": "SALE",
    "items": [...],
    "tenders": [...],
    "totals": { "gross": {...}, "discounts": {...}, "tax": {...}, "net": {...} }
  }
}
```

### Required-value gotchas

- **`messageType`** must be exactly `"RetailEvent"`, and **`eventType`** must be one of
  `ORIGINAL`, `CORRECTION`, `CANCELLATION`, `SNAPSHOT` (not the transaction type).
- **`transaction.totals`** must include `gross`, `discounts`, `tax`, and `net` — `discounts`
  is required even when it is `"0.00"`.
- **Tender `tenderId` carries the fund code.** The output `TNFFCD` is taken *directly* from
  `tender.tenderId`, which must be the **2-letter** fund code (e.g. `CA` cash, `VI` credit/Visa,
  `DC` debit, `MA` Mastercard) — not a free-form id like `"1"` or `"TENDER001"`. See
  [`../docs/FundCode_Mapping.md`](../docs/FundCode_Mapping.md) for the full table.
