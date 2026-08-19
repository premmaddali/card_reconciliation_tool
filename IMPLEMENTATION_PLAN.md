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
**three independent functions, each a processor file → GL**, matched by **weighted confidence**
(amount + date primary; reference + channel add weight), summarised daily + monthly.
- [x] `ReconEngine` with `ReconFunction` entry points AtmToGl / PosToGl / InterchangeToGl
- [x] GL scoped to 6 seeded `GlSettlementAccount`s; channel via `POS/`·`CASH/` particular prefix; currency routing
- [x] Weighted scorer (`MatchWeights`, appsettings-configurable), greedy assignment, reference-identity amount-mismatch detection
- [x] Persist `ReconMatch` + `ReconException` + `ReconPassSummary` (daily & monthly, currency bucketed to MUR/USD/EUR/Other)
- [x] Verified on real March data: POS 54,623/71,641 · ATM 5,742/22,987 · Bulk 62,385/107,544

## Phase 4 — Exception workflow  ✅ (2026-08-14)
- [x] Exceptions grid on RunDetail: filter by type + status; drill-in modal
- [x] Side-by-side processor vs GL amounts/dates, confidence, narrative; status transitions (Open→Investigating→Resolved/WrittenOff), audited
- [x] Sanitized comment thread (`HtmlSanitizer`); verified persisting end-to-end via the UI

## Phase 5 — Dashboard & reports  🟡 (2026-08-14)
- [x] Dashboard: stat tiles + recent runs; per-run `StatCard` tiles + daily/monthly currency summary tables
- [x] Three recon menu screens (ATM/POS/Bulk) + run detail; global `/exceptions` cross-run view
- [x] Contrast pass (indigo primary, stronger text/borders); fixed invalid Flowbite icon names
- [ ] QuestPDF exception & run-summary PDF; ClosedXML Excel export of exceptions (remaining)

## Phase 6 — Hardening  ⬜
- [ ] FGAC on runs/exceptions, audit log, large-file streaming perf pass, seeded demo run from `SharedFiles`

---

## Open items to confirm with analysts
1. **Match granularity for Pass 1** — is a single `ReferenceNumber` unique per settlement, or can it
   repeat (partial/reversal)? Affects duplicate handling.
2. **GL tie-out basis for Pass 3** — do analysts reconcile GL to processor by **daily settlement batch
   total**, by **MUR-equivalent**, or by original currency per account? DataModel currently assumes
   date×currency×account totals.
3. **Which report is "authoritative"** when Network (T464) and Processor (ATM) disagree — drives whether
   MissingLeft vs MissingRight is the actionable exception.
