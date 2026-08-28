# ReconTool — Project Status & Task Register

**Multi-Scheme Card Reconciliation Platform** — status as of **2026-08-28** (branch `recon_platform`).

A .NET 9 Blazor Server application that automates the (previously manual, Excel-based) reconciliation
of card settlement against the general ledger. Originally a Mastercard/BankOne tool, now evolving into
a **generic multi-scheme platform** (Mastercard, Visa, UnionPay, RuPay, domestic schemes) where schemes,
CMS platforms and core-banking formats are added through adapters and configuration — see
**[PLATFORM_ARCHITECTURE.md](PLATFORM_ARCHITECTURE.md)** and
**[Generic_Multi_Scheme_Reconciliation_Platform.md](Generic_Multi_Scheme_Reconciliation_Platform.md)**.

> Companion documents: **[FILE_ANALYSIS.md](FILE_ANALYSIS.md)** (source data) · **[DataModel.md](DataModel.md)**
> (database contract) · **[RECON_ENGINE.md](RECON_ENGINE.md)** (matching logic) ·
> **[IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)** (phase history) ·
> **[PLATFORM_ARCHITECTURE.md](PLATFORM_ARCHITECTURE.md)** (platform refactor).

---

## 1. How to run

| | |
|---|---|
| Solution | `C:\Projects\ReconTool\Recon.slnx` (`Recon.Web` + `Recon.Web.Tests`) |
| Run | `powershell -ExecutionPolicy Bypass -File C:\Projects\ReconTool\Scripts\Start-ReconTool.ps1` → **http://localhost:5104**; stop with `Scripts\Stop-ReconTool.ps1` |
| CSS build | `npm run build:css` in `Recon.Web` (Tailwind v4) — run after markup changes |
| Database | SQL Server `localhost`, database **`Recon`** (migrations auto-apply on startup) |
| Login | **`admin` / `Admin@123`** — ⚠️ change on first use |
| Tests | `dotnet test` (9 xUnit tests against the real March sample files) |

---

## 2. Technology stack

- **.NET 9**, Blazor Server (Interactive Server render mode)
- **Flowbite Blazor + Tailwind CSS v4** (light/dark, indigo primary)
- **EF Core 9 + SQL Server**, code-first, Fluent API only, `DbContextFactory` pattern, audit interceptor
- **ASP.NET Identity** (local) **+ Active Directory / LDAP** (hybrid), permission-driven menu
- **ClosedXML** (Excel import and export), **HtmlSanitizer**, an authorized CSV match export, and a permission-gated XLSX run summary with exception register

---

## 3. What is built (completed)

### 3.1 Foundation & infrastructure ✅
- Blazor Server project scaffolded to match SBI_IA; Tailwind/Flowbite build; layout (`MainLayout`,
  `SideNav`, `EmptyLayout`) and shared components (`PageHeader`, `StatCard`, `StatusBadge`, `AppModal`
  equivalents, `AvatarInitials`).
- `AppDbContext` (IdentityDbContext) with global soft-delete filter, `AuditSaveChangesInterceptor`
  (stamps CreatedBy/Date, converts deletes to soft deletes), `ICurrentUserService`.
- SQL Server `Recon` DB; migrations: `InitialCreate`, `ReconEngine`, `WidenReconReference`,
  `Identity`, `BatchPeriodRange`.

