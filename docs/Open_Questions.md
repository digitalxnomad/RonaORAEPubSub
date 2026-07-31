# Open Questions for Rona

**PubSubApp v1.0.101 | RonaORAEPubSub | July 2026**

Behaviour that is **shipped and test-covered**, but where the *specification* is ambiguous or the
source data is inconsistent. Each entry states what TTree does today, why, and what a decision
would change. None of these is a known defect — they are places where the current behaviour is a
judgement call that only Rona can confirm.

When one is resolved, change the code, regenerate the affected baseline, and delete the entry.

---

## 1. `SLFTX4` on a cross-region return — `N` or literal blank?

**Raised by:** *Tactill | ACO | ECO Fee — cross-region return* (receipts 2136 → 4839)

The ticket's expectation reads:

> for Seq-1-SKU the `SLFTX1 = Y`, `SLFTX2 = Y` & `SLFTX4 = <BLANK>`

`SLFTX1` and `SLFTX2` were genuinely wrong and are fixed in v1.0.101. The `SLFTX4` half is the
open question: the field emits **`N`**, not a blank.

**Why `N` was delivered:**

| Evidence | Value |
|----------|-------|
| Return 4839 (after fix) | `SLFTX1=Y SLFTX2=Y SLFTX3=N SLFTX4=N` |
| Original QC sale 2136 — the transaction being reversed | `SLFTX1=Y SLFTX2=Y SLFTX3=N SLFTX4=N` |
| Every other transaction in `samples/` | Unset flags print `N` |

An unset charged-tax flag has printed `N` everywhere since the field existed; no code path emits a
blank. The return now matches its own original sale exactly, which is the outcome the ticket was
driving at.

**The question:** is `<BLANK>` in the ticket shorthand for "not `Y`" (satisfied — this is closed),
or is a literal space genuinely required in the RIM record?

**If a literal blank is required**, it is not a cross-region fix — it changes the meaning of "not
charged" for **all four flags on every transaction type**, and every baseline in `samples/` would
move. That is a spec change, not a bug fix, and needs confirming before anyone relies on it.

> The only non-`Y`/`N` value the field takes today is `SLFTX3 = "O"`, the First Nation partial
> exemption marker (v1.0.90).

---

## 2. Eco-fee `SLFACD` / `SLFTCD` — the payload's `authority` and `code` disagree between regions

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

## 3. `TNFAUT` for a gift card activation of $10,000.00 or more

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

## Resolved

| Question | Answer | Version |
|----------|--------|---------|
| Should multiple promo GC activations emit one `PP` per card? | No — one aggregate `PP` per transaction carrying the summed promo value, alongside one `PC` per card | v1.0.95 |
| Cross-region `SLFACD`/`SLFTCD` on **tax** lines (flagged in the returns mapping document) | Bucket each tax by its own `jurisdiction.region` | v1.0.98 |
