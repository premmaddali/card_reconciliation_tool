# ReconTool — Reconciliation Engine Spec (MasterCard)

## Re-run analysis from stored imports (2026-10-20)

The Bulk/Daily run-detail action **Re-run analysis (all stages)** reads the already
staged TRX and GL, creates a new `ReconRun` linked by `AnalysisOfRunId`, then runs the
current primary engine, Date/Amount/Currency, Card Number, and Previous/Next Period
passes in that order. Existing pass flags belong to the original run and do not
suppress the new run's stages. Original matches, comments, verification and freeze
state are preserved, including when the original is archived or already verified.
Historical import date labels are not revalidated as new uploads; the original
source associations and displayed reconciliation range are retained.

Each stage is recorded in `ReconRunPass`; missing card identities, missing GL value
dates, or unavailable adjacent GL are explicitly marked **Skipped** with a reason.
Unexpected failures mark the analysis failed and leave the original untouched.
All analysis copies use native staging IDs, so already-projected BankOne imports
can also use the secondary passes without re-uploading their files.

Daily date-range replays keep their original range, and consolidated-month replays
read all the original child imports. Analysis results do not join the operational
daily pool, orphan sweep, active batch lists, dashboards or exception overview and
never reserve GL for operational runs. They cannot be submitted, frozen, archived
as an operational batch, or used to create manual GL. Access requires an active
user with Bulk view and add/edit permission. Stored source rows must still exist.

The additive `StoredSourceReanalysis` migration adds the nullable self-reference on
`ReconRuns`. The run detail lists recent analyses with links back to the original.

## Credit-card net billing amount (2026-10-20)

For product type `CR`, the GL comparison amount is `BILLING AMOUNT - ISS MARKUP AMOUNT`
in billing currency. Subtraction uses the signed source values, then the existing GL
matcher compares magnitudes. A blank ISS markup is zero; missing billing leaves the
credit transaction unmatched rather than substituting transaction/settlement amount.
Debit products keep their existing calculation.

The same calculation applies to Bulk/Daily primary matching, secondary/card/adjacent
passes, Daily cross-run matching, the long-outstanding sweep, and review candidates.
Normalized BankOne batches read the original staging billing/ISS values, including
already-imported batches. Source amounts remain intact and `ReconciliationAmount`
stores the net amount; matching evidence records the amount basis. Stored historical
matches are not automatically recalculated.

## ATM withdrawal date/amount rule (2026-10-19)

Bulk primary matching (including the primary pass of Daily runs), Bulk date/amount
secondary passes and Daily cross-run matching pair outstanding ATM withdrawals using
the calendar portion of the TRX transaction timestamp against GL `VALUE_DATE`.
The TRX processing date and time of day do not participate in this rule; the original
timestamp remains available for audit.

Withdrawals are identified by transaction code `04` or wording containing `WITHDRAWAL`,
and must match a `CASH` GL entry with the exact reconciliation amount, currency and
settlement leg. Repeated same-day amounts may pair in any order, consuming each GL
entry once. References and card identities are not required by this withdrawal rule.
Unique-reference matching still runs first. Other transactions retain the normal
ambiguity checks, and the unbounded orphan sweep retains its reference requirement.

## Complementary card evidence (2026-10-07)

### Separate card-number pass (2026-10-12)

Completed, unsubmitted native Bulk results expose **Match with Card Number**. This can run after the initial or Date/Amount/Cur pass. It copies the source results into a linked run (`CardOfRunId`), preserves existing assignments, and evaluates only unassigned unmatched/ambiguous interchange rows. Both sides must supply the same full card number, exact billing amount/currency and settlement leg, with transaction date against GL value date within two days. Supplied reference conflicts are excluded. Every added assignment must be unique on both sides across the whole date window; iteration order cannot resolve duplicate candidates. Added matches carry the `Card Number/Date/Amount/Cur` evidence label and a rule score of 95 (not a calibrated probability).

