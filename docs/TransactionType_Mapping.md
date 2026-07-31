# Transaction Type Mapping Analysis

**PubSubApp v1.0.102 | RonaORAEPubSub | July 2026**

---

> Behaviour documented here that is still awaiting a specification decision from Rona is tracked
> in [Open_Questions.md](Open_Questions.md).

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
| ADJUSTMENT | **11** | Price adjustment — stays 11 on *both* the return and the re-sale line |
| AR_PAYMENT | **43** | |
| VOID | **87** | |
| POST_VOID | **88** | |
| EXCHANGE, NONMERCH, SERVICE, CANCEL | **01** | Default fallback (logs a warning) |

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

### For ADJUSTMENT Transactions (price adjustment)

A price adjustment returns an already-purchased item at its original price and re-sells it at
the adjusted price in the same transaction, so each adjusted SKU produces **two** order lines.
`SLFTTP` is `11` on both; only `SLFLNT` differs.

| Line | SLFLNT | Notes |
|------|--------|-------|
| Return leg (original price) | **11** | Prints per the returns mapping — `SLFQTN`/`SLFEXN` `-`, `SLFADC`/`SLFADP`/`SLFOVR` pinned to zeros |
| Re-sale leg (adjusted price) | **01** | Prints per the sales mapping |
| Either leg, EPP coverage item | **21** | The EPP override wins; the sign fields differentiate the two legs |

The legs are paired on `parentLineId` — the re-sale leg's `parentLineId` is the return leg's
`lineId`, and both carry the same SKU. See *Adjustment Leg Pairing* below.

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

## Adjustment Leg Pairing

On an ADJUSTMENT the two halves of each adjusted SKU are linked by the payload itself: the
**re-sale leg's `parentLineId` is the return leg's `lineId`**, and both ends carry the same SKU.

`parentLineId` is overloaded — on an EPP coverage item it points at the SKU being covered — so a
link only counts as an adjustment pair when **both ends share a SKU**, which an EPP plan and the
item it covers never do. Adjustment re-sale legs are excluded from the EPP parent/child reorder
for the same reason.

Do **not** infer the pairing from SKU alone or from pricing signs:

| Inference | Fails when |
|-----------|------------|
| Match on SKU | One SKU is adjusted twice in a transaction — the pairs cross-contaminate and a pair's two legs end up disagreeing |
| Return leg = negative price | A leg repriced to or from `$0` has no sign, so **both** halves print as sale lines and the refund disappears |
| Return leg = negative quantity | Real captures carry a **positive** quantity on the return leg |

The pricing-sign test survives only as a fallback for an adjustment item that carries no usable
`parentLineId` link.

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

Evaluated **per line**, not per transaction (an ADJUSTMENT carries return and sale lines together).

| Line | SLFQTN | Rule |
|------|--------|------|
| Any line of a RETURN | `-` | Negative quantity for returns |
| ADJUSTMENT return leg | `-` | The return half of a pair |
| ADJUSTMENT re-sale leg | *(blank)* | The sale half of a pair |
| All others | *(blank)* | Positive quantity |

Amount fields themselves stay **positive** on every line; the return is signalled by the sign
columns (`SLFQTN`, `SLFEXN`, `TNFAMN`), never by embedding a `-` in the value.

**Tax lines always leave `SLFQTN` blank**, returns included — a tax line has no quantity to sign
(`SLFQTY` is the constant `000000100`), so the direction rides on `SLFEXN` alone. The eco-fee
`83` line is *not* a tax line for this purpose: it carries a real SKU and follows the SKU-line
rule above.

### SLFSLN — Sell Price Negative Sign

**Always blank.** Not on tax lines (v1.0.96) and not on SKU lines either (v1.0.102). ORAE sign
conventions vary between captures — the no-receipt return capture sends a negative `unitPrice`
where the with-receipt one sends positive — so a computed sign leaked a `-` into this field on
some returns and not others. `SLFQTN` and `SLFEXN` carry the direction.

### SLFRSN — Reason Code (16 chars, right-padded)

| Condition | SLFRSN | Notes |
|-----------|--------|-------|
| transactionType = RETURN | `RRT0` + `items[n].return.reason` | e.g. `RRT00204` |
| transactionType = ADJUSTMENT | `POV0` + `items[n].return.reason` | e.g. `POV01502` — on **both** legs of the pair; a leg with no reason of its own takes its partner's |
| transactionType = VOID | `VOD0` | Void reason |
| priceVehicle = "OVD:OVR" | `POV0` + override reason | e.g. `POV01504` |
| priceVehicleCode = "MAN" | `IDS0` + override reason | Manual discount |
| None of above | 16 spaces | Default blank |

⚠️ The field is a fixed 16 and the reason is payload-supplied, so the result is **truncated**, not
just padded. An over-long value would fail `RecordSetValidator`, and the production subscriber
ACKs and drops a failing message — the whole transaction would be lost rather than retried.

### SLFOTS/SLFOTD/SLFOTR/SLFOTT — Original Transaction Fields

These identify the **original sale** being returned against, sourced from
`references.originalEvent` — not from the current event.

