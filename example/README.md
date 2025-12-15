# Example Test Files

This directory contains sample files for testing the XML/JSON processing functionality.

## Test Files

### 1. `test_recordset.xml`
Sample XML file with RecordSet structure (SDISLF + SDITNF format).

**Format:**
```xml
<RecordSet>
  <SDISLF>...</SDISLF>
  <SDITNF>...</SDITNF>
</RecordSet>
```

### 2. `test_retailevent.json`
Sample JSON file with RetailEvent v2.0.0 structure.

**Format:**
```json
{
  "businessContext": {...},
  "transaction": {...}
}
```

## How to Test

### Option 1: Test XML RecordSet
```bash
cd PubSubApp
dotnet run --test ../example/test_recordset.xml
```

### Option 2: Test JSON RetailEvent
```bash
cd PubSubApp
dotnet run --test ../example/test_retailevent.json
```

## What the Test Mode Does

1. **Loads** the file (XML or JSON)
2. **Parses** to RecordSet structure
3. **Validates** field lengths and required fields
4. **Outputs** RecordSet as JSON to console
5. **Saves** output to `*_output.json` file

## Output Example

```
=== XML Test Mode ===
Reading XML file: ../example/test_recordset.xml

✓ XML file loaded successfully

Detected XML format
Parsing XML to RecordSet...

=== RecordSet Output (JSON) ===
{
  "SDISLF": {
    "SLFTTP": "01",
    ...
  },
  "SDITNF": {
    "TNFTTP": "01",
    ...
  }
}

=== Validation Results ===
...

✓ Output saved to: ../example/test_recordset_output.json
```

## Validation Checks

The test mode validates:
- ✓ Required fields present
- ✓ Field lengths match CSV specs (YYMMDD = 6, SKU = 9, etc.)
- ✓ Data types correct (int for PolledStore, etc.)

## Adding Your Own Test Files

1. Place XML/JSON files in this directory
2. Run: `dotnet run --test example/your_file.xml`
3. Check the `*_output.json` file for results
