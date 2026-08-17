# Open Questions for Rona

**PubSubApp v1.0.102 | RonaORAEPubSub | July 2026**

Decisions only Rona can make. Each entry states the evidence and what the answer would change, so
it can be actioned without re-deriving any of it. Two kinds:

- **Part A — shipped behaviour, ambiguous spec.** Already live and test-covered; the current
  behaviour is a judgement call. None of these is a known defect.
- **Part B — blocking unstarted work.** A specification conflict that must be settled *before*
  the work can be built correctly.

When one is resolved, change the code, regenerate the affected baseline, and move the entry to
**Resolved** with the answer recorded.

---

# Part A — Shipped behaviour, ambiguous spec

---

## 1. Eco-fee `SLFACD` / `SLFTCD` — the payload's `authority` and `code` disagree between regions

**Raised by:** review of the `83` eco-fee line

TTree maps the fee object positionally — `fee.authority → SLFACD`, `fee.code → SLFTCD`, with
`SLFACD` falling back to the item's tax `jurisdiction.region` when absent. Nothing interprets the
values. The three real captures then produce three different shapes:

| Capture | Store | Payload | `SLFACD` | `SLFTCD` |
|---------|-------|---------|----------|----------|
| `GC Activation/tx00095_multiitem` | SK | `authority:"SK"`, `code:"1630"` | `SK` | `1630` |
| `Returns/adjustment_qc_ecofee` | QC 41100 | `authority:"22"`, `code:"PQ"` | `22` | `PQ` |
| `Cross Region/return_on_from_qc_4839` | ON 55010 | neither present | `FED` *(fallback)* | *(blank)* |

**The Saskatchewan and Quebec payloads are inverted relative to each other.** `PQ` is a
jurisdiction and `1630`/`22` look like fee codes, so on the SK capture the jurisdiction lands in
`SLFACD`, and on the QC capture it lands in `SLFTCD`.

**Two pieces of internal evidence say `SLFACD` is meant to hold a jurisdiction:**

1. **Its own fallback is one.** When `authority` is absent, TTree substitutes
   `tax.jurisdiction.region` — which is why 4839 prints `FED`. The field's default is a
   jurisdiction, but the QC payload fills it with `22`.
2. **Tax lines on the same transaction** print `SLFACD` = `FED`/`PQ`/`HON` and `SLFTCD` =
   `GST`/`PST`/`HST` — jurisdiction in `ACD`, rate code in `TCD`. The QC eco-fee line inverts that.

By that convention the QC eco-fee line should read `SLFACD=PQ`, `SLFTCD=22`.

**The question:** which is it?

- **(a) ORAE's QC feed has the two fields transposed at the producer**, and TTree should normalise
  — e.g. if `authority` is not a recognised jurisdiction but `code` is, swap them.
- **(b) Quebec's eco-fee programme legitimately uses `22` as the authority** and `PQ` as the code,
  and the two provinces differ by design.

No normalisation has been implemented, because under reading (b) it would corrupt every QC
eco-fee line. A rule is only safe once the answer is known.

> ⚠️ `samples/Returns/output_adjustment_qc_ecofee.json` currently **freezes `22`/`PQ` as expected
> output**. That baseline records what the mapper does, not what is correct. If the answer is (a),
> the fix is small and the baseline updates with it.

**Related, already decided:** when one leg of an adjustment pair omits `authority`/`code` and its
partner carries them, the missing values are borrowed so the two `83` lines match (AC3). Only
missing values are filled — a fee stating its own code keeps it.

---

## 2. `TNFAUT` for a gift card activation of $10,000.00 or more

**Raised by:** code review, v1.0.99

`TNFAUT` is a fixed 6 digits holding the activation value in cents, so it cannot represent
$10,000.00 or more. Before v1.0.99 the over-long value failed `RecordSetValidator` and the
production subscriber **ACKed and dropped the message** — the whole transaction was lost.

Today the value is **clamped to `999999` with an Error-level log** naming the true amount, and
`TNFRDS` on the same line still carries the true value (e.g. `00000012500.00 A`). Nothing is lost,
but the emitted `TNFAUT` is deliberately wrong.

**The question:** what *should* a ≥ $9,999.99 activation put in `TNFAUT`? Clamping was chosen only
because it is less bad than discarding the transaction. No real capture has approached the limit.

---

# Part B — Blocking unstarted work

---

## 3. Endless Aisle: `SLFRFD` is specified one character short 🔴

**Raised by:** CR *RONA TSP Mapping Changes* — Endless Aisle (MIM-7509 / MIM-8070)

The CR defines `SLFRFD` on an EA line as **storeId (5) + rightmost 10 digits of `sodaRef`**, and
its own table gives the field length as **16**. Those disagree — the value is 15:

| Input (from the supplied `Returns Aug 14` capture) | Value |
|---|---|
| `businessContext.store.storeId` | `55010` (5) |
| `altIds` `sodaRef` | `005012225556668` (15) → rightmost 10 = `2225556668` |
| Concatenated `SLFRFD` | `550102225556668` — **15 characters** |

`OrderRecord.ReferenceDesc` is `[StringLength(16, MinimumLength = 16)]`. A 15-character value
**fails `RecordSetValidator`**, and the production subscriber logs the error then returns
`Reply.Ack` — the message is acknowledged and never published, so **the whole transaction is
lost rather than retried**.

**The question:** is the 16th character a trailing space, or is a digit missing from the formula
(6-digit store? 11 digits of `sodaRef`)?

The existing SODA branch pads to 16 with a trailing space (`PadOrTruncate(sodaRefId, 16)`), which
is almost certainly the intent — but padding a financial reference field is not something to
assume. **Not implemented pending this answer.**

---

## 4. Endless Aisle: two competing sources for the line type

**Raised by:** review of the supplied EA captures

The CR specifies EA detection via `item.altIds` entry `type="sodaType"`, `value="ENDLESS_AISLE"`
→ `SLFLNT = 42`. But both supplied captures *also* carry the line type directly on the item:

```json
"lineBusiness": { "detailType": "42" }
```

The CR never mentions `lineBusiness.detailType`. Implementing to the CR means keying off
`sodaType` and ignoring a field that states the answer outright.

**The question:** which source is authoritative, and what should happen if they ever disagree —
for example a `sodaType=ENDLESS_AISLE` item whose `detailType` is not `42`?

---

## Resolved

| Question | Answer | Version |
|----------|--------|---------|
| Should multiple promo GC activations emit one `PP` per card? | No — one aggregate `PP` per transaction carrying the summed promo value, alongside one `PC` per card | v1.0.95 |
| Cross-region `SLFACD`/`SLFTCD` on **tax** lines (flagged in the returns mapping document) | Bucket each tax by its own `jurisdiction.region` | v1.0.98 |
| `SLFTX4` on a cross-region return — `N` or a literal blank? (*Tactill \| ACO \| ECO Fee*, receipts 2136 → 4839) | **`N`** — confirmed 07/31/26. `<BLANK>` in the ticket meant "not `Y`". No code change: unset charged-tax flags have always printed `N`, and the return already matched its original QC sale exactly. That ticket is fully satisfied by v1.0.101 | v1.0.101 (no change needed) |
