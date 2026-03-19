# Plan: Consolidated Tax Lines for GST+PST/QST Provinces

## Problem
Currently, non-Ontario provinces (BC, MB, QC, SK) create **per-item** tax records — one tax line per item, lumping all of that item's taxes into a single amount. The new requirement says these provinces should create **two consolidated** tax lines across all SKUs: one for Federal (GST) and one for Provincial (PST/QST), matching the Ontario consolidation pattern.

## Current Behavior (Non-Ontario)
```
Item 1 (SKU 123) → Tax line (all taxes for item 1 combined)
Item 2 (SKU 456) → Tax line (all taxes for item 2 combined)
```

## Target Behavior (BC, MB, QC, SK)
```
Item 1 (SKU 123)
Item 2 (SKU 456)
Federal GST tax line (GST summed across all items) → LineType "XG", TaxAuthCode "FED"
Provincial tax line (PST/QST summed across all items) → LineType varies by province, TaxAuthCode varies
```
- If either federal or provincial tax total is 0, skip that tax line.

## Changes

### File: `PubSubApp/RetailEventMapper.cs`

#### 1. Add GST+PST/QST province detection (after line 462)
Add a check for whether the province is one of the four GST+PST/QST provinces:
```csharp
bool isGstPstProvince = province?.ToUpper() is "BC" or "MB" or "SK" or "QC";
```

#### 2. Replace the `else` block (lines 691–825) with a three-way branch
The current code has:
- `if (isOntario)` → consolidated HON/HON1
- `else` → per-item tax records

Change to:
- `if (isOntario)` → consolidated HON/HON1 (unchanged)
- `else if (isGstPstProvince)` → **NEW** consolidated Federal/Provincial tax lines
- `else` → per-item tax records (original logic, for all other provinces like AB, HST provinces, etc.)

#### 3. New `else if (isGstPstProvince)` block logic
Iterate all items, separate each item's taxes into Federal vs Provincial buckets:
- **Federal (GST):** taxType is "GST", "FEDERAL", "VAT", "NATIONAL" → accumulate into `federalTaxTotal`
- **Provincial (PST/QST):** taxType is "PST", "QST", "PROVINCIAL", "STATE", "QUEBEC" → accumulate into `provincialTaxTotal`
- Capture the first encountered `taxCode` for each bucket (for TaxRateCode)

Then create up to 2 consolidated tax records:
1. **Federal GST record** (if `federalTaxTotal != 0`):
   - `TaxAuthCode = "FED"` (padded to 6)
   - `LineType = "XG"` (from MapTaxAuthToLineType)
   - `ExtendedValue` / `ItemSellPrice` = formatted `federalTaxTotal`

2. **Provincial record** (if `provincialTaxTotal != 0`):
   - `TaxAuthCode` determined by province: "BC" → "BC", "MB" → "MB", "SK" → "SK", "QC" → "PQ"
   - `LineType` from `MapTaxAuthToLineType`: BC→"XR", MB→"XM", SK→"XS", QC/PQ→"XQ"
   - `ExtendedValue` / `ItemSellPrice` = formatted `provincialTaxTotal`

Both tax records use the same boilerplate fields as the existing Ontario tax records (SKU="000000000", Quantity="000000100", etc.).

#### 4. No changes needed to `ParseItemTaxes`
The item-level ChargedTax1-4 flags for non-Ontario provinces already work correctly (PST→Tax1=Y, GST→Tax2=Y, QST→Tax3=Y). No changes needed there.

#### 5. No changes needed to `MapTaxAuthToLineType`
The existing mapping already has entries for BC, FED, MB, PQ, SK. No changes needed.

### Helper: Province-to-TaxAuthCode mapping
Add a small helper or inline mapping for the provincial tax authority code:
```csharp
string provincialTaxAuth = province?.ToUpper() switch
{
    "BC" => "BC",
    "MB" => "MB",
    "SK" => "SK",
    "QC" => "PQ",
    _ => province?.ToUpper() ?? ""
};
```

## Summary of Output Structure

For a BC transaction with 2 SKUs:
```
OrderRecord[0] = Item 1 (LineType="01", SKU="123456789", SLFTX1="Y", SLFTX2="Y")
OrderRecord[1] = Item 2 (LineType="01", SKU="987654321", SLFTX1="Y", SLFTX2="Y")
OrderRecord[2] = Federal GST (LineType="XG", SKU="000000000", TaxAuthCode="FED  ")
OrderRecord[3] = BC PST     (LineType="XR", SKU="000000000", TaxAuthCode="BC    ")
```

## Risk Assessment
- **Low risk**: Ontario logic is untouched
- **Low risk**: Other provinces (AB, HST provinces) fall through to existing per-item logic
- **Medium risk**: Need to handle edge case where a tax doesn't have a clear taxType — fallback to jurisdiction region or rate-based detection
