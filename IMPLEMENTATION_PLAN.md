# ReconTool — Implementation Plan & Task List

Automates the currently-manual, Excel-based **MasterCard reconciliation** at BankOne. Analysts today
cross-check five exports (`SharedFiles/`) by hand; this tool imports them, runs a **three-pass exact
match**, and drives an exception workflow. Data contract = **[DataModel.md](DataModel.md)**; source
profiling = **[FILE_ANALYSIS.md](FILE_ANALYSIS.md)**.

## Confirmed decisions

| Decision | Choice |
|---|---|
| v1 scope | **All three passes** (Network↔Processor, Processor completeness, Processor↔GL) |
| Storage | **Persist to SQL Server** — staging tables, runs, results, exceptions, comments |
| Amount matching | **Exact** (reference + amount + fee to the cent; per currency) |

## Confirmed stack (mirrors SBI_IA Tool)

| Layer | Choice |
|---|---|
| Runtime | .NET 9 |
| UI | Blazor (Interactive Server) + Flowbite Blazor + Tailwind; reuse `MainLayout`/`SideNav`/`PageHeader`/`StatCard`/`StatusBadge`/`AppModal` |
| Data | EF Core 9, code-first, Fluent API only, SQL Server; `DbContextFactory` + `AuditSaveChangesInterceptor` |
| Auth | ASP.NET Identity (local) + AD/LDAP hybrid; permission-driven menu (`AppAccess`/`AppAccessMenu`) |
| Excel import | ClosedXML or the streaming OpenXML reader (files up to 107k rows / 23 MB) |
| Reports | QuestPDF (exception report, run summary); Excel export via ClosedXML |
| Architecture | single Blazor Server app, layered by folders (`Domain`/`Data`/`Services`/`Components`) |

## Solution structure

```
C:\Projects\ReconTool
│  Recon.sln
│  DataModel.md              ← DB contract
│  IMPLEMENTATION_PLAN.md    ← this file
│  FILE_ANALYSIS.md          ← source profiling
│  SharedFiles\              ← sample exports (test fixtures)
├─ Recon.Web\
│   ├─ Domain\   Import/, Staging/, Recon/, Identity/, Common/
│   ├─ Data\     AppDbContext, Configurations/, Migrations/, interceptor, seeders
│   ├─ Services\ importers (one per SourceType), ReconEngine (3 passes), exception svc, PDF/Excel export, LDAP/permissions
│   ├─ Components\ Pages/ (Upload, Runs, RunDetail, Exceptions, Dashboard, Admin/GlMapping) + Shared/
│   └─ wwwroot\  Tailwind build output
└─ Recon.Web.Tests\  parser + engine unit tests against SharedFiles fixtures
```

---

## Phase 0 — Scaffold & infrastructure  ✅ (2026-08-14)
- [x] Create solution `Recon.Web` (Blazor Server, net9.0, nullable); ported SBI_IA `Program.cs`, Flowbite/Tailwind v4 setup, `MainLayout`/`SideNav`/`PageHeader`/`StatCard`/`StatusBadge`, interceptor
- [x] `.claude/launch.json` (port 5104), `appsettings.Development.json` → `Recon` DB on localhost SQL Server
- [x] Builds clean; Tailwind CSS builds; app runs and dashboard renders

## Phase 1 — Identity & access  ✅ (2026-08-14) — ported from SBI_IA
- [x] Ported `AppUser/AppRole/AppAccess/AppAccessMenu/AppPage/UserAppAccess`; `AppDbContext` is now `IdentityDbContext<AppUser,AppRole,Guid>` with global soft-delete filter
- [x] Local + AD/LDAP login (`/auth/login`, `/auth/logout`, `LdapAuthService`), cookie auth, `[Authorize]` global + `AuthorizeRouteView` + `RedirectToLogin`; `ICurrentUserService` reads the principal
- [x] `PermissionService` (merged grants + permission-driven `SideNav` menu); rebranded `Login.razor`; header user + sign-out
- [x] Seed: 13 pages, roles (Administrator/Recon Manager/Recon Officer/Viewer), admin user `admin`/`Admin@123` with full grants
- [x] Admin screens: `/admin/users` (create local user, assign roles, enable/disable) and `/admin/roles` (page-grant matrix)
- [x] `Identity` migration applied; login flow + user creation verified in browser

## Phase 2 — Import pipeline  ✅ (2026-08-14)
- [x] `ImportBatch` entity + upload UI (`/import`, multi-file, type auto-detect by header signature)
- [x] Parsers for all five sources in `ImportService` (T464, DebitSettlement ATM+POS, InterchangeFee header-row-3, GlDump)
- [x] Shared normalization (`ValueParsers`): padded-amount + sign → signed decimal; `"NULL"`→null; ISO ccy map; multi-format dates; MMDDYY/HHMMSS for T464
- [x] Content-hash duplicate guard; per-file parse report on the Upload page; `/batches` list
- [x] Tests (`Recon.Web.Tests`, xUnit, throwaway DBs): T464 count 11,533 + amount/date/ccy normalization; ATM channel + NULL-fee + sign; duplicate rejection — **3/3 passing**

