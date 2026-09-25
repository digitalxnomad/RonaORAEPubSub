# Example Test Files

This directory contains sample ORAE payloads and their expected output, used as the automated
regression suite.

## Layout

Samples sit either at the top level or in a scenario folder. Most are **real captures**; anything
named `synthetic_*` was constructed to exercise a code path no capture covers yet, and should be
replaced when a real payload of that shape arrives.

| Folder | Covers |
|--------|--------|
| *(top level)* | Basic sales, cash tender/rounding, SK PST, `transactionBurned` |
| `GC Activation/`, `Standard GC Activation/`, `Promo GC No GiftCard Node/` | Gift card activations — standard, promo, mixed, multi-card |
| `Returns/` | Returns (with and without receipt) and price adjustments, including the real cross-region and eco-fee captures |
| `Endless Aisle/` | Real in-store Endless Aisle payment (Aug 11) and refund (Aug 14) captures — line type `42` |
| `Cross Region/` | An item bought in QC (store 41100, receipt 2136) and returned in ON (store 55010, receipt 4839) — the pair proves the return reports the *purchase's* jurisdiction, not the returning store's |
| `Web Tendering/` | In-store payment against and refund of a web order (MIM-10971) — line type `30`. Real captures: a payment with money due, a deposit, a fulfilment with nothing due, and a refund. `soda_zeroship_refund` is a real SODA capture kept as a control, because `SODA` and `WEB_TENDERING` both emit `SLFLNT=30` by separate branches and a baseline on each keeps them from blurring together |
| `Tax Exemptions/` | Manual tax exemptions (MIM-10984 / MIM-10106). Real captures for QC `FIRST_NATION_PARTIAL`, BC `PST_ONLY` (three carts), AB `PROVINCIAL_GOVERNMENT` and the live Ontario `FIRST_NATION_PARTIAL` 07/13 capture, plus QC and BC returns of an exempt sale and a non-exempt AB control. `bc_exempt_first_nation_synthetic` is the one synthetic case — BC `FIRST_NATION`, the only rule branch with no real payload, and the only one that marks both `SLFTX1` and `SLFTX2`. Before this folder the exemption path had no coverage in any province, including the shipped Ontario rule; `on_exempt_first_nation_partial` exists to keep the Ontario scheme from drifting into the newer one |
| `Adjustment Pairing/` | Adjustment leg-pairing edge cases (duplicate SKU, `$0` leg, EPP coverage, fee-code conflict) — all synthetic |
| `Field Limits/` | Fixed-width overflow guards on `SLFRSN` and `TNFAUT` — synthetic |
| All other folders | Scenario captures from specific tickets — `SODA Mixed Cart/`, `Mother Baby SKU & UOM/`, `Manual Override (1)/`, `Cash+Visa/`, `CashRoundingUp/`, `Cash Rounding Down/`, `062326_Bugs/`, and so on. This table is not exhaustive; the suite discovers cases by convention, not from this list |

Files named `tactill order.json` are **not** ORAE payloads — they are a different format kept for
reference, carry no baseline, and are deliberately excluded from the suite.

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
committing:

```bash
PUBSUB_UPDATE_BASELINES=1 dotnet test    # PowerShell: $env:PUBSUB_UPDATE_BASELINES=1; dotnet test
git diff samples/
```

**A regeneration run always reports FAILED, by design.** It rewrites the expectations rather than
checking them, so it must never be mistaken for a passing suite — a green run with that variable
set would mean the regression net had silently replaced itself with whatever the mapper currently
emits. Review the diff, unset the variable, and re-run to actually verify.

### Adding a case

Drop the input and its `output_<name>.json` next to each other and the suite picks it up — no code
change. To generate the baseline for a new input, run it through test mode and copy the result
from the configured `OutputSavePath`, then **read the output before committing it**: a generated
baseline records what the mapper does today, which is only correct if today's behaviour is.

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