| SLFTTP | SLFOTS | SLFOTD | SLFOTR | SLFOTT |
|--------|--------|--------|--------|--------|
| 01, 04, 43 (Sale/Employee/AR) | `00000` | `000000` | `000` | `00000` |
| 11, `RETURN_WITH_RECEIPT` | `originalEvent.storeId` | `originalEvent.businessDay` | `originalEvent.registerId` | `originalEvent.sequenceNumber` |
| 11, `RETURN_NO_RECEIPT` | `00000` | `000000` | `000` | `00000` |
| 87, 88 (Void/Post-Void) | Store ID | Transaction date | Register ID | Sequence number |

`SLFOST` (Original Store) follows the same rule: `originalEvent.storeId` on a with-receipt
return, `00000` otherwise. **Tax lines always print zeros** for all five fields except on
VOID/POST-VOID, which keep the current-event values.

---

## SLFTX1–SLFTX4 — Charged-Tax Flags (SKU Lines)

**Method:** `ParseItemTaxes()`

Set from each tax's **own** `jurisdiction.region`, not the store's province — the same principle as
the tax-line bucketing below. Keying off the store sent a Quebec purchase returned at an Ontario
store through the Ontario rate split, setting `SLFTX4` where `SLFTX1`/`SLFTX2` belonged.

| Tax jurisdiction | Flag | Meaning |
|------------------|------|---------|
| `PQ`, `BC`, `MB`, `SK` | `SLFTX1` | Provincial sales tax (PST/QST) |
| `FED` | `SLFTX2` | Federal GST |
| `HON` | `SLFTX3` | Ontario full HST (13%) |
| `HON1` | `SLFTX4` | Ontario partial HST (5%) |
| `HNB`, `HNF`, `HNS`, `HPE` | `SLFTX1` + `SLFTX2` | Atlantic HST (harmonized) |

Unset flags print `N`, never blank. Notes:

- **Region-less taxes keep the historical heuristics** (rate split in ON, tax-type switch
  elsewhere). This is deliberate: the flag path and the tax-line path disagreed on the ambiguous
  case — the line defaulted to `HON1`, the flag to full HST — so routing everything through one
  classifier would silently flip `SLFTX3`→`SLFTX4` on existing Ontario output.
- **A zero tax amount is skipped; a negative one is not.** Return taxes are negative but *were*
  charged on the original sale, so they must still set their flag.
- **`taxExempt = true` entries are skipped** entirely.
- **First Nation partial exemption** overrides `SLFTX3` to `"O"` when
  `transaction.qualifiers.isTaxExemptTransaction` is set and a tax entry carries `status="A"`.
  Applied after the loop, so it wins over the jurisdiction routing.
- **Tax lines themselves always carry `SLFTX1-4 = N`** — the flags describe the SKU.

---

## Tax Line Type Mapping (SLFLNT for Tax Records)

**Methods:** `ClassifyTaxBucket()` then `MapTaxAuthToLineType()`

Each item tax is bucketed by **its own `jurisdiction.region`**, not by the store's province, and
one aggregate line is emitted per bucket. This matters for cross-region transactions — an Ontario
store refunding a Quebec purchase carries `FED`/`PQ` taxes that must print as `XG`/`XQ` under
their own authorities, not collapse into the store province's `XI`/`HON1`.

A tax with no recognised region falls back to the store province's historical heuristics (rate
split in ON, type/rate split in GST+PST provinces, single GST line in AB), so same-region
behaviour is unchanged. Note the precedence: **region wins over rate**, the reverse of the
pre-v1.0.98 logic, which matters only for a payload whose region and rate disagree.

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
| `AdjustmentPairing.Build()` | ADJUSTMENT items linked by `parentLineId` sharing a SKU | Identifies each pair's return vs re-sale leg |
| `IsReturnLine()` | Whole transaction is a RETURN, or this item is an adjustment return leg | Per-line return treatment (signs, pinned price fields) |

---

## Complete Mapping Flow

```
Input: transaction.transactionType
  |
  ├─ HasEmployeeDiscount() ──► bool
  ├─ HasGiftCardTender()   ──► bool
  ├─ GetCustomerId()       ──► bool
  |
  ├─► AdjustmentPairing.Build()  (ADJUSTMENT only)
  |     └─► return leg / re-sale leg per adjusted SKU
  |
  ├─► MapTransTypeSLFTTP(type, empDiscount)
  |     └─► SLFTTP (01, 04, 11, 43, 87, 88)   [ADJUSTMENT → 11 on every line]
  |           └─► TNFTTP (same value)
  |                 └─► TNFESI ("#####" if 04)
  |
  ├─► MapTransTypeSLFLNT(type, empDiscount, giftCard, customerId)
  |     └─► SLFLNT (01, 02, 04, 11, 12, 45, 87)
  |           └─► ADJUSTMENT re-sale leg overridden to 01
  |
  ├─► Per item: EPP override check
  |     └─► If EPP="9": SLFTTP=21, SLFLNT=21
  |
  ├─► Per line: IsReturnLine()
  |     └─► SLFQTN/SLFEXN "-", SLFADC/SLFADP/SLFOVR pinned to zeros
  |
  ├─► SLFRSN (type + priceVehicle + return.reason, truncated to 16)
  └─► SLFOTS/OTD/OTR/OTT (zeros for sale; references.originalEvent for a
        with-receipt return; zeros for no-receipt; zeros on tax lines)
```
