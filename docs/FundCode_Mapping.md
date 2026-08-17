# Fund Code (TNFFCD) Mapping Logic

**PubSubApp v1.0.104 | RonaORAEPubSub | July 2026**

---

> Open specification questions affecting these codes — including the `TNFAUT` 6-digit limit on
> large gift card activations — are tracked in [Open_Questions.md](Open_Questions.md).

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

## Gift Card Activation Codes (Synthesized — `PP` and `PC`)

`PP` and `PC` never arrive in the payload. Unlike every code above, they are **synthesized by the
mapper** and appended after the real tenders whenever the cart contains a gift card activation
(`PR`, penny rounding, is synthesized the same way).

A gift card activation is not a payment, so these lines record *what happened to the card* rather
than money collected.

### `PC` — Activation record

Emitted **once per activated card**, in item order. A cart activating three cards emits three `PC`
lines.

| Field | Value |
|-------|-------|
| `TNFAMT` | Always `00000000000` — an activation, not money collected |
| `TNFCCD` | That card's token. Blank for an attribute-only promo GC, which carries no `giftCard` node |
| `TNFAUT` | That card's loaded value in cents, zero-padded to 6 (e.g. `002200` for $22.00) |
| `TNFRDS` | Loaded value padded to 14 + `" A"` activation marker (e.g. `00000000022.00 A`) |
| `TNFMSR` | Constant `"S"` |

⚠️ `TNFAUT` is a fixed 6 digits, so an activation of **$10,000.00 or more cannot be represented**.
The value is clamped to `999999` with an Error-level log; `TNFRDS` on the same line still carries
the true amount. How such an activation *should* be represented is an open question with Rona.

### `PP` — Promotional value

Emitted **once per transaction**, and only when a *promo* gift card is activated. Two promo
activations of $100 and $50 produce a single `PP` of $150 alongside two `PC` lines.

| Field | Value |
|-------|-------|
| `TNFAMT` | Summed promo value, from each item's `PromoGiftCard` discount `appliedAmount` |
| `TNFCCD` | Blank |
| `TNFMSR` | Constant `"S"` |

The split matters on a promo activation: the customer pays nothing for the card, so there is no
real tender to record. **`PP` books the value the store gave away; `PC` records that the card was
activated.** A standard gift card gets a `PC` only — the customer paid cash for it, and that is
already on the `CA` line.

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
| `EXTENDED` | `LC` | Extended |
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
