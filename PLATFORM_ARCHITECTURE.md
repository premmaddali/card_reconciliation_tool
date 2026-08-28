# Architectural Assessment & Implementation Roadmap
## Generic Multi-Scheme Reconciliation Platform

Companion to [Generic_Multi_Scheme_Reconciliation_Platform.md](Generic_Multi_Scheme_Reconciliation_Platform.md).
Status: **implemented** (phases 0–6 complete on branch `recon_platform`, 2026-08-24; all 9 tests green).
Decisions taken: **legacy staging + projection** storage strategy; full phase 0–6 scope.

---

## 1. Current-state assessment

### 1.1 What is already generic (keep as-is)

| Component | Location | Assessment |
|---|---|---|
| Weighted-confidence matcher | `Services/Recon/ReconEngine.cs` → `MatchAsync` / `Score` | **Already scheme-agnostic.** Operates on in-memory `ProcRow`/`GlRow` shapes; weights/tolerances from `MatchWeights` config; candidate generation indexes by `(leg, currency, cents)` — no scheme knowledge. |
| Match weights & tolerances | `Services/Recon/MatchWeights.cs` | Fully configurable via appsettings. |
| GL account scoping | `Domain/Recon/GlSettlementAccount.cs` (DB table) | Accounts, roles, currencies are data, not code. |
| Exception workflow | `ExceptionWorkflowService`, maker/checker in `ReconWorkflow.cs` | Generic. |
| Bulk insert pipeline | `ImportService.BulkInsertAsync` (SqlBulkCopy, reflection-based) | Generic over any staging entity. |
| Results, summaries, exports, dashboard, RBAC | Pages / services | Generic consumers of match/exception rows. |

### 1.2 Mastercard/BankOne-specific components (the adapters to extract)

| # | Hard-coded today | Where |
|---|---|---|
| 1 | **Five source types** (`SourceType` enum) with header-signature detection baked into one service | `Domain/Import/ImportBatch.cs`, `ImportService.DetectSource` |
| 2 | **Per-source parsers** mapping exact column names ("Header Code", "MICROFILM REF NUMBER"…) into five concrete staging tables | `ImportService.ParseT464/ParseInterchange/ParseDebit/ParseGl` |
| 3 | **Staging schema per source** — `StgNetworkT464`, `StgInterchangeFee`, `StgDebitSettlement`, `StgGlEntry`, `StgGlBulkPosting` | `Domain/Staging/` |
| 4 | **Processor-row loading** with BankOne semantics (channel via `TRANSACTION_WORDING`, leg via `ProductType == "CR"`, FX fallback chain txn→settlement→billing) | `ReconEngine.LoadProcessorAsync` |
| 5 | **GL-row loading** with bank-specific semantics (`Ccy = 480` hard-coded, channel from `TRAN_PARTICULAR` prefix `POS/`·`CASH/`, reference in `TRAN_PARTICULAR_2`) | `ReconEngine.LoadGlAsync` |
| 6 | **Six seeded Mastercard accounts** | `DbSeeder` (already data — just re-label as a scheme profile seed) |
| 7 | **Function enum** (`AtmToGl…NetworkToAtm`) and UI copy "MasterCard · BankOne" | `Domain/Import/ReconRun.cs`, SideNav/Home/Login |

### 1.3 Key insight