## Phase 3 — Reconciliation engine  ✅ (2026-08-14) — model revised per analyst input
The three-pass idea was replaced by the analyst's actual model (see **[RECON_ENGINE.md](RECON_ENGINE.md)**):
**three processor → GL functions**, matched by **weighted confidence**
(amount + date primary; reference + channel add weight), summarised daily + monthly.
- [x] `ReconEngine` with `ReconFunction` entry points AtmToGl / PosToGl / InterchangeToGl
- [x] GL scoped to 6 seeded `GlSettlementAccount`s; channel via `POS/`·`CASH/` particular prefix; currency routing
- [x] Weighted scorer (`MatchWeights`, appsettings-configurable), greedy assignment, reference-identity amount-mismatch detection
- [x] Persist `ReconMatch` + `ReconException` + `ReconPassSummary` (daily & monthly, currency bucketed to MUR/USD/EUR/Other)
- [x] Verified on real March data: POS 54,623/71,641 · ATM 5,742/22,987 · Bulk 62,385/107,544
- [x] Network ↔ ATM reconciliation: a separate T464 + ATM batch, no GL side, with per-reference
   match/mismatch/one-sided/duplicate validation outcomes

## Phase 4 — Exception workflow  ✅ (2026-08-14)
- [x] Exceptions grid on RunDetail: filter by type + status; drill-in modal
- [x] Side-by-side processor vs GL amounts/dates, confidence, narrative; status transitions (Open→Investigating→Resolved/WrittenOff), audited
- [x] Sanitized comment thread (`HtmlSanitizer`); verified persisting end-to-end via the UI
- [x] Standalone Network ↔ ATM reconciliation (`/recon/network-atm`): reference-level match, amount/fee/currency
   mismatch, one-sided, and duplicate outcomes persisted per run; completed runs refresh the batch list in place

## Phase 5 — Dashboard & reports  🟡 (2026-08-14)
- [x] Dashboard: stat tiles + recent runs; per-run `StatCard` tiles + daily/monthly currency summary tables
- [x] Three recon menu screens (ATM/POS/Bulk) + run detail; global `/exceptions` cross-run view
- [x] Contrast pass (indigo primary, stronger text/borders); fixed invalid Flowbite icon names
- [x] Permission-gated CSV export of filtered match results
- [x] ClosedXML XLSX export of run summary and exception register (PDF out of scope)

## Phase 6 — Hardening  ⬜
- [ ] FGAC on runs/exceptions, audit log, large-file streaming perf pass, seeded demo run from `SharedFiles`

## Phase 7 — Generic multi-scheme platform  ✅ (2026-08-24, branch `recon_platform`)
Transforms the Mastercard tool into a configurable reconciliation platform per
[Generic_Multi_Scheme_Reconciliation_Platform.md](Generic_Multi_Scheme_Reconciliation_Platform.md);
architecture and roadmap in [PLATFORM_ARCHITECTURE.md](PLATFORM_ARCHITECTURE.md). Strategy: strangler
refactor — legacy path kept runnable until output parity, engine matching math untouched.

- [x] **File-format layer** — `Platform/Files/FileReader.cs`: reusable Excel/CSV readers + `HeaderMap` extracted from `ImportService`
- [x] **Normalized model** — `NormalizedTransaction` / `NormalizedGlEntry` (all plan fields: refs, dates ×3, amount/billing/settlement + currencies, D/C, merchant, terminal, acquirer, masked card, card type, scheme, processor, account, description, txn type, trace) + `SchemeProfile` config entity
- [x] **Adapter abstractions** — `ITransactionAdapter` / `IGlcAdapter` / `IStagingSink` / `IAdapterRegistry` (`Platform/Adapters/`)
- [x] **Mastercard adapters** — `BankOneDebitSettlementAdapter` (ATM/POS channel detection), `BankOneInterchangeAdapter`, `BankOneNetworkT464Adapter`, `BankOneGlAdapter` (+ shared `GlBulkPostingClassifier`); parsers moved verbatim from `ImportService`
- [x] **Normalized projection** — `NormalizedProjection` carries all BankOne semantics (channel prefixes, MUR posting convention, FX fallback chain, settlement-leg routing) outside the engine; engine reads normalized tables when projected, legacy loaders otherwise
- [x] **SchemeProfile seeding** — Mastercard—BankOne (active, real adapters, incl. NetworkToAtm) + Visa/UnionPay/RuPay/Domestic placeholders
- [x] **Visa demo adapter** — proves a new scheme = one adapter class + registration, zero engine changes
- [x] **Scheme ownership** — `ImportBatch.SchemeProfileId` / `ReconRun.SchemeProfileId` (migration `SchemeOwnership`); `ReconRunner` filters strictly per scheme; `IImportService.ImportAsync` / `IReconEngine.RunAsync` stamp the owning scheme
- [x] **Demo data** — `DemoDataSeeder`: per placeholder scheme, own GL dump + 3 settlement batches + 3 completed runs with realistic counts/confidence; hashes namespaced `demo-<scheme>-…`
- [x] **Scheme navigation** — landing page `/recon` (card per scheme, direct links to each function's actual screen); collapsible per-scheme sidebar groups with arrow toggle + auto-expand; Mastercard items route to the original screens, others to `/recon/scheme/{id}/{function}`; old flat recon menu items retired; Administration moved below the scheme groups
- [x] **Migrations** — `PlatformNormalizedModel`, `SchemeProfileFunctions`, `SchemeOwnership`
- [ ] Follow-up: route `ImportService` detection through `IAdapterRegistry`; SchemeProfile admin screen; real adapters for Visa/UnionPay/RuPay/domestic

---

## Open items to confirm with analysts
1. **Match granularity for Pass 1** — is a single `ReferenceNumber` unique per settlement, or can it
   repeat (partial/reversal)? Affects duplicate handling.
2. **GL tie-out basis for Pass 3** — do analysts reconcile GL to processor by **daily settlement batch
   total**, by **MUR-equivalent**, or by original currency per account? DataModel currently assumes
   date×currency×account totals.
3. **Which report is "authoritative"** when Network (T464) and Processor (ATM) disagree — the validation
   now surfaces both one-sided outcomes; analysts still need to decide which is actionable.
