# ORAE v2.0.0 Compliance Validation

## Overview

The `OraeValidator` validates incoming `RetailEvent` messages against the [ORAE v2.0.0 schema](https://transactiontree.com/schemas/orae/orae-2-0-0.json) before they are mapped and published downstream. It is implemented as a static class in `PubSubApp/Validation/OraeValidator.cs`.

**Entry point:** `OraeValidator.ValidateOraeCompliance(RetailEvent retailEvent)`
**Returns:** `List<string>` — empty if valid, otherwise contains one error message per violation.

---

## Configuration

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `DisableOraeValidation` | `bool` | `false` | When `true`, skips ORAE validation entirely and logs a warning |

Set in `appsettings.json` under `PubSubConfiguration`:
```json
{
  "PubSubConfiguration": {
    "DisableOraeValidation": false
  }
}
```

---

## Message Processing Behavior

| Scenario | Action | PubSub Reply |
|----------|--------|--------------|
| Validation passes | Message proceeds to mapping and publishing | ACK (on success) |
| Validation fails | Errors logged + Slack alert sent | **ACK** (prevents redelivery of invalid data) |
| Validation disabled | Warning logged, message skips validation | Depends on downstream |

When validation fails, a Slack alert is sent immediately (no cooldown) with the error count and up to 5 error details, truncated to 500 characters.

---

## Validation Rules

### 1. Root-Level Required Fields

| Field | Type | Rule | Example Error |
|-------|------|------|---------------|
| `schemaVersion` | `string` | Required. Must be exactly `"2.0.0"` | `Invalid schemaVersion: 2.1.0. Expected '2.0.0'` |
| `messageType` | `string` | Required. Must be exactly `"RetailEvent"` | `Invalid messageType: Order. Expected 'RetailEvent'` |
| `eventType` | `string` | Required. Must be one of: `ORIGINAL`, `CORRECTION`, `CANCELLATION`, `SNAPSHOT` | `Invalid eventType: PENDING. Must be one of: ORIGINAL, CORRECTION, CANCELLATION, SNAPSHOT` |
| `eventCategory` | `string` | Required. Must be one of: `TRANSACTION`, `TILL`, `STORE`, `SESSION`, `BANKING`, `ORDER`, `INVENTORY`, `MAINTENANCE`, `CALIBRATION`, `AUDIT`, `STATUS`, `LOYALTY`, `TOPUP`, `PROMOTION`, `OTHER` | `Invalid eventCategory: UNKNOWN` |
| `eventId` | `string` | Required. Non-empty | `Missing required field: eventId` |
| `occurredAt` | `DateTime` | Required. Must not be default | `Missing required field: occurredAt` |
| `ingestedAt` | `DateTime` | Required. Must not be default | `Missing required field: ingestedAt` |

---

### 2. businessContext (Required Object)

If the entire `businessContext` object is null, a single error is returned and nested checks are skipped.

#### 2.1 businessContext Root Fields

| Field | Type | Rule | Example Error |
|-------|------|------|---------------|
| `businessDay` | `DateTime` | Required. Must not be default | `Missing required field: businessContext.businessDay` |
| `channel` | `string` | Required. Non-empty | `Missing required field: businessContext.channel` |

#### 2.2 businessContext.store (Required Object)

| Field | Type | Rule | Example Error |
|-------|------|------|---------------|
| `storeId` | `string` | Required. Non-empty | `Missing required field: businessContext.store.storeId` |
| `currency` | `string` | Optional. If present, must be 3 uppercase letters (ISO-4217) | `Invalid businessContext.store.currency: 'USD5'. Must be ISO-4217 (3 uppercase letters)` |

#### 2.3 businessContext.workstation (Required Object)

| Field | Type | Rule | Example Error |
|-------|------|------|---------------|
| `registerId` | `string` | Required. Non-empty | `Missing required field: businessContext.workstation.registerId` |
| `sequenceNumber` | `int?` | Optional. If present, must be >= 0 | `Invalid businessContext.workstation.sequenceNumber: must be >= 0` |

---

### 3. Category-Specific Payload

| Rule | Example Error |
|------|---------------|
| If `eventCategory` is `"TRANSACTION"`, the `transaction` object must be present | `eventCategory 'TRANSACTION' requires transaction object` |

---

### 4. transaction (Validated When Present)

#### 4.1 transaction Root Fields

| Field | Type | Rule | Example Error |
|-------|------|------|---------------|
| `transactionType` | `string` | Required. Must be one of: `SALE`, `RETURN`, `EXCHANGE`, `VOID`, `CANCEL`, `ADJUSTMENT`, `NONMERCH`, `SERVICE` | `Invalid transaction.transactionType: REFUND. Must be one of: SALE, RETURN, EXCHANGE, VOID, CANCEL, ADJUSTMENT, NONMERCH, SERVICE` |

#### 4.2 transaction.totals (Required Object)

All Money fields below follow the [Money Object Rules](#7-money-object-validation) described in section 7.

| Field | Required | Example Error |
|-------|----------|---------------|
| `gross` | Yes | `Missing required field: transaction.totals.gross` |
| `discounts` | Yes | `Missing required field: transaction.totals.discounts` |
| `tax` | Yes | `Missing required field: transaction.totals.tax` |
| `net` | Yes | `Missing required field: transaction.totals.net` |
| `changeDue` | No (validated if present) | — |
| `cashRounding` | No (validated if present) | — |

#### 4.3 transaction.items[] (Conditional Requirement)

| Rule | Example Error |
|------|---------------|
| Must have at least 1 item unless `transactionType` is `CANCEL` or `VOID` | `transaction.items[] must have at least one item for non-CANCEL/VOID transactions` |

---

### 5. LineItem Validation (Per Item in `transaction.items[]`)

Each item is validated with an indexed path prefix: `transaction.items[N]`.

| Field | Type | Rule | Example Error |
|-------|------|------|---------------|
| `lineId` | `string` | Required. Non-empty | `Missing required field: transaction.items[0].lineId` |
| `item` | `object` | Required | `Missing required object: transaction.items[0].item` |
| `item.sku` | `string` | Required. Non-empty | `Missing required field: transaction.items[0].item.sku` |
| `item.description` | `string` | Required. Non-empty | `Missing required field: transaction.items[0].item.description` |
| `quantity` | `object` | Required | `Missing required object: transaction.items[0].quantity` |
| `quantity.uom` | `string` | Required. Non-empty | `Missing required field: transaction.items[0].quantity.uom` |
| `pricing` | `object` | Required | `Missing required object: transaction.items[0].pricing` |
| `pricing.unitPrice` | `Money` | Required. Follows Money rules | `Missing required field: transaction.items[0].pricing.unitPrice` |
| `pricing.extendedPrice` | `Money` | Required. Follows Money rules | `Missing required field: transaction.items[0].pricing.extendedPrice` |

#### 5.1 LineItem Tax Components (`transaction.items[N].taxes[]`)

Each tax entry is validated with an indexed path: `transaction.items[N].taxes[T]`.

| Field | Type | Rule | Example Error |
|-------|------|------|---------------|
| `amount` | `Money` | Required. Follows Money rules | `Missing required field: transaction.items[0].taxes[0].amount` |

---

### 6. Tender Validation (Per Entry in `transaction.tenders[]`)

Each tender is validated with an indexed path prefix: `transaction.tenders[N]`.

| Field | Type | Rule | Example Error |
|-------|------|------|---------------|
| `tenderId` | `string` | Required. Non-empty | `Missing required field: transaction.tenders[0].tenderId` |
| `method` | `string` | Required. Non-empty | `Missing required field: transaction.tenders[0].method` |
| `amount` | `Money` | Required. Follows Money rules | `Missing required field: transaction.tenders[0].amount` |

---

### 7. Money Object Validation

All `Money` (`CurrencyAmount`) fields throughout the schema are validated with the same rules via the `ValidateMoney` helper. The `path` in error messages reflects the field's location in the object hierarchy.

| Field | Rule | Regex | Example Error |
|-------|------|-------|---------------|
| `currency` | Required. Must be 3 uppercase letters (ISO-4217) | `^[A-Z]{3}$` | `Invalid transaction.totals.gross.currency: 'usd'. Must be ISO-4217 (3 uppercase letters)` |
| `value` | Required. Must be a signed decimal string: up to 18 integer digits, up to 4 decimal places | `^-?[0-9]{1,18}(\.[0-9]{1,4})?$` | `Invalid transaction.totals.gross.value: 'abc'. Must be signed decimal string (e.g., '12.34', '-5.00')` |

**Valid `value` examples:** `"12.34"`, `"-5.00"`, `"0"`, `"123456789012345678.1234"`
**Invalid `value` examples:** `"abc"`, `"12.34567"` (>4 decimals), `""`, `"$12.34"`

---

## Where Validation Is Invoked

| Context | File | Behavior on Failure |
|---------|------|-------------------|
| **Main message handler** | `Program.cs` ~line 312 | Errors logged, Slack alert sent (no cooldown), message ACK'd |
| **Test mode** (`--test`) | `Program.cs` ~line 612 | Errors logged to console, processing stops |

---

## Alerting

When ORAE validation fails in the main message handler:

- A Slack alert is sent **immediately** (no cooldown) via `SendSlackAlertImmediate`
- The alert includes:
  - The error count
  - Up to 5 specific validation errors
  - Truncated to 500 characters if necessary
- The alert is prefixed with the machine hostname: `[HOSTNAME] ✗ ORAE VALIDATION FAILED: ...`
- Requires `SlackWebhookUrl` to be configured in `appsettings.json`

---

## Validation Hierarchy Diagram

```
RetailEvent
├── schemaVersion        (required, must be "2.0.0")
├── messageType          (required, must be "RetailEvent")
├── eventType            (required, enum)
├── eventCategory        (required, enum)
├── eventId              (required)
├── occurredAt           (required)
├── ingestedAt           (required)
├── businessContext       (required object)
│   ├── businessDay      (required)
│   ├── channel          (required)
│   ├── store            (required object)
│   │   ├── storeId      (required)
│   │   └── currency     (optional, ISO-4217 if present)
│   └── workstation      (required object)
│       ├── registerId   (required)
│       └── sequenceNumber (optional, >= 0 if present)
└── transaction          (required if eventCategory = "TRANSACTION")
    ├── transactionType  (required, enum)
    ├── totals           (required object)
    │   ├── gross        (required Money)
    │   ├── discounts    (required Money)
    │   ├── tax          (required Money)
    │   ├── net          (required Money)
    │   ├── changeDue    (optional Money)
    │   └── cashRounding (optional Money)
    ├── items[]          (>= 1 required unless CANCEL/VOID)
    │   ├── lineId       (required)
    │   ├── item         (required object)
    │   │   ├── sku          (required)
    │   │   └── description  (required)
    │   ├── quantity     (required object)
    │   │   └── uom          (required)
    │   ├── pricing      (required object)
    │   │   ├── unitPrice     (required Money)
    │   │   └── extendedPrice (required Money)
    │   └── taxes[]
    │       └── amount   (required Money)
    └── tenders[]
        ├── tenderId     (required)
        ├── method       (required)
        └── amount       (required Money)
```

**Money Object** (used throughout):
```
CurrencyAmount
├── currency    (required, regex: ^[A-Z]{3}$)
└── value       (required, regex: ^-?[0-9]{1,18}(\.[0-9]{1,4})?$)
```