Card-pass lineage has its own unique source index so it does not replace or collide with Date/Amount/Cur lineage. Repeated clicks return the existing child. Period, permission, frozen/archived/verification, native-source and GL-reservation safeguards apply. Missing card coverage returns an import instruction without saving a child run. Existing primary card corroboration and all older matching buttons remain available.

- BankOne TMS `CARD_ACCOUNT_ENTITY_ID` is treated as the full card identifier for this customer. GL `TRAN_RMKS` is accepted only when its entire value is a complete 13–19 digit identifier (spaces and hyphens allowed). Masked identifiers, embedded narratives, scientific notation and numeric Excel source cells do not establish card identity. Export these columns as text to preserve all digits.
- Existing reference, amount/date, currency, settlement-leg and secondary rules remain. Exact card agreement adds the configurable `Card` weight (default 25), only with amount/currency agreement within the existing date window. Scores are capped at 100 and are not probabilities. A known card conflict blocks reference shortcuts, scored matches, secondary assignments and review proposals. Missing card evidence preserves existing behavior. Repeated same-card transactions still require a clear mutual best candidate.
- Card comparison is plain string equality. Both import paths store `CARD_ACCOUNT_ENTITY_ID` in nullable `CardAccountEntityId` and preserve plain GL `TRAN_RMKS`. Matching normalizes spaces/hyphens but uses no hashing, encryption, decryption, cryptographic key, profile lookup or key file. The obsolete `CardIdentityToken` database column remains solely for migration compatibility and is not used by any matcher.
- Same-file re-import restores missing plain TMS evidence and GL remarks previously replaced by legacy tokens, directly from source rows. Legacy tokens are never reversed or accepted as card numbers. Re-import original extracts, then create a new reconciliation when upgrading a token-based installation. Previous reconciliation results remain unchanged.
- The **AI Analysis** screen shows card-evidence coverage and per-candidate agreement. Its existing evidence schema includes the agreement flag. Historical reconciliation results are unchanged; headers alone cannot measure the actual uplift. ASP.NET browser-cookie/antiforgery protection is independent of card comparison and remains enabled.

## Daily / period reconciliation (2026-10-12)

Daily / period reconciliation runs alongside — and never changes — monthly Bulk reconciliation. A **month container** (`ReconPeriodRun`) groups **date-range runs** (`ReconRun` with `ParentPeriodRunId` + `PeriodGranularity` Day/Range; monthly batches stay `Month`, the default). Each date-range run imports its own files and matches them with `ReconEngine.RunAsync`, then the cumulative set of still-unmatched transactions is reconciled against all available GL within the admin **settlement spread** (`ReconParameters.SettlementSpreadDays`, default 20 days). The month boundary is a consolidation/reporting unit, not a matching boundary: matching is one rolling pool bounded by the spread, so a later load reconciles earlier outstanding transactions (and the reverse) automatically — there is no separate "previous/next" pass. Full design in `DAILY_RECON_DESIGN.md`.

- **Posting-period validation is relaxed for date-range runs.** `MatchingPeriod.ValidateAsync` and `ReconEngine.RunAsync` take `enforcePostingPeriod` (default true). Monthly runs still require the imported batch periods to agree and every processor posting date to fall inside the range (this catches mislabeled files). Date-range runs pass **false**: a deliberate sub-month window legitimately holds posting dates that lag past it, since loads are sliced by transaction date. Only scheme, period format and date order are validated. This must not be re-enabled for date-range runs.
- **The pending-transaction match** is reference agreement + exact billing amount + currency + settlement leg, with the transaction date and GL value date within the spread (date-tolerant; uniqueness required on both sides; competing candidates → `Ambiguous`). It uses no card data. Resolved pairs set `ReconMatch.MatchedAcrossPeriods` and are written into whichever run owns each record (a freeze blocks only new primary runs, never late settlement; cross-period resolution is always audited).
- **Rollup** consolidates a completed month's date-range runs into a single `Month`-granularity batch (re-points matches/exceptions, archives the children); the month batch then behaves like a classic Bulk batch.
- **Orphan sweep** is a spread-UNBOUNDED, reference-only final pass across all consolidated month batches for the long tail; reference-less items are left for manual review.
- Implemented by `DailyReconService` (`BackMatchAsync`, `RollUpAsync`, `OrphanSweepAsync`, shared core `ApplyPoolMatchAsync`); migration `DailyPeriodRuns`; UI at `/recon/daily`. User-facing wording avoids "back-match"/"pool" — it reads "Reconcile pending past transactions".

