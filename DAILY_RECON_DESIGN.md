# ReconTool — Daily / Period Reconciliation Design (2026-10-12)

Status: **implemented (Phases 1–3, 2026-10-12).** Agreed with the product owner in session and built
behind the `PeriodGranularity` flag; nothing here changes the existing monthly Bulk/ATM/POS flow — see
"Non-breaking guarantees" below. Validated end-to-end on real April 2023 data (552 pending past
transactions reconciled across two date-range loads). Phase status is in §9; UX/terminology in §13.

## 1. Why

Monthly batches are coarse enough that settlement lag washes out inside the month. For **daily
reconciliation** it does not: a MasterCard transaction dated day *T* can post to the GL with a value
date anywhere from **same-day to ±20 days** away. A single day cannot be reconciled as a closed box.
We need a model that reconciles arbitrary date ranges (one day, two days, a whole month) while
tolerating that spread, and then consolidates a completed month back into the proven monthly record.

## 2. Core concepts

- **Settlement spread (S)** — admin-configurable window, default ~20 days. Bounds how far apart a
  transaction date and a GL value date may be and still be considered the same settlement. Drives
  both the candidate pool and the Outstanding-vs-Aged split.

- **Month Run** (new parent, `ReconPeriodRun`) — a container for *scheme + calendar month*. Holds
  **no files itself**. It groups the date-range runs beneath it and is the thing that gets rolled up.

- **Date-range run** (a `ReconRun` child) — the unit that actually imports files and matches. Its
  range may be **any width**: one day, two days, or 1st→last. It imports its own TRX + GL for that
  range and matches against the month's accumulating pool (see §4).

- **Outstanding** — an unmatched item that is still *within the spread* of the latest data loaded in
  the month. Expected to settle; **not** an exception yet.

- **Aged** — an unmatched item beyond the spread with no counterpart. A genuine exception. Aging is
  **only finalized at rollup** (during an open month everything unmatched is Outstanding).

- **Rollup** — when a month's days are covered, consolidate the child runs into a single canonical
  **month batch** (a monthly-granularity `ReconRun`, same shape as today's Bulk recon). Children
  become read-only history; the month batch is the record of truth and the orphan-run participant.

- **Load-time back-match (the former "Prev/Next")** — there is **no separate Previous/Next pass**.
  The month boundary is a *consolidation/reporting unit, not a matching boundary*. Every file load
  matches against the cumulative pool of everything processed so far **within the spread**, which
  naturally reaches across the month boundary: when month M+1's GL loads it back-matches month M's
  still-outstanding transactions, and M+1's transactions reach back to M's outstanding GL. Prev/Next
  dissolves into what every load does automatically (see §4).

- **Change table** — at each load, a visible table of what the newly loaded rows resolved behind
  them: *these rows in this file matched these previously-outstanding items from earlier days / the
  prior month.* Transparency for the analyst.

- **Orphan run** — the wider, **spread-unbounded** catch-all over **consolidated month batches**:
  every still-unmatched item across months, matched against every still-available GL entry, ignoring
  the spread window entirely (for long-tail stragglers that settled beyond S). Applies to Bulk and
  Daily. Runs on monthly-shaped records, so it reuses the existing one-pool machinery with no new
  double-match surface (see §7).

## 3. Schema

Additive only. New default values make every existing row behave exactly as today.

- **`ReconPeriodRun`** (new): `ReconPeriodRunId`, `SchemeProfileId?`, `Function`, `PeriodKey`
  (`yyyy-MM`), `Status` (Open / RolledUp), audit columns (`IAuditable`).
- **`ReconRun`** (additive): `ParentPeriodRunId int?` (null = classic standalone run — i.e. all
  existing runs), `PeriodGranularity` enum `{ Month = 0, Day = 1, Range = 2 }` default **`Month`**.
- **Settings**: `SettlementSpreadDays` (admin-editable; global default, optionally overridable per
  `SchemeProfile`).
- Migration: `DailyPeriodRuns` (new table + two nullable columns + FK/index on `ParentPeriodRunId`).

No change to `ReconMatch` / `ReconException` shape — date-range children reuse them keyed by their
own `ReconRunId`, so the matching service and the exceptions workflow are **reused, not forked**.

## 4. Matching flow (one rolling pool, spread-bounded)

There is a single rolling open pool. The month is a label on it, not a wall through it. Every file
load matches against **everything processed so far within the spread**, regardless of which day or
month it was loaded in.

Reference-matched records already ignore the spread — they join on the reference number and settle
whenever they settle. The spread only affects the **date/amount/currency fallback** (today
date-exact; the card pass is already ±2 days via `Enumerable.Range(-2, 5)`) and **which GL is a
candidate**.

Each load executes **bidirectionally** against the pool, bounded by S:

1. **New transactions → available GL.** This load's transactions are matched against *all*
   still-available GL whose value date is within S of the transaction date, excluding `usedGl` —
   including GL loaded in earlier days and in the **prior month** if within S.
2. **New GL → outstanding transactions.** This load's GL is offered to still-Outstanding transactions
   from earlier loads within S — including transactions from the **prior month**. This is the
   cross-boundary back-match; its results populate the §2 **change table**.

Both directions are the existing Prev/Next logic, now applied continuously per load and bounded by S
instead of ±1 calendar month — the month boundary falls out as just another point the window crosses.
Safety is unchanged: `usedGl` + the reserved-GL exclusion (`SecondaryMatchService.cs:95-104`)
guarantee a GL row is claimed once across the overlapping windows.

**Cross-period resolution is always allowed and audited.** When a back-match resolves an item that
belongs to an already-consolidated/frozen prior month, it writes into that month's records (its
Outstanding count drops, the item flips to Matched) and is logged. A freeze blocks starting a **new
primary run** for a period; it does **not** block late settlement arriving — under this model late
settlement is the normal case, not a reopening, so no manual unfreeze is required on this path.