The engine core (`MatchAsync`/`Score`) needs almost no change. The coupling lives at the
**edges**: file detection/parsing (#1–3) and normalization into the engine's row shapes (#4–5).
That is exactly the adapter boundary the vision describes — so the refactor is an
**extraction, not a rewrite**.

---

## 2. Target architecture

```
Blazor UI (batch wizard: scheme → CMS/CBS profile → upload → run)
        │
        ▼
┌──────────────────────────── Platform Core ────────────────────────────┐
│  ReconEngine (unchanged math)      Workflow / Exceptions / Exports    │
│  operates only on NormalizedTransaction + NormalizedGlEntry          │
└───────────────▲──────────────────────────────────────▲────────────────┘
                │ normalized models                     │
     ┌──────────┴──────────┐                 ┌──────────┴──────────┐
     │  ITransactionAdapter │                 │  IGlcAdapter        │
     │  (per CMS/scheme)    │                 │  (per CBS/format)   │
     └──────────▲──────────┘                 └──────────▲──────────┘
                │ raw rows                              │
        AdapterRegistry ← resolved by SchemeProfile configuration (DB)
```

### 2.1 New abstractions (`Recon.Web/Platform/`)

```csharp
/// A payment-scheme + CMS combination that can parse a transaction file.
public interface ITransactionAdapter
{
    string Code { get; }                       // e.g. "mastercard-bankone-atm"
    bool CanDetect(IFileHeaders headers);      // header-signature detection
    IEnumerable<NormalizedTransaction> Parse(IFileReader reader, ParseContext ctx);
}

/// A core-banking / GL export format.
public interface IGlcAdapter
{
    string Code { get; }
    bool CanDetect(IFileHeaders headers);
    IEnumerable<NormalizedGlEntry> Parse(IFileReader reader, ParseContext ctx);
}
```

- **NormalizedTransaction / NormalizedGlEntry** — the common model from the plan
  (reference, external reference, dates ×3, amount/billing/settlement + currencies,
  D/C indicator, merchant, terminal, acquirer, masked card, card type, scheme,
  processor, account, description, txn type, auth code, trace, batch).
- **AdapterRegistry** — DI-resolved `IEnumerable<ITransactionAdapter>`; detection loops
  adapters instead of hard-coded header checks.
- **SchemeProfile** (DB entity) — one row per configured scheme/CMS pairing:
  display name, transaction adapter code, GL adapter code, matching-rule overrides
  (weights/threshold/date tolerance), duplicate rules, active flag.
  This is the plan's "configuration over code": adding Visa/OpenWay = new adapter class +
  a `SchemeProfile` row, zero engine changes.
- **File format layer** — extract the existing Excel/CSV row readers
  (`IImportRow`, `HeaderMap`, blank-row skip) into `IFileReader` implementations reused by all adapters.

### 2.2 Data model changes (additive only)

| Change | Type |
|---|---|
| `SchemeProfile` table (scheme, CMS, adapter codes, rule overrides) | new |
| `NormalizedTransaction` / `NormalizedGlEntry` tables keyed by `ImportBatchId` | new |
| `ImportBatch.SourceType` → plus nullable `SchemeProfileId`; keep legacy enum for existing rows | extend |
| `ReconRun.SchemeProfileId` | extend |
| Existing five staging tables | **kept** — Mastercard adapters continue writing them, and a projection step fills the normalized tables (or adapters write normalized directly; decided in phase 2 spike) |

### 2.3 Engine changes (minimal)

1. `LoadProcessorAsync` / `LoadGlAsync` switch to reading the **normalized** tables;
   the current BankOne-specific logic moves verbatim into the Mastercard adapters
   (channel prefixes, `Ccy=480`, FX fallback chain, leg routing become adapter rules).
2. `Score()` gains optional configurable fields (auth code, trace, settlement date) — weights already exist for Card/Time; add entries rather than new logic paths.
3. Everything downstream (matches, exceptions, summaries, workflow, exports) untouched.

---

## 3. Refactoring strategy (strangler, never big-bang)

Each phase ends with `dotnet test` green and the March sample reproducing
POS 54,623 · ATM 5,742 · Bulk 62,385 matches.

| Phase | Content | Risk |
|---|---|---|
| **0. Baseline** | Capture golden results for all four functions on sample files as a regression fixture | none |
| **1. File-format layer** | Extract `IFileReader`/header utilities from `ImportService`; behavior identical | low |
| **2. Normalized model + Mastercard adapters** | Define normalized entities/migrations; implement `BankOne*Adapter`s by moving parser code; run both paths, compare outputs | medium |
| **3. Adapter registry & SchemeProfile** | Detection via registry; seed `SchemeProfile` for Mastercard/BankOne; batch wizard gains scheme/profile pickers defaulting to Mastercard | medium |
| **4. Engine reads normalized model** | Swap `Load*Async` sources; delete nothing yet | medium |
| **5. Prove extensibility** | Add one synthetic second profile (e.g. Visa CSV demo adapter) end-to-end without touching the engine | low |
| **6. Cleanup** | Retire dead branches, relabel UI copy, update docs | low |

Phases 2–4 keep the old path runnable until output parity is proven, satisfying
"All existing functionality must continue to work exactly as it does today."

---

## 4. Technical risks & mitigations

| Risk | Mitigation |
|---|---|
| Performance regression moving 100k-row loads through a wider normalized model | Keep SqlBulkCopy into normalized tables; index `(ImportBatchId, ReferenceNumber)` and `(ImportBatchId, Ccy, Amount)`; phase-0 fixture times each run |
| Channel/leg/FX semantics differ subtly per CMS and leak back into the engine | These live **only** in adapters; engine sees pre-routed leg/channel flags; code review gate |
| Config sprawl (weights duplicated per profile vs global) | `SchemeProfile` overrides are sparse — null = inherit global `MatchWeights` defaults |
| Migration burden on existing DB | All changes additive; legacy staging tables retained; no destructive migrations |
| Scope creep toward full productization | This branch delivers phases 0–5; deployment/UI polish stays out of scope |

---

## 5. Decisions needed before Phase 1

1. **Normalized storage strategy**: adapters write normalized tables directly (simpler, recommended)
   vs. keep legacy staging + projection views (more compatibility, more code)? — **Decided: legacy staging + projection** (`NormalizedProjection`).
2. **SchemeProfile granularity**: one profile per scheme+CMS pair (recommended), or per function too? — **Decided: per scheme+CMS**, with a `Functions` list per profile.
3. **UI placement**: add scheme/profile selection to the existing batch wizard (recommended) vs. a separate platform screen? — **Decided: scheme-first navigation** — landing page `/recon` with one card per scheme linking to each function's actual screen; collapsible per-scheme sidebar groups; Mastercard routes to the original screens, others to `/recon/scheme/{id}/{function}`.
4. **Visa demo adapter in scope** for phase 5, or defer to a follow-up? — **Decided: in scope** (`VisaDemoTransactionAdapter`), plus full demo data per placeholder scheme (`DemoDataSeeder`).

On agreement, implementation proceeds phase by phase with the regression fixture gating every step.

---

## 6. Implementation status (2026-08-24)

| Phase | Status | Notes |
|---|---|---|
| 0. Baseline | ✅ | 9 xUnit tests green throughout; March-sample expectations preserved |
| 1. File-format layer | ✅ | `Platform/Files/FileReader.cs` (`IImportRow`, `HeaderMap`, `ParsedFile`) |
| 2. Normalized model + adapters | ✅ | `Platform/Model/`, `Platform/Adapters/BankOne*`, `NormalizedProjection`; migrations `PlatformNormalizedModel`, `SchemeProfileFunctions` |
| 3. Registry & SchemeProfile | ✅ | `AdapterRegistry` in DI; seeded Mastercard—BankOne + 4 placeholder profiles |
| 4. Engine reads normalized | ✅ | `LoadGlAsync`/`LoadProcessorAsync` try normalized first, legacy fallback; scheme ownership via `SchemeProfileId` (migration `SchemeOwnership`) |
| 5. Extensibility proof | ✅ | `VisaDemoTransactionAdapter` + per-scheme demo data (`DemoDataSeeder`) |
| 6. Cleanup & UI | ✅ | Scheme landing page, collapsible sidebar groups, Mastercard→legacy route mapping, neutral UI copy |

**Remaining follow-ups**: route `ImportService` detection through `IAdapterRegistry`; SchemeProfile admin
screen; real adapters for Visa/UnionPay/RuPay/domestic schemes; clear demo rows before production use.
# Adapter-owned matching keys

Determining the business match key is an adapter responsibility. For example, BankOne Debit emits
`ReferenceNumber`, BankOne Credit emits `MicrofilmRefNumber`, and a future Visa adapter may emit a retrieval
reference number. The engine consumes the normalized key only; for BankOne credit the GL adapter supplies the
key from `TRAN_PARTICULAR_2` after its twelve-character posting prefix.