## Matching safeguards (2026-10-02)

Single-month BankOne imports derive their period from TMS processing dates (transaction-date fallback) and GL posting dates, including duplicate-file reuse. Filenames are not period evidence. When both files identify the same month and the selected month is different, the run screen adjusts the period to the detected full month and displays a notice. Partial ranges within the correct month are retained; mixed or unknown source months are not guessed. Frozen-period and duplicate-run guards apply to the resolved period. Historical run labels and assignments are never rewritten by import metadata correction.

These rules supersede the older greedy-assignment and implicit-MUR descriptions below.

- New runs validate source batch periods, scheme ownership and processor posting dates before creating a run. Transaction dates may precede the posting period. Historical results are retained; incorrectly labelled runs need a new reconciliation with the source period.
- BankOne reconciles using billing amount and its recorded currency first, then available transaction or settlement evidence. GL uses `TRAN_CRNCY_CODE`, independently of account denomination. Missing legacy currency fields retain the documented MUR fallback; explicit unknown GL codes do not become MUR. Exact reference identity cannot bypass currency checks.
- Amount candidate lookup searches the complete tolerance interval within the posting-date window. It uses sorted amount indexes and retains only the best two candidates per side plus candidate counts to bound memory for common amounts.
- The reference pass requires uniqueness across the entire settlement leg and match key, including all event codes. The scored pass requires a clear best candidate on both sides, with a configurable `MinimumScoreGap` (default 5). Ties and unresolved competition are recorded as `Ambiguous`, without assigning a GL row.
- Exact-reference and amount agreement alone no longer forces a score of 100 when dates disagree. Scores describe rule evidence; they are not calibrated probabilities of correctness.
- Conflicting supplied identity references cannot be overridden by amount/date agreement, including secondary and review proposals.
- Secondary matching can reconsider unresolved ambiguous rows using exact transaction date / GL value date, billing amount, currency and settlement leg, while maintaining uniqueness on both sides and assignment reservations. Additional passes reject incorrectly labelled source periods.

### Review and local AI pilot

Completed native Bulk runs expose **Review matching quality & local AI suggestions**, linking to `/recon/run/{id}/matching-review`. The read-only review flags missing references, currency conflicts and repeated primary assignment keys. It also lists all mutually unique residual candidates within ±2 value-date days, with pagination and source row IDs. Date-window proposals require analyst verification of reference and posting direction before manual acceptance; they are not automatically assigned.

The optional review uses the installed local Ollama model configured by `LocalAi:Model` (default `qwen2.5:7b`) at `http://127.0.0.1:11434`. It samples at most five cases, one per category. Evidence includes row IDs, dates, amounts, recorded/assigned currencies, transaction codes and GL direction; account/card identifiers, merchant names and raw narratives are excluded. Cloud aliases and HTTP redirects are rejected. Structured responses must cover each supplied case once, use the allowed review action, and cite only supplied GL rows. Incomplete or invalid responses are rejected. Recommendations are advisory and do not write assignments or alter runs.

Matching review requires an active authenticated user with Bulk page-view permission. Normalized runs are currently excluded from this native-source review to prevent staging-ID confusion. Primary matching continues to support normalized batches.

The MasterCard reconciliation has **three independent functions**, each its own menu option. POS and Bulk
match a processor file against the bank GL using a weighted confidence score. Network ↔ ATM validates the
T464 network settlement file against ATM settlement by reference, amount, currency, and fee; it has no GL
side. The engine is generic: sources, GL scope and weights are configuration, so other schemes
can be added later.

