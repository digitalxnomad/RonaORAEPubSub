# Open Questions for Rona

**PubSubApp v1.0.110 | RonaORAEPubSub | September 2026**

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

## 3. `SLFTE2` (AB) and `SLFTE1` (BC) are specified as mandatory, but no payload carries the source

**Raised by:** MIM-10984 implementation, v1.0.109

Two fields are specified "cannot be blank", and in both cases the source field is absent from every
capture supplied with the ticket:

| Field | Province | Specified source | In the captures |
|-------|----------|------------------|-----------------|
| `SLFTE2` | AB | `extensions.x-tax-exemption-band` | absent — AB 344 carries only `x-tax-exemption-level` |
| `SLFTE1` | BC | `transaction.taxExemption.certificateId` | absent — BC 0234, 0240 and the wholesaler capture carry `reason`, `authority` and `program` only |

The mapping is implemented and both fields fill the moment the source appears; today they emit
blank. Test cases AB-03 and BC-07 therefore cannot pass on the supplied data.

`bc_exempt_first_nation_synthetic` carries an invented `certificateId` purely so the populated BC
path is covered at all. Two rows of the per-province `SLFTE` table remain unexercised by any
payload, because no QC, BC or AB capture carries `x-tax-exemption-band` — only the Ontario one
does — so "AB takes the band" and "QC/BC blank the band" are both asserted by code and documented
here, but proved by nothing. QC's blanking of `SLFTEN` *is* exercised: that capture carries a
customer name and the baseline shows the field empty.

**BC-07 already anticipates this** — *"TE1 = `certificateId` if present; if Atreya confirms → else
`FIN 490`"*. So there is a proposed constant fallback awaiting confirmation.

**The questions:**

- **(a)** Is `FIN 490` confirmed as the BC `SLFTE1` fallback when no `certificateId` is present, and
  does it apply to QC as well? Nothing has been hard-coded, because a literal in a customer-facing
  identifier field is not something to guess at.
- **(b)** Is AB expected to start sending `x-tax-exemption-band`, or does `SLFTE2` need a fallback
  too? AB is the only province where `SLFTE2` is the sole identifier, so a blank leaves the
  exemption unattributed.

---

## 4. MB and SK have no tax-exemption specification

**Raised by:** MIM-10984 implementation, v1.0.109

MIM-10984 covers QC, AB and BC. Before v1.0.109 every non-Ontario province printed `SLFTX3="O"`.
That rule now applies to Ontario, the Atlantic HST provinces and any unrecognised `taxArea`, so MB
and SK had to land on one side or the other.

They follow the BC rule — a waived PST marks `SLFTX1` with `"O"` or `"E"` — **on the inference
that they are structurally identical to BC** (GST + provincial PST, same `jurisdiction.region`
shape). No capture exists and no ticket says so.

The marker scheme was scoped to exactly QC/BC/AB/MB/SK rather than "everything except Ontario".
The Atlantic HST provinces and any unrecognised `taxArea` keep `SLFTX3="O"`, because the marker
switch covers only the `FED` and provincial-PST buckets — an Atlantic exemption routed through it
would set no flag at all and the exemption would disappear from the record.

> ⚠️ **Unlike everything else in Part A, this behaviour is not test-covered.** No MB or SK
> exemption capture exists, so no baseline pins it — the suite would not notice if it changed. It
> is the one entry here that rests on reasoning alone.

**The question:** is that right, or do MB and SK have their own treatment? The alternative
considered was to leave them on the old `SLFTX3="O"`, which was rejected as incoherent once their
structural twin moved away from it.

---

## 5. Web Tendering — `SLFRFD` has two identical sources, and only one is specified

**Raised by:** MIM-10971 implementation, v1.0.110

`SLFRFD` is read from `transaction.tenders[0].card.emv.tags.invoiceNumber`, as the ticket specifies.
But every Web Tendering item **also** carries the same 15 digits in `item.altIds` as `sodaRef`:

| Capture | `sodaRef` | `invoiceNumber` |
|---------|-----------|-----------------|
| all 19 Web Tendering captures | *(15 digits)* | identical, in every one |
| `soda_zeroship_refund` (SODA, not WT) | `005010240354401` | absent — SODA does not use the EMV tag |

The two are indistinguishable on the supplied data, so the choice is untestable. It matters in one
case: **a Web Tendering payload that omits the EMV tag would emit a blank `SLFRFD`** even though
`sodaRef` was sitting on the item. The SODA branch already reads `sodaRef` for exactly this field,
and the SODA capture proves the tag can be absent on a line type `30` record.

No fallback was added — the ticket names one source, and inventing a second is the kind of guess
that has been wrong before here.

**The question:** should `SLFRFD` fall back to `altIds` `sodaRef` when the tender carries no
`invoiceNumber`, or is the EMV tag guaranteed present on every Web Tendering transaction?

