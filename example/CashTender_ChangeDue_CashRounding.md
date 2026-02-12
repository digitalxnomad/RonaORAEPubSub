# Cash Tender Records — changeDue & cashRounding Breakdown

**PubSubApp v1.0.50 | RonaORAEPubSub | February 2026**

---

## Overview

For **CASH** tender transactions, the system generates up to **3 separate tender records** in RIMTNF:

| Line | TNFFCD | Description | Source Field | Required |
|------|--------|-------------|-------------|----------|
| 1 | **CA** | Cash amount received | `tenders[].amount.value` | **Mandatory** |
| 2 | **CA** | Change returned to customer | `totals.changeDue.value` | Optional (only if non-zero) |
| 3 | **PR** | Penny rounding adjustment | `totals.cashRounding.value` | Optional (only if non-zero) |

---

## Line 2 — Change Returned (changeDue)

### What is changeDue?

The cash change given back to the customer when they pay more than the transaction total.

- **Input source:** `transaction.totals.changeDue.value`
- **When created:** Only when `changeDue` is non-zero and there is a CASH tender in the transaction.

### Field Mapping

| Field | JSON Key | Value | Rule |
|-------|----------|-------|------|
| Fund Code | TNFFCD | **CA** | Always "CA" |
| Amount | TNFAMT | `00000000241` | Absolute value in cents, 11 digits, leading zeros |
| Negative Sign | TNFAMN | **-** | **Always "-"** — money going back to customer |
| Credit Card | TNFCCD | *(blank)* | Always blank for cash |
| Card Exp. | TNFEXP | `0000` | Always "0000" |
| Auth Number | TNFAUT | *(blank)* | Always blank for cash |
| Reference Code | TNFRFC | *(blank)* | Always blank |

### Sign Rule — Change Returned

> **Rule:** If change is returned, the sign is **always "-"** (negative), regardless of the incoming value's sign. Change returned always represents money leaving the register.

### Example

| Scenario | Net Total | Customer Pays | changeDue (ORAE) | TNFAMT | TNFAMN |
|----------|-----------|---------------|-------------------|--------|--------|
| Customer overpays | $22.59 | $25.00 | `"2.41"` | `00000000241` | **-** |
| Exact payment | $22.60 | $22.60 | `"0.00"` | *Record not created (zero)* | |

---

## Line 3 — Penny Rounding (cashRounding)

### What is cashRounding?

Canadian penny rounding adjustment. Canada eliminated the penny in 2013, so **cash transactions** are rounded to the nearest **$0.05**. This adjustment only applies to cash payments — card transactions settle to the exact penny.

- **Input source:** `transaction.totals.cashRounding.value`
- **When created:** Only when `cashRounding` is non-zero and there is a CASH tender in the transaction.

### Rounding Rules (to nearest $0.05)

| Net Total | Rounded To | cashRounding (ORAE) | Meaning |
|-----------|-----------|---------------------|---------|
| $22.51 | $22.50 | `"-0.01"` | Customer saves 1 cent |
| $22.52 | $22.50 | `"-0.02"` | Customer saves 2 cents |
| $22.53 | $22.55 | `"+0.02"` | Customer pays 2 cents extra |
| $22.54 | $22.55 | `"+0.01"` | Customer pays 1 cent extra |
| $22.55 | $22.55 | `"0.00"` | No rounding — record not created |

### Field Mapping

| Field | JSON Key | Value | Rule |
|-------|----------|-------|------|
| Fund Code | TNFFCD | **PR** | Always "PR" |
| Amount | TNFAMT | `00000000002` | Absolute value in cents, 11 digits, leading zeros |
| Negative Sign | TNFAMN | *see inverted sign rule below* | Sign is **inverted** from ORAE value |
| Credit Card | TNFCCD | *(blank)* | Always blank for cash |
| Card Exp. | TNFEXP | `0000` | Always "0000" |
| Auth Number | TNFAUT | *(blank)* | Always blank for cash |
| Reference Code | TNFRFC | *(blank)* | Always blank |

### Sign Rule — Penny Rounding (INVERTED)

> **Rule:** The sign is **inverted** from the ORAE input value:
>
> - ORAE sends **positive** (customer rounds up) → output sign is **"-"**
> - ORAE sends **negative** (customer rounds down) → output sign is **blank**

### Sign Inversion Examples

| ORAE cashRounding | TNFAMT | TNFAMN | Explanation |
|-------------------|--------|--------|-------------|
| `"+0.02"` | `00000000002` | **-** | Customer rounds up → store absorbs rounding |
| `"+0.01"` | `00000000001` | **-** | Customer rounds up → store absorbs rounding |
| `"-0.02"` | `00000000002` | *(blank)* | Customer rounds down → customer absorbs rounding |
| `"-0.01"` | `00000000001` | *(blank)* | Customer rounds down → customer absorbs rounding |

---

## Complete Cash Transaction Example

**Scenario:** Customer buys item for $19.93 + $2.59 HST = $22.52 net. Pays with $25.00 cash.

- Penny rounding: $22.52 → $22.50 (rounds down, cashRounding = `"-0.02"`)
- Change returned: $25.00 - $22.50 = $2.50

### Input JSON (relevant sections)

| Field | Value |
|-------|-------|
| `tenders[0].method` | `"CASH"` |
| `tenders[0].tenderId` | `"CA"` |
| `tenders[0].amount.value` | `"25.00"` |
| `totals.net.value` | `"22.52"` |
| `totals.changeDue.value` | `"2.50"` |
| `totals.cashRounding.value` | `"-0.02"` |

### Output RIMTNF (3 Tender Records)

| # | TNFFCD | TNFAMT | TNFAMN | Description | Sign Rule Applied |
|---|--------|--------|--------|-------------|-------------------|
| 1 | **CA** | `00000002500` | *(blank)* | Cash received ($25.00) | Positive = blank |
| 2 | **CA** | `00000000250` | **-** | Change returned ($2.50) | Always "-" for change |
| 3 | **PR** | `00000000002` | *(blank)* | Penny rounding (-$0.02) | ORAE negative → blank (inverted) |

---

## TNFAMN Sign Rules Summary

| Record Type | Line | Rule |
|-------------|------|------|
| **Sales & Returns** (Line 1 — CA received) | 1 | Blank for positive, "-" for negative (standard) |
| **Change Returned** (Line 2 — CA change) | 2 | **Always "-"** if change is returned |
| **Penny Rounding** (Line 3 — PR) | 3 | **Inverted:** ORAE positive → "-" | ORAE negative → blank |

> **Note:** Card transactions (Visa, Mastercard, Debit, etc.) settle to the exact penny and do not generate changeDue or cashRounding records. These records are exclusive to CASH tenders.