> **Platform note (branch `recon_platform`):** the engine now loads rows from the normalized platform
> model (`NormalizedTransactions` / `NormalizedGlEntries`) when a batch has been projected, falling back
> to the legacy BankOne loaders otherwise. All scheme-specific semantics (channel prefixes, MUR posting
> convention, FX fallback chain, settlement-leg routing) moved into adapters + `NormalizedProjection`
> (`Recon.Web/Platform/`); the matching math below is unchanged and scheme-agnostic. Runs and batches
> carry a `SchemeProfileId` so each scheme's data is fully isolated. See
> [PLATFORM_ARCHITECTURE.md](PLATFORM_ARCHITECTURE.md).

## The three functions

| Function | Processor source | GL scope | Notes |
|---|---|---|---|
| **POS Recon** | File 2 — POS settlement | Cardholder-settlement rows where `TRAN_PARTICULAR` starts `POS` | |
| **Bulk Recon** | File 3 — Interchange (all card txns) | All cardholder-settlement rows | superset; credit + debit |
| **Network ↔ ATM Recon** | File 5 — T464 network settlement + File 1 — ATM settlement | No GL | unique reference comparison; mismatch, one-sided, and duplicate outcomes |

There is currently no ATM General Ledger. ATM transactions are reconciled only by the Network ↔ ATM function.

## GL scope — the six accounts (verified present in the sample)

Only these `ACCT_NUM` values are card-reconciliation accounts; everything else in the GL dump is ignored.

| Role | MUR | USD | EUR |
|---|---|---|---|
| **Cardholder Settlement** (per-txn, holds the match key) | 9849065772 | 9849065773 | 9849065774 |
| **Intermediary Settlement** (MasterCard clearing leg) | 9849077911 | 9849077913 | 9849077912 |

Currency of a processor transaction routes it to the matching cardholder account (by
`TRANSACTION_CURRENCY`). Matching happens against the **cardholder-settlement** rows — these carry the
per-transaction detail:

- Debit matching uses `TRAN_PARTICULAR_2` as the processor `REFERENCE_NUMBER`.
- Credit matching uses `TRAN_PARTICULAR_2.Substring(12)` as processor `MICROFILM REF NUMBER`; the first twelve GL characters are the BankOne posting prefix.
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
| Match key | adapter-defined: debit `ReferenceNumber`; credit `MicrofilmRefNumber == TranParticular2.Substring(12)` | 17 |
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

### Superseded baseline (2026-08-14)

These counts predate the corrected Mastercard credit key (`MICROFILM REF NUMBER` ↔
`TRAN_PARTICULAR_2.Substring(12)`) and must not be used as the current acceptance baseline. Re-run Bulk
reconciliation and record the credit/debit match-rate results before accepting the next release.

| Function | Processor rows | Matched | Exceptions | Notes |
|---|---|---|---|---|
| **POS → GL** | 71,641 | 54,623 | 34,301 | most POS tie to GL `POS/` rows |

### Candidate generation (keeps it O(n))
Index GL cardholder rows by `(currency, roundedAmount)`. Amount is selective, so each processor txn
yields a small candidate set; score those within the date-tolerance window only.

## Outputs

- **ReconMatch** per processor txn: chosen GL row, confidence, tier, both amounts/dates, plus review fields
  `ReferenceNumber`, `MicrofilmRefNumber`, and the effective `TRAN_PARTICULAR_2` value.
- **ReconException** for every Unmatched/Ambiguous/AmountMismatch, with narrative + analyst workflow.
- **Summaries** per function run, by currency, at **two grains**: daily (`Day` set) and monthly
  (`Day` null) — matched vs exception counts and amounts, plus average confidence.

## Known data realities (from profiling the March sample)
- Duplicate analysis treats `(match key, TRANSACTION_CODE)` as transaction identity: `01` purchase, `04` cash, and `09` credit are separate events even when the key repeats.
- ~91% of POS matches tie to `TRAN_AMT` exactly; the rest are real amount exceptions or FX cases.
- GL `REF_NUM` column is unusable (all `NULL` on these accounts) — the reference lives in
  `TRAN_PARTICULAR_2`.