The date/amount fallback key relaxes from date-exact to **date-tolerant within S**, with
**uniqueness-within-window** as the ambiguity guard (two candidates sharing leg/amount/ccy in the
window → `Ambiguous`, no assignment). No date guessing.

A run of width "1st→last day" is simply the degenerate case: one wide range = today's monthly batch.

## 5. Aging

During an **open** month, every unmatched item is **Outstanding** — we cannot yet distinguish
not-yet-settled from missing, because later GL may still arrive. At **rollup**, anything still
unmatched is finalized: within S of month end and plausibly settling next month → carried as
Outstanding (eligible for the next month's load-time back-match); beyond S → **Aged exception** on
the month batch (eligible only for the spread-unbounded Orphan run).

## 6. UI / forms (new surface, isolated from monthly)

- **New Daily/Period recon area.** Create a Month Run (scheme + month). Under it, "Add date-range
  run" with From/To (defaults to a single day), inline TRX + GL upload (same uploader as
  `ReconRunner`), Run. List of date-range runs with per-run matched / Outstanding counts and a month
  roll-up state.
- **Change table on load.** After each load, show what the newly loaded rows resolved behind them —
  previously-outstanding items (earlier days or the prior month) now matched by this load, grouped by
  the day/month they belonged to.
- **Month Run detail**: accumulated matched / Outstanding / (post-rollup) Aged, the child runs, and a
  **Roll up month** action (Manager/Admin) when coverage is complete.
- **Admin**: `SettlementSpreadDays` setting.
- Classic ATM/POS/Bulk screens are untouched.

## 7. Orphan run & freeze policy

- The cross-boundary stragglers inside the spread are already handled automatically by load-time
  back-match (§4), so **Orphan is the spread-unbounded sweep for the long tail** — items that settled
  beyond S. It runs over **consolidated month batches** (monthly-shaped records → one clean pool, no
  two-schema union, Bulk + Daily).
- A forgotten rollup is **not** a correctness bug: the next month's load-time back-match still
  reconciles across the boundary within S. Rollup is consolidation, not a matching dependency.
