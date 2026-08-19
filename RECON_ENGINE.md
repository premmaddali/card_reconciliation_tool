# ReconTool — Reconciliation Engine Spec (MasterCard)

The MasterCard reconciliation is **three independent functions**, each its own menu option. Every
function matches one **processor file** against the **bank GL** using a **weighted confidence score**,
then rolls results up **daily and monthly**. The engine is generic: sources, GL scope and weights are
configuration, so other schemes can be added later.

## The three functions

| Function | Processor source | GL scope | Notes |
|---|---|---|---|
| **ATM Recon** | File 1 — ATM settlement | Cardholder-settlement rows where `TRAN_PARTICULAR` starts `CASH` | timestamp differences expected → wider date tolerance |
| **POS Recon** | File 2 — POS settlement | Cardholder-settlement rows where `TRAN_PARTICULAR` starts `POS` | |
| **Bulk Recon** | File 3 — Interchange (all card txns) | All cardholder-settlement rows | superset; credit + debit |

*T464 (File 5) is parked — purpose to be decided later.*

## GL scope — the six accounts (verified present in the sample)

Only these `ACCT_NUM` values are card-reconciliation accounts; everything else in the GL dump is ignored.

| Role | MUR | USD | EUR |
|---|---|---|---|
| **Cardholder Settlement** (per-txn, holds the match key) | 9849065772 | 9849065773 | 9849065774 |
| **Intermediary Settlement** (MasterCard clearing leg) | 9849077911 | 9849077913 | 9849077912 |

Currency of a processor transaction routes it to the matching cardholder account (by
`TRANSACTION_CURRENCY`). Matching happens against the **cardholder-settlement** rows — these carry the
per-transaction detail:

- `TRAN_PARTICULAR_2` = the 12-digit reference number (same domain as processor `REFERENCE_NUMBER`).
- `TRAN_PARTICULAR` = `POS/…` or `CASH/…` (+ `REFUND`, `REVERSAL`, `CREDIT` variants) = channel.
- `TRAN_AMT` = amount in the account currency; `DEBIT`/`CREDIT` = direction.
- `TRAN_DATE` = posting date.

The intermediary accounts are the counter-leg (packed `DDMMYY`+code particular, no clean reference) —
used for the aggregate balance check, not per-transaction matching in v1.

## Weighted confidence match

For each processor transaction P, generate GL candidates and score each pair P↔G. **Amount and
date/time are primary; reference, card and channel add weight.** Weights are configurable (defaults):

| Attribute | Rule | Default weight |
|---|---|---|
| **Amount** | `|P.amount − G.TranAmt| ≤ 0.01` (account currency) | **45** (primary) |
| **Date** | same posting date = full; else linear decay across the tolerance window (ATM wider) | **33** (primary) |
| Reference | `P.ReferenceNumber == G.TranParticular2` | 17 |
| Channel | `POS`/`CASH` prefix matches the function | 5 |
| Card / Time | reserved — GL cardholder rows carry neither, so scored 0 for now | 0 |
| **Total** | | **100** |

Amount + date alone (45 + 33 = **78**) already clear the threshold, so ATM — which rarely carries a
usable reference — still matches on amount and date. Weights and tolerances are configurable via the
`MatchWeights` appsettings section.

- **Confidence = best candidate score (0–100).**
- **Matched** when amount ties **and** confidence ≥ `MatchThreshold` (default **70**), **or** the
  reference is an exact identity match.
- **AmountMismatch** when the reference matches but the amount differs → recorded as a match row *and*
  an exception.
- **Unmatched (processor)** when no candidate is accepted → exception.
- **Unmatched (GL)** — cardholder-settlement rows no processor txn claimed → exception.
- A GL row is consumed by at most one processor txn (greedy by descending confidence).

### Verified on the March sample (2026-08-14)
| Function | Processor rows | Matched | Exceptions | Notes |
|---|---|---|---|---|
| **POS → GL** | 71,641 | 54,623 | 34,301 | most POS tie to GL `POS/` rows |
| **ATM → GL** | 22,987 | 5,742 | 17,331 | only ~6k ATM exist in GL cardholder accounts — the rest is a real finding |

### Candidate generation (keeps it O(n))
Index GL cardholder rows by `(currency, roundedAmount)`. Amount is selective, so each processor txn
yields a small candidate set; score those within the date-tolerance window only.

## Outputs

- **ReconMatch** per processor txn: chosen GL row, confidence, tier, both amounts/dates.
- **ReconException** for every Unmatched/Ambiguous/AmountMismatch, with narrative + analyst workflow.
- **Summaries** per function run, by currency, at **two grains**: daily (`Day` set) and monthly
  (`Day` null) — matched vs exception counts and amounts, plus average confidence.

## Known data realities (from profiling the March sample)
- POS references align well with GL (~90% found); ATM references mostly do **not** (only ~25% in GL by
  reference) and ATM settles in USD — which is exactly why amount+date/time weighting is primary.
- ~91% of POS matches tie to `TRAN_AMT` exactly; the rest are real amount exceptions or FX cases.
- GL `REF_NUM` column is unusable (all `NULL` on these accounts) — the reference lives in
  `TRAN_PARTICULAR_2`.