### 3.2 Import pipeline ✅
- `ImportService` auto-detects which of the five BankOne exports a file is, by header signature
  (incl. the Interchange file's row-3 header and ATM-vs-POS disambiguation).
- `ValueParsers` normalizes the messy source data: zero-padded text amounts + separate sign columns →
  signed decimals; literal `"NULL"` → null; numeric ISO currencies (480/840/978) → codes; multiple date
  formats incl. T464's `MMDDYY`/`HHMMSS`.
- Four staging tables (ATM + POS consolidated with a `Channel` discriminator; Interchange; GL; T464).
- **Content-hash de-dup that reuses** already-staged files (one GL dump serves many batches).
- Reused uploads are reported as **Already imported** and the import fixture tests tolerate files opened in Excel.
- Verified: row counts exact (11,533 / 107,544 / 71,641 / 22,987 / 106,282).

### 3.3 Reconciliation engine ✅
- **Three independent functions**: **POS → GL**, **Bulk (Interchange) → GL**, and **Network ↔ ATM**.
  ATM has no GL dataset and is validated only against T464 network settlement.
- Scopes GL to the **six seeded settlement accounts** (cardholder DR + intermediary CR, per MUR/USD/EUR),
  channel via the `POS/`·`CASH/` particular prefix, routed by currency.
- **Weighted confidence match** — amount + date primary (45 + 33), adapter-defined key + channel add weight
  (17 + 5); threshold 70; reference-identity catches amount mismatches. Weights configurable in
  `appsettings` (`MatchWeights`).
- Persists `ReconMatch`, `ReconException`, `ReconPassSummary` (daily **and** monthly, per currency,
  non-GL currencies bucketed to "Other").
- **Mastercard credit correction (2026-08-27):** debit matches `REFERENCE_NUMBER` to GL
  `TRAN_PARTICULAR_2`; credit matches `MICROFILM REF NUMBER` to `TRAN_PARTICULAR_2.Substring(12)`.
  The match register and Advanced Analysis expose `Reference`, `Microfilm`, and effective
  `TRAN_PARTICULAR_2`, with filters for the latter two. The prior March counts are superseded pending a
  fresh Bulk run and measured acceptance baseline.

### 3.4 Batch-centric workflow ✅ (current UX)
- Each recon screen = **"New reconciliation batch"**: pick a **period (from/to)**, upload **TRX** + **GL**
  files inline, **Run**. Validates file types; runs the engine; navigates to results.
- **Network ↔ ATM recon** (`/recon/network-atm`, its own menu link and batch list) validates a **T464
  network settlement** file against the **ATM settlement** report: match, amount/fee/currency mismatch,
  one-sided, and duplicate outcomes per reference. No GL side. On completion, the page refreshes its
  batch list in place; open the new top row to review the result.
- **Duplicate-period warning** (function + date range) with non-blocking "Run anyway".
- **Results view = 3 tabs**: **Transactions** (TRX rows), **GL entries** (relevant rows only, filtered
  to accounts + channel), **Reconciliation** (stat cards + currency summary + exceptions). Paged tables.
- **Mastercard review columns:** Match results and Advanced Analysis show `Reference`, `Microfilm`, and
  effective `TRAN_PARTICULAR_2`; the latter is unchanged for debit and `Substring(12)` for credit. Match
  results supports column filters for Microfilm and `TRAN_PARTICULAR_2`.
- Batches list per function; all values persisted and reopenable.

### 3.5 Exception workflow ✅
- Filterable exceptions grid (by type + status); drill-in modal showing processor-vs-GL amounts,
  confidence, narrative.
- Status transitions (Open → Investigating → Resolved / WrittenOff) and a **sanitized comment thread**
  (`ExceptionWorkflowService`, HtmlSanitizer), audited. Verified persisting end-to-end.
- Global `/exceptions` cross-run overview.

### 3.5.1 ATM network validation ✅
- Standalone `NetworkToAtm` runs persist one T464-to-ATM validation outcome per nonblank reference, including
  source-row identifiers, amount/currency/fee/date evidence, and duplicate counts.
- The batch detail view shows summary counts plus a filterable, paged comparison register. This confirms or
  challenges the processor-side ATM transaction before analysts investigate its GL outcome.

### 3.6 Authentication & access ✅ (ported from SBI_IA)
- **Local + AD/LDAP login** (`/auth/login`, `/auth/logout`, `LdapAuthService`), cookie auth (8h sliding),
  lockout after 5 failures; branded login page; every route protected (`[Authorize]` + `RedirectToLogin`).
- **Permission-driven menu** (`PermissionService`) — sidebar built from each user's page grants.
- Identity model: `AppUser/AppRole/AppAccess/AppAccessMenu/AppPage/UserAppAccess`.
- **Seeded**: 13 pages, 4 roles (Administrator, Recon Manager, Recon Officer, Viewer) with default
  grants, admin user with full access.
- **Admin screens**: `/admin/users` (create local users, assign roles, enable/disable) and
  `/admin/roles` (per-page permission matrix). Verified in browser.
- Match CSV downloads require both authentication and the matching ATM/POS/Bulk page-view grant.
- Each run can download a permission-gated XLSX workbook containing its frozen currency summary and exception register.

### 3.7 Dashboard ✅
- Home dashboard: stat tiles (files, rows staged, sources loaded, runs), recent completed runs, quick
  links to each recon function.
- Dashboard includes a role-aware **Awaiting verification** queue: a signed-in user sees only runs whose
  active workflow step is assigned to one of their roles.

### 3.7.1 Batch lifecycle & administration ✅ / follow-up
- A completed run can be archived; an immutable `BatchArchive` snapshot and transaction-level
  `BatchArchiveTransaction` rows are retained, while the live run is hidden from active batch lists.
- Freezing a reconciliation scope archives all completed runs for the same function, scheme and period,
  then blocks a new reconciliation for that scope. Source staging data remains retained for audit safety.
- Roles & access groups permission rows by payment scheme and provides an **All** grant that sets
  View, Add, Edit and Delete together.
- Run parameters now includes controlled Scheme Profile setup (name, CMS, adapter, function). A restart
  generates its scheme menu entries.

### 3.8 Generic multi-scheme platform ✅ (branch `recon_platform`)
- **Adapter architecture** (`Recon.Web/Platform/`): `ITransactionAdapter` / `IGlcAdapter` parse scheme- and
  CMS-specific files; `AdapterRegistry` resolves them; `NormalizedTransaction` / `NormalizedGlEntry` are the
  scheme-agnostic models the engine consumes. Mastercard/BankOne parsers were extracted verbatim into
  adapters (`BankOne*Adapter`); a `VisaDemoTransactionAdapter` proves extensibility with zero engine changes.
- **SchemeProfile configuration table**: one row per scheme+CMS pairing (adapter codes, enabled functions,
  match-rule overrides, active flag). Seeded: Mastercard—BankOne (active, real adapters) plus Visa,
  UnionPay, RuPay, Domestic placeholders (active, demo data).
- **Normalized projection**: legacy staging rows are projected into the normalized model
  (`NormalizedProjection`), preserving all BankOne semantics (channel prefixes, MUR posting convention,
  FX fallback chain, settlement-leg routing) outside the engine. The engine reads normalized tables when a
  batch is projected and falls back to legacy loaders otherwise — matching math untouched.
- **Scheme ownership**: `ImportBatch.SchemeProfileId` and `ReconRun.SchemeProfileId` isolate every batch
  and run per scheme; screens filter strictly to their scheme (legacy screens see unscoped Mastercard data).
- **Demo data** (`DemoDataSeeder`): each placeholder scheme gets its own GL dump + three settlement batches
  and three completed runs (ATM/POS/Bulk) with realistic counts and confidence — full-featured screens,
  not stubs. Hashes namespaced `demo-<scheme>-…` so they never collide with real uploads.
- **Navigation**: scheme landing page (`/recon`) with one card per scheme linking directly to each
  function's actual screen; sidebar has collapsible per-scheme groups (arrow toggle, auto-expand on active
  child) between Reconciliation and Administration. Mastercard's menu items route to the original screens
  (`/recon/atm|pos|bulk|network-atm`); other schemes use `/recon/scheme/{id}/{function}` pages that render
  the same `ReconRunner`. Old flat recon menu items retired.