- **Freeze guards new primary runs, not late settlement.** Freezing a period prevents starting a new
  primary reconciliation for it. It does **not** block a later load's back-match or the Orphan run
  from resolving that period's outstanding items — those always apply and are audited (§4). No manual
  unfreeze is needed for late settlement.

## 8. Non-breaking guarantees

- `PeriodGranularity` defaults to `Month`; `ParentPeriodRunId` defaults null. Every existing run and
  code path is unchanged.
- `ReconMatch` / `ReconException` shapes unchanged; existing queries unaffected.
- The month-string Prev/Next lookup (`SecondaryMatchService.cs:79-90`) generalizes to a date-window
  lookup that returns the identical result for monthly keys (a month = a 1-month window).
- Duplicate detection for date-range runs keys on `ParentPeriodRun + range` with the existing "Run
  anyway" override; freeze applies at the month batch after rollup.

## 9. Phasing

1. **Phase 1 — DONE (2026-10-12).** `ReconPeriodRun` + `ReconRun.ParentPeriodRunId`/`PeriodGranularity`
   + `ReconParameters.SettlementSpreadDays`; migration `DailyPeriodRuns` applied. Admin spread setting
   at `/admin/parameters`. `DailyReconService.BackMatchAsync` — the spread-bounded, bidirectional,
   pool-wide back-match that writes into whichever run owns each record and returns a per-run change
   table; re-runnable after each load; non-card; isolated from `SecondaryMatchService`. Proven by
   `DailyReconServiceTests` (cross-run resolve + idempotency, spread bound, ambiguity). UI: `/recon/daily`
   (month containers + create) and `/recon/daily/{id}` (add date-range run → primary match → back-match
   → change table). Menu page `DAILYRECON` seeded. **Scope note:** the pool is the container's own child
   runs (within-calendar-month). The cross-calendar-month extension (include the adjacent month
   container's runs in the pool) is the same code over a wider run set — the seam is marked in
   `DailyReconService.BackMatchAsync`.
2. **Phase 2 — DONE (2026-10-12).** Cross-calendar-month pool: `BackMatchAsync` now pools this
   container + its immediate calendar neighbours (open children, or a rolled-up neighbour's month
   batch), so May's load back-matches April's outstanding and vice versa. GL discovery is now
   **exception-driven** (available GL = the pool's UnmatchedGl exceptions), which is batch-count- and
   rollup-independent. `RollUpAsync` consolidates a month: re-points children's matches/exceptions onto
   a new `Month`-granularity run (`ParentPeriodRunId == null`, so it reads as a standalone month batch),
   archives the children, marks the container `RolledUp` + `RolledUpRunId`; Manager/Admin only
   (BULKRECON view+edit). The classic Bulk screen now excludes date-range children (`ParentPeriodRunId
   == null` filter) so daily stays isolated while the rolled-up batch still appears. Month-detail UI
   shows Matched / Outstanding / Aged cards + "Roll up month" + rolled-up banner. Tests:
   `Cross_month_load_resolves_prior_month_outstanding`, `Rollup_consolidates_children_into_a_month_batch`
   (5/5 `DailyReconServiceTests`).
3. **Phase 3 — DONE (2026-10-12).** `OrphanSweepAsync(schemeId?)` — a spread-**unbounded**,
   **reference-anchored** final pass over all standalone month batches (rolled-up daily months +
   classic monthly Bulk) for one scheme. With no date window the only safe anchor is an exact
   reference match (+ amount/currency/leg + uniqueness on both sides); reference-less stragglers are
   left for manual review. Writes into whichever batch owns each record (freeze does not block late
   settlement). Manager/Admin. The daily back-match and the orphan sweep now share one core,
   `ApplyPoolMatchAsync` (`maxSpreadDays` null = unbounded; `requireReference` toggles the anchor). UI:
   "Orphan sweep" card on `/recon/daily`. Tests:
   `Orphan_sweep_resolves_reference_match_beyond_spread_and_ignores_refless` (6/6 `DailyReconServiceTests`).
   Verified end-to-end against the live DB (6 month batches in scope).

## 11. Phase 1 build notes

- Primary match of a date-range run reuses `ReconEngine.RunAsync` unchanged; the run is then tagged
  with `ParentPeriodRunId` + `PeriodGranularity` (Day if from==end, else Range).
- A date-range run is expected to import the files **for that range** (the uploaded slice). Phase 1
  does not re-slice staging rows by date inside the engine; matching uses the whole imported batch.
- `DailyReconService` only flips Outstanding → Matched/Ambiguous and clears the corresponding
  exceptions in each owner run; it never creates new (aged) exceptions during an open month.

## 12. Phase 2 build notes

- **Pool** = this container + immediate calendar neighbours (prior/next month). More-distant months
  exceed a realistic spread, so one neighbour each side suffices. Classic monthly batches that were not
  created via a container are not pooled (daily is container-based).
- **GL discovery is exception-driven.** Available GL for the pool = the GL rows referenced by
  UnmatchedGl exceptions across pool runs; the exception's run owns the GL. This replaced keying on a
  run's single `GlBatchId`, which a rolled-up (multi-batch) month cannot represent.
- **Rollup** creates the month batch as `ParentPeriodRunId == null`, `PeriodGranularity = Month`, so it
  is a first-class month batch (shown on the Bulk screen, eligible for the future Orphan run).
  Children are archived, not deleted, so their change-table history (`ReconRunPass`) survives. The
  consolidated run's `ProcessorBatchId`/`GlBatchId` are nominal (first child's). As of 2026-10-20,
  RunDetail resolves all archived child imports for the source-data tabs and Re-run analysis;
  its Reconciliation tab continues to show the consolidated matches/exceptions/summaries.
- **Aged (display)** = unmatched exceptions whose date ≤ month-end − spread (settlement window closed
  within the month). Computed for display, not persisted.

## 13. UX, terminology and validation (2026-10-12)

- **User-facing terminology.** The UI never shows "back-match" or "pool". The automatic reconciliation
  of earlier unmatched items reads "**Reconcile pending past transactions**"; the orphan sweep reads
  "**Reconcile long-outstanding transactions (all months)**". Internal service/record/const names keep
  BackMatch/pool — this rule is about on-screen text only.
- **Posting-period validation.** Date-range runs call `ReconEngine.RunAsync` with
  `enforcePostingPeriod: false` — a sub-month window legitimately holds processor posting dates that lag
  past it (loads are sliced by transaction date). Monthly behaviour is unchanged (default true). See
  RECON_ENGINE.md. Without this, daily runs are rejected with "Processor posting dates fall outside…".
- **Progress UX.** The Add-run flow uses the same overlay as the monthly runner — four phases
  (TRX → GL → Match records → Reconcile pending) with live record counts and the unique-reference pause.
- **Reconciled view.** The month page shows a cumulative **count + per-period summary** of reconciled
  pending transactions, read from the DB (`MatchedAcrossPeriods`), so a re-run **never wipes it** — a run
  with nothing new just notes "no new matches". Clicking the count opens a right-side **drawer** with the
  detail (reference, amount, currency, transaction date, GL value date; latest 200 of N).
- **Tests.** `DailyReconServiceTests` (6): cross-run resolve + idempotency, spread bound, ambiguity,
  cross-month, rollup, orphan (beyond-spread + refless-ignored). Period-validation change covered by the
  existing source-period / matching-period tests.
- **Demo slicing.** `scratchpad/slice_april.py` splits an April file into two date-range slices (16-Apr
  cut, by transaction date for TRX / value date for GL, no rows dropped) for a live cross-load demo.
  Source files live under the git-ignored `SharedFiles/`.

## 10. Open questions

- Per-currency spread (MUR T+1 vs USD/EUR nostro T+2–3) — Phase 1 uses a single global/scheme spread;
  per-currency is a later refinement.
- Exact rollup semantics for Ambiguous children (carry as Ambiguous vs force review before rollup).
