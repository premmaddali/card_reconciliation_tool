# ReconTool — Project Status & Task Register

**BankOne MasterCard Reconciliation Tool** — status as of **2026-08-14**.

A .NET 9 Blazor Server application that automates the (previously manual, Excel-based) reconciliation
of MasterCard card settlement against the general ledger. Built to mirror the SBI_IA Tool stack.

> Companion documents: **[FILE_ANALYSIS.md](FILE_ANALYSIS.md)** (source data) · **[DataModel.md](DataModel.md)**
> (database contract) · **[RECON_ENGINE.md](RECON_ENGINE.md)** (matching logic) ·
> **[IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)** (phase history).

---

## 1. How to run

| | |
|---|---|
| Solution | `C:\Projects\ReconTool\Recon.sln` (`Recon.Web` + `Recon.Web.Tests`) |
| Run | `dotnet run --project Recon.Web` → **http://localhost:5104** (launch profile `recon-web`) |
| CSS build | `npm run build:css` in `Recon.Web` (Tailwind v4) — run after markup changes |
| Database | SQL Server `localhost`, database **`Recon`** (migrations auto-apply on startup) |
| Login | **`admin` / `Admin@123`** — ⚠️ change on first use |
| Tests | `dotnet test` (5 xUnit tests against the real March sample files) |

---

## 2. Technology stack

- **.NET 9**, Blazor Server (Interactive Server render mode)
- **Flowbite Blazor + Tailwind CSS v4** (light/dark, indigo primary)
- **EF Core 9 + SQL Server**, code-first, Fluent API only, `DbContextFactory` pattern, audit interceptor
- **ASP.NET Identity** (local) **+ Active Directory / LDAP** (hybrid), permission-driven menu
- **ClosedXML** (Excel import), **QuestPDF** + **HtmlSanitizer** (present; export screens pending)

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
- Verified: row counts exact (11,533 / 107,544 / 71,641 / 22,987 / 106,282).

### 3.3 Reconciliation engine ✅
- **Three independent functions**, each a processor file → GL: **ATM**, **POS**, **Bulk (Interchange)**.
- Scopes GL to the **six seeded settlement accounts** (cardholder DR + intermediary CR, per MUR/USD/EUR),
  channel via the `POS/`·`CASH/` particular prefix, routed by currency.
- **Weighted confidence match** — amount + date primary (45 + 33), reference + channel add weight
  (17 + 5); threshold 70; reference-identity catches amount mismatches. Weights configurable in
  `appsettings` (`MatchWeights`).
- Persists `ReconMatch`, `ReconException`, `ReconPassSummary` (daily **and** monthly, per currency,
  non-GL currencies bucketed to "Other").
- Verified on real March data: **POS 54,623/71,641 · ATM 5,742/22,987 · Bulk 62,385/107,544**.

### 3.4 Batch-centric workflow ✅ (current UX)
- Each recon screen = **"New reconciliation batch"**: pick a **period (from/to)**, upload **TRX** + **GL**
  files inline, **Run**. Validates file types; runs the engine; navigates to results.
- **Duplicate-period warning** (function + date range) with non-blocking "Run anyway".
- **Results view = 3 tabs**: **Transactions** (TRX rows), **GL entries** (relevant rows only, filtered
  to accounts + channel), **Reconciliation** (stat cards + currency summary + exceptions). Paged tables.
- Batches list per function; all values persisted and reopenable.

### 3.5 Exception workflow ✅
- Filterable exceptions grid (by type + status); drill-in modal showing processor-vs-GL amounts,
  confidence, narrative.
- Status transitions (Open → Investigating → Resolved / WrittenOff) and a **sanitized comment thread**
  (`ExceptionWorkflowService`, HtmlSanitizer), audited. Verified persisting end-to-end.
- Global `/exceptions` cross-run overview.

### 3.6 Authentication & access ✅ (ported from SBI_IA)
- **Local + AD/LDAP login** (`/auth/login`, `/auth/logout`, `LdapAuthService`), cookie auth (8h sliding),
  lockout after 5 failures; branded login page; every route protected (`[Authorize]` + `RedirectToLogin`).
- **Permission-driven menu** (`PermissionService`) — sidebar built from each user's page grants.
- Identity model: `AppUser/AppRole/AppAccess/AppAccessMenu/AppPage/UserAppAccess`.
- **Seeded**: 13 pages, 4 roles (Administrator, Recon Manager, Recon Officer, Viewer) with default
  grants, admin user with full access.
- **Admin screens**: `/admin/users` (create local users, assign roles, enable/disable) and
  `/admin/roles` (per-page permission matrix). Verified in browser.

### 3.7 Dashboard ✅
- Home dashboard: stat tiles (files, rows staged, sources loaded, runs), recent completed runs, quick
  links to each recon function.

---

## 4. Data model (summary)

| Area | Tables |
|---|---|
| Identity / access | `AspNetUsers/Roles/…`, `AppPage`, `AppAccess`, `AppAccessMenu`, `UserAppAccess` |
| Import control | `ImportBatch`, `ReconRun` (function, period range, batch refs, headline results) |
| Staging (raw) | `StgDebitSettlement` (ATM+POS), `StgInterchangeFee`, `StgGlEntry`, `StgNetworkT464` |
| Recon results | `ReconMatch`, `ReconException`, `ReconExceptionComment`, `ReconPassSummary` |
| Reference | `GlSettlementAccount` (the 6 seeded accounts) |

Full column-level contract in **[DataModel.md](DataModel.md)**.

---

## 5. Key domain facts (for anyone new to the reconciliation)

- Match key: processor `REFERENCE_NUMBER` == GL `TRAN_PARTICULAR_2` on the cardholder-settlement
  accounts; GL `REF_NUM` is unusable (all NULL). GL `TRAN_PARTICULAR` prefix separates `POS/` vs `CASH/`.
- The six GL accounts: cardholder settlement DR `9849065772/773/774` (MUR/USD/EUR); intermediary
  settlement CR `9849077911/913/912`.
- Reference-only matching would wrongly flag ~74% of ATM (their references aren't in the GL, and ATM
  settles in USD) — hence the amount+date-primary confidence model.

---

## 6. Verification

- **5 xUnit tests pass** (`Recon.Web.Tests`): T464 parse + normalization, ATM channel/NULL/sign,
  duplicate handling, and a full POS + ATM engine run against the real sample.
- Browser-verified: login/logout + protected redirect, user creation, permission menu, batch results
  3-tab view, exception status + comment persistence.
- Dev DB seeded with runs #5 (ATM), #6 (POS), #7 (Bulk) via the `SeedDevDatabase` utility test.

---

## 7. Pending tasks

### High priority
1. **Excel / PDF export** — export exceptions and the run summary (QuestPDF + ClosedXML are already
   referenced). Analysts will expect a downloadable exception report.
2. **Change default admin password** flow — force/allow password change; currently `admin/Admin@123` is
   seeded and must be changed manually.

### Medium priority
4. **Intermediary-account reconciliation** — v1 matches against cardholder-settlement rows only; the
   intermediary settlement accounts (the MasterCard clearing leg) are not yet reconciled.
5. **"Make it generic"** — extract the MasterCard specifics (accounts, channel prefixes, weights, source
   layouts) into a configurable "scheme profile" so other reconciliations can be added.
6. **T464 network file** — role still undecided (parked by request); decide whether/how it reconciles
   (e.g., network ↔ ATM acquiring).
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
