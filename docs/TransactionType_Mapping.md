# Transaction Type Mapping Analysis

**PubSubApp v1.0.50 | RonaORAEPubSub | February 2026**

---

## Overview

The incoming `transaction.transactionType` drives two key output fields (**SLFTTP** and **SLFLNT**) plus several conditional behaviors across order, tax, and tender records.

### Valid Incoming Transaction Types

| transactionType | Description |
|-----------------|-------------|
| SALE | Regular purchase transaction |
| RETURN | Return of merchandise |
| EXCHANGE | Exchange transaction |
| VOID | Current transaction void |
| CANCEL | Cancel transaction |
| ADJUSTMENT | Adjustment transaction |
| NONMERCH | Non-merchandise transaction |
| SERVICE | Service transaction |

**Special cases:** CANCEL and VOID transactions are allowed to have zero items.

---

## SLFTTP — Order Transaction Type

**Method:** `MapTransTypeSLFTTP()`

| Incoming transactionType | SLFTTP | Condition |
|--------------------------|--------|-----------|
| SALE | **04** | Any item priceVehicle contains "EMP" (employee discount) |
| SALE | **01** | Default (no employee discount) |
| RETURN | **11** | |
| AR_PAYMENT | **43** | |
| VOID | **87** | |
| POST_VOID | **88** | |
| EXCHANGE, ADJUSTMENT, NONMERCH, SERVICE, CANCEL | **01** | Default fallback |

---

## SLFLNT — Line Type

**Method:** `MapTransTypeSLFLNT()` — uses 3 boolean flags in priority order.

### For SALE Transactions

| Priority | Condition | SLFLNT |
|----------|-----------|--------|
| 1 | Employee discount (priceVehicle contains "EMP") | **04** |
| 2 | Gift card tender present | **45** |
| 3 | Customer ID exists | **02** |
| 4 | Default | **01** |

### For RETURN Transactions

| Priority | Condition | SLFLNT |
|----------|-----------|--------|
| 1 | Gift card tender present | **45** |
| 2 | Customer ID exists | **12** |
| 3 | Default | **11** |

### Other Transaction Types

| transactionType | SLFLNT | Notes |
|-----------------|--------|-------|
| VOID | **87** | Same as SLFTTP |
| POST_VOID | **01** | Note: SLFTTP is 88, but SLFLNT is 01 |
| Everything else | **01** | Default |

---

## EPP Coverage Override (Item-Level)

If an item has attribute `x-epp-coverage-identifier = "9"`, **both SLFTTP and SLFLNT are overridden to "21"** for that specific item record only. Other items in the same transaction are unaffected.

---

## TNFTTP — Tender Transaction Type

Set to the **same value as SLFTTP** — passed directly from `mappedTransactionTypeSLFTTP` into `CreateBaseTenderRecord()`.

| SLFTTP | TNFTTP | Description |
|--------|--------|-------------|
| 01 | 01 | Regular sale |
| 04 | 04 | Employee sale |
| 11 | 11 | Return |
| 43 | 43 | AR Payment |
| 87 | 87 | Void |
| 88 | 88 | Post-void |

---

## Transaction-Type-Dependent Fields

### TNFESI — Employee Sale ID

| TNFTTP | TNFESI | Rule |
|--------|--------|------|
| 04 | `#####` | 5 hash marks for employee sales |
| All others | *(blank)* | Empty |

### SLFQTN — Quantity Negative Sign

| transactionType | SLFQTN | Rule |
|-----------------|--------|------|
| RETURN | `-` | Negative quantity for returns |
| All others | *(blank)* | Positive quantity |

### SLFRSN — Reason Code (16 chars, right-padded)

| Condition | SLFRSN | Notes |
|-----------|--------|-------|
| transactionType = RETURN | `RRT0` | Return reason |
| transactionType = VOID | `VOD0` | Void reason |
| priceVehicle = "OVD:OVR" | `POV0` + override reason | e.g. `POV01504` |
| priceVehicleCode = "MAN" | `IDS0` | Manual discount |
| None of above | 16 spaces | Default blank |

### SLFOTS/SLFOTD/SLFOTR/SLFOTT — Original Transaction Fields

| SLFTTP | SLFOTS | SLFOTD | SLFOTR | SLFOTT |
|--------|--------|--------|--------|--------|
| 01, 04, 43 (Sale/Employee/AR) | `00000` | `000000` | `000` | `00000` |
| 11 (Return) | Store ID | Transaction date | Register ID | Source transaction ID |
| 87, 88 (Void/Post-Void) | Store ID | Transaction date | Register ID | Sequence number |

---

## Tax Line Type Mapping (SLFLNT for Tax Records)

**Method:** `MapTaxAuthToLineType()`

| Tax Authority Code | SLFLNT | Description |
|--------------------|--------|-------------|
| BC | XR | BC PST |
| FED | XG | Goods and Services Tax (GST) |
| HNB | XN | HST New Brunswick |
| HNF | XF | HST Newfoundland |
| HNS | XV | HST Nova Scotia |
| HON | **XH** | HST Ontario |
| HON1 | XI | HST Partial Ontario |
| HPE | XP | HST Prince Edward Island |
| MB | XM | Manitoba PST |
| PQ | XQ | Quebec Provincial Tax |
| SK | XS | Saskatchewan PST |
| Default | XH | Falls back to HST Ontario |

---

## Detection Methods

| Method | What It Checks | Impact |
|--------|---------------|--------|
| `HasEmployeeDiscount()` | Any item priceVehicle contains "EMP" | SLFTTP → 04, SLFLNT → 04 |
| `HasGiftCardTender()` | Any tender method = "GIFT_CARD" | SLFLNT → 45 |
| `GetCustomerId()` | Customer ID token exists | SLFLNT → 02 (sale) or 12 (return) |
| `GetEPPCoverageIdentifier()` | Item attribute `x-epp-coverage-identifier` = "9" | Both SLFTTP & SLFLNT → 21 per item |

---

## Complete Mapping Flow

```
Input: transaction.transactionType
  |
  ├─ HasEmployeeDiscount() ──► bool
  ├─ HasGiftCardTender()   ──► bool
  ├─ GetCustomerId()       ──► bool
  |
  ├─► MapTransTypeSLFTTP(type, empDiscount)
  |     └─► SLFTTP (01, 04, 11, 43, 87, 88)
  |           └─► TNFTTP (same value)
  |                 └─► TNFESI ("#####" if 04)
  |
  ├─► MapTransTypeSLFLNT(type, empDiscount, giftCard, customerId)
  |     └─► SLFLNT (01, 02, 04, 11, 12, 45, 87)
  |
  ├─► Per item: EPP override check
  |     └─► If EPP="9": SLFTTP=21, SLFLNT=21
  |
  ├─► SLFRSN (based on type + priceVehicle)
  ├─► SLFQTN ("-" for RETURN)
  └─► SLFOTS/OTD/OTR/OTT (zeros for sale, populated for return/void)
```