---

## 6. Web Tendering — three supplied fixture folders are empty, and no capture mixes line types

**Raised by:** MIM-10971 implementation, v1.0.110

**(a) Empty fixtures.** Three folders in `examples.zip` contain only 0-byte files:
`scenario return-3`, `scenario return-5` and `scenario-9-WT-return`. Whatever they were meant to
cover is unknown. `scenario-9-WT-return` in particular reads like a Web Tendering return variant not
covered by `scenario return-1`, `-2` or `-4`, which are behaviourally identical to each other.

**(b) No mixed cart.** All 20 runnable captures are a single Web Tendering item with a single `PL`
tender. Nothing exercises a Web Tendering line alongside ordinary merchandise in one transaction.
The branch is an `else if`, so a non-WT item in the same cart *should* keep its normal mapping, but
nothing proves it.

A synthetic was **not** built for (b), unlike the BC `FIRST_NATION` case in v1.0.109. The difference:
there the behaviour was a named requirement with no payload, so the synthetic encoded a stated rule.
Here nothing states that a mixed Web Tendering cart is even a real flow, and inventing one would
freeze a guess about the business process as a baseline.

**The questions:** can the three empty fixtures be re-exported, and can a Web Tendering transaction
ever contain non-Web-Tendering merchandise lines?

---

# Part B — Blocking unstarted work

*(none open — the Endless Aisle blockers were answered 08/12/26; see Resolved)*

---

## Resolved

| Question | Answer | Version |
|----------|--------|---------|
| Should multiple promo GC activations emit one `PP` per card? | No — one aggregate `PP` per transaction carrying the summed promo value, alongside one `PC` per card | v1.0.95 |
| Cross-region `SLFACD`/`SLFTCD` on **tax** lines (flagged in the returns mapping document) | Bucket each tax by its own `jurisdiction.region` | v1.0.98 |
| `SLFTX4` on a cross-region return — `N` or a literal blank? (*Tactill \| ACO \| ECO Fee*, receipts 2136 → 4839) | **`N`** — confirmed 07/31/26. `<BLANK>` in the ticket meant "not `Y`". No code change: unset charged-tax flags have always printed `N`, and the return already matched its original QC sale exactly. That ticket is fully satisfied by v1.0.101 | v1.0.101 (no change needed) |
| Endless Aisle `SLFRFD` — 15-character value against a 16-character field (CR *RONA TSP Mapping Changes*, MIM-7509 / MIM-8070) | **15 digits + one trailing space**, i.e. `PadOrTruncate(storeId + rightmost-10 sodaRef, 16)`. Confirmed by Grace 08/12/26; the CR's length column was the error. Matches how the SODA branch already fills this field | v1.0.103 |
| Endless Aisle line type — `altIds sodaType` or `lineBusiness.detailType`? | **`altIds` `sodaType == "ENDLESS_AISLE"`**, per the CR. Confirmed by Grace 08/12/26: keeps detection consistent with every other flow, and avoids depending on `detailType`, which was introduced for Endless Aisle only. `lineBusiness.detailType` is **deliberately ignored** — a `sodaType=ENDLESS_AISLE` line emits `SLFLNT=42` regardless of what `detailType` says | v1.0.103 |
| MIM-10984 §1 — "SLFTX3 & SLFTX4 are always `N`" for QC/AB/BC, yet Ontario uses `SLFTX3="O"` for the same thing | **Both, by province.** Ontario keeps `SLFTX3="O"` (MIM-10106, in production); QC/AB/BC mark the waived tax on `SLFTX1`/`SLFTX2` and leave `SLFTX3`/`SLFTX4` at `N`. Confirmed by the MIM-10984 test cases (BC-08, QC-07, SH-01) | v1.0.109 |
| MIM-10984 §3.3/§3.4/§3.5 — three province-specific flag tables; is each a separate rule? | **No, one rule.** The waived tax's own `jurisdiction.region` picks the flag; `taxExemption.program` picks the letter (`FIRST_NATION`/`FIRST_NATION_PARTIAL` → `"O"`, anything else → `"E"`). All five captures and all documented cases fall out of it | v1.0.109 |
| MIM-10984 §2 — field named `isTaxExemptionTransaction` | **ORAE sends `isTaxExemptTransaction`** — confirmed again by all seven new exempt captures (six real, one synthetic). Third ticket carrying the wrong spelling; the model comment in `OraeModels.cs` records it | v1.0.109 (no change needed) |
| MIM-10984 §3.2 vs §3.5 — `SLFTE1`/`SLFTE2` requirements contradict between QC/BC and AB | **Not a contradiction — the sourcing is per province.** QC/BC identify the exemption by certificate, AB by band, and neither reports the customer name. Implemented as a three-row table rather than filling all three fields from whatever the payload carries | v1.0.109 |