- **Migrations**: `PlatformNormalizedModel` (SchemeProfiles, NormalizedTransactions, NormalizedGlEntries),
  `SchemeProfileFunctions` (Functions column), `SchemeOwnership` (scheme FKs on batches/runs).

---

## 4. Data model (summary)

| Area | Tables |
|---|---|
| Identity / access | `AspNetUsers/Roles/…`, `AppPage`, `AppAccess`, `AppAccessMenu`, `UserAppAccess` |
| Import control | `ImportBatch` (+ `SchemeProfileId`), `ReconRun` (+ `SchemeProfileId`; function, period range, processor/GL/network batch refs, headline results) |
| Archive / freeze | `BatchArchive`, `BatchArchiveTransaction`, `BatchFreeze` (immutable run/result snapshots and frozen scopes) |
| Staging (raw) | `StgDebitSettlement` (ATM+POS), `StgInterchangeFee`, `StgGlEntry`, `StgNetworkT464` |
| Platform (normalized) | `SchemeProfile`, `NormalizedTransaction`, `NormalizedGlEntry` |
| Recon results | `ReconMatch`, `ReconException`, `ReconExceptionComment`, `ReconPassSummary`, `AtmNetworkValidation` |
| Reference | `GlSettlementAccount` (the 6 seeded accounts), `IsoCurrency` |

Full column-level contract in **[DataModel.md](DataModel.md)**.

---

## 5. Key domain facts (for anyone new to the reconciliation)

- Match keys are channel/product-specific: debit uses processor `REFERENCE_NUMBER` == GL
  `TRAN_PARTICULAR_2`; credit uses processor `MICROFILM REF NUMBER` == `TRAN_PARTICULAR_2` from its
  thirteenth character (zero-based `Substring(12)`). GL `REF_NUM` is unusable. Repeated keys are distinct business events when their
  `TRANSACTION_CODE` differs (`01` purchase, `04` cash, `09` credit).
