# Fund Code (TNFFCD) Mapping Logic

**PubSubApp v1.0.50 | RonaORAEPubSub | February 2026**

---

## How It Works

The fund code is set **directly from `tender.TenderId`** in the incoming ORAE data:

```csharp
tenderRecord.FundCode = tender.TenderId ?? "";
```

The ORAE system sends the 2-letter fund code as the `tenderId` field. The code uses that value directly.

---

## Cash Tender Records (Special Case)

When `tender.Method == "CASH"`, up to **3 records** are created per transaction:

| Record | Fund Code | Source Field | Sign Rule |
|--------|-----------|--------------|-----------|
| **Line 1** – Cash received (mandatory) | From `tender.TenderId` (typically `CA`) | `tender.amount.value` | Standard (positive = blank, negative = `-`) |
| **Line 2** – Change returned (optional) | Hardcoded `CA` | `totals.changeDue` | Always `-` |
| **Line 3** – Penny rounding (optional) | Hardcoded `PR` | `totals.cashRounding` | **Inverted** (ORAE positive → `-`, ORAE negative → blank) |

### Rules

- Lines 2 and 3 are **optional** — only created if the value is non-zero and non-null
- Lines 2 and 3 are created **once per transaction** regardless of how many CASH tenders exist (controlled by `cashChangeProcessed` flag)
- Line 2 sign is **always** `-` because change returned is money going back to the customer
- Line 3 sign is **inverted** from ORAE: if ORAE reports a positive rounding value, the output sign is `-`; if ORAE reports negative, the output sign is blank

---

## Fallback (No Tenders Array)

If no tenders array exists but `totals.net.value` is available, a single tender record is created with fund code **`CA`** (default cash).

---

## Fund Code Reference Table

### MapTenderMethodToFundCode

Maps the `tender.method` string to a 2-letter fund code. This method exists as a helper/fallback but is **not called** in the main tender loop (since `TenderId` is used directly).

| ORAE Method | Fund Code | Description |
|-------------|-----------|-------------|
| `CASH` | `CA` | Cash |
| `CHECK` / `CHEQUE` | `CH` | Cheque |
| `DEBIT` / `DEBIT_CARD` / `DEBITATM` | `DC` | Debit Card |
| `CREDIT` / `CREDIT_CARD` | `VI` | Credit Card (defaults to VISA) |
| `VISA` | `VI` | Visa |
| `MASTERCARD` / `MASTER_CARD` | `MA` | Mastercard |
| `AMEX` / `AMERICAN_EXPRESS` | `AX` | American Express |
| `GIFT_CARD` / `GIFTCARD` | `PG` | Gift Card Redeemed |
| `COUPON` | `CP` | Coupon |
| `TRAVELLERS_CHEQUE` / `TRAVELERS_CHECK` | `TC` | Travellers Cheque |
| `US_CASH` | `US` | US Cash |
| `FLEXITI` | `FX` | Flexiti |
| `WEB_SALE` | `PL` | Web Sale |
| `PENNY_ROUNDING` | `PR` | Penny Rounding |
| `CHANGE` | `ZZ` | Change |
| _(unknown)_ | `CA` | Default to Cash |

### MapCardSchemeToFundCode

Maps the `tender.card.scheme` string to a 2-letter fund code. Used as a secondary lookup when card scheme information is available.

| Card Scheme | Fund Code | Description |
|-------------|-----------|-------------|
| `VISA` | `VI` | Visa |
| `MASTERCARD` / `MASTER_CARD` / `MC` | `MA` | Mastercard |
| `AMEX` / `AMERICAN EXPRESS` / `AMERICANEXPRESS` | `AX` | American Express |
| `DEBIT` / `DEBITATM` | `DC` | Debit Card |
| `GIFTCARD` / `GIFT_CARD` | `PG` | Gift Card |
| _(unknown)_ | `VI` | Default to Visa |

---

## Processing Flow Summary

```
Tender Loop:
  ├── For EACH tender:
  │     ├── Set FundCode = tender.TenderId (direct from ORAE)
  │     ├── Set Amount from tender.amount.value (standard sign)
  │     ├── Set Card fields if tender.card exists
  │     └── Add record to TenderRecords
  │
  │     └── If CASH and not yet processed:
  │           ├── Line 2: CA record from totals.changeDue (always "-")
  │           └── Line 3: PR record from totals.cashRounding (inverted sign)
  │
  └── Fallback (no tenders):
        └── Single CA record from totals.net.value
```