- The six GL accounts: cardholder settlement DR `9849065772/773/774` (MUR/USD/EUR); intermediary
  settlement CR `9849077911/913/912`.
- Reference-only matching would wrongly flag ~74% of ATM (their references aren't in the GL, and ATM
  settles in USD) — hence the amount+date-primary confidence model.

---

## 6. Verification

- Import pipeline tests pass for T464 parsing, ATM channel/NULL/sign, and duplicate batch reuse; focused
  tests cover maker/checker workflow transitions plus T464-to-ATM outcome classification and standalone
  batch creation. Full engine runs continue to use the real March sample files. All 9 tests green after
  the platform refactor (engine matching math untouched; legacy loaders retained as fallback).
- Browser-verified: login/logout + protected redirect, user creation, permission menu, batch results
  3-tab view, exception status + comment persistence, scheme landing page cards, collapsible per-scheme
  sidebar groups, per-scheme demo runs (Visa/UnionPay/RuPay/Domestic).
- Dev DB seeded with runs #5 (ATM), #6 (POS), #7 (Bulk) via the `SeedDevDatabase` utility test, plus
  per-scheme demo runs from `DemoDataSeeder`.

---

## 7. Pending tasks

### High priority
1. **Rebaseline Mastercard credit matching** — run a clean Bulk reconciliation using the corrected
   `MicrofilmRefNumber` ↔ `TranParticular2.Substring(12)` rule; record credit/debit match rates and review
   unmatched credit rows before accepting the new result baseline.
2. ~~Default admin password flow~~ ✅ Local users can change their password from the header’s lock icon;
   the seeded `admin/Admin@123` account should be changed immediately after first sign-in.
3. ~~Queue & audit reporting~~ ✅ Dashboard queue is labelled **My queue**; `/admin/audit-reports` lists
   run, submission, verification, archive and freeze activity with activity, user and period filters.
4. ~~Archive/freeze authorization~~ ✅ Separate **Archive batch** and **Freeze period** Edit grants are
   configurable in Roles & access and enforced by `BatchArchiveService`.

### Medium priority
4. **Intermediary-account reconciliation** — v1 matches against cardholder-settlement rows only; the
   intermediary settlement accounts (the MasterCard clearing leg) are not yet reconciled.
5. ~~"Make it generic"~~ ✅ **Done** (branch `recon_platform`) — scheme/CMS/CBS specifics now live in
   adapters + `SchemeProfile` configuration; see §3.8 and PLATFORM_ARCHITECTURE.md. Remaining follow-ups:
   route `ImportService` detection through `IAdapterRegistry`, add a SchemeProfile admin screen, and
   replace placeholder adapters with real ones when onboarding Visa/UnionPay/RuPay/domestic schemes.
6. **T464 network file** — initial network ↔ ATM acquiring validation is implemented; confirm analyst
  thresholds and whether it should become a blocking control before verification.
7. **Large-table performance** — data tabs page at 100 rows; consider virtualization/server search for
   exploring 70k–100k row batches. Exceptions grid caps at 500 rows.

### Lower priority / polish
8. **Broader visual design pass** — further refine spacing, empty states, table density, mobile.
9. **Remove or relabel** the now-secondary global `/import` and `/batches` pages (superseded by the
   batch flow).
10. **Audit log & FGAC hardening** — per-record access control and an activity log.
11. **Data lifecycle** — delete/archive old batches; re-run/replace a batch in place.
12. **Deployment** — hosting, HTTPS, connection-string/secrets config, AD settings for production.

---

## 8. Known limitations / notes

- Foreign-currency matching uses the settlement amount/currency first, then billing amount/currency,
  when a transaction currency has no configured cardholder-settlement GL account. The original transaction
  amount remains on the match record for traceability.
- The app runs with the seeded admin until more users are provisioned; **change the default password**.
- Sample files live in `SharedFiles/` (git-ignored) and were renamed to `0-GL`, `1-ATM`, `2. POS`,
  `3-INTERCHANGE`, `5-T464`.
- Writes via raw `sqlcmd` need `SET QUOTED_IDENTIFIER ON` (filtered indexes); the app itself is unaffected.
- Placeholder schemes (Visa/UnionPay/RuPay/Domestic) run on demo data seeded by `DemoDataSeeder`; their
  upload slots accept only BankOne-format files until real adapters are registered. Clear a scheme's demo
  rows (batches + runs with its `SchemeProfileId`) before first production use.
- `ImportService` still performs its own header detection (not yet routed through `IAdapterRegistry`);
  new-scheme files are rejected until that follow-up is done.
