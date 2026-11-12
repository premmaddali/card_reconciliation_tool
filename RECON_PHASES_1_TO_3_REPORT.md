# Release Notes & Implementation Report: Phases 1 to 3

**Date:** November 12, 2026  
**Scope:** Exception Investigation Deep-Linking, Re-Reconciliation State Synchronization, and Consolidated Cross-Batch Reporting.

---

## Executive Summary
This document summarizes the architectural fixes and feature enhancements implemented across **Phases 1, 2, and 3**. The updates resolve circuit crashes when inspecting batches without GL files, ensure manual exception investigations persist across re-reconciliation passes, and introduce a consolidated multi-batch exceptions report with Excel and CSV exports.

---

## Phase 1: Investigation & Batch Exceptions List Safeguards

### Problem
1. **Navigation Misalignment:** Navigating from the global `/exceptions` page opened `RunDetail.razor` on the default reconciliation tab (`filter=all`), requiring analysts to manually re-filter for exceptions.
2. **Circuit Termination on Missing GL Batches:** Opening a run without a GL file (e.g. processor-only imports or standalone scheme tests) or an archived consolidated run caused `ReconAnalysisInputs.cs` and `RunDetail.razor` to throw unhandled exceptions (`"This batch has no stored GL source"`), abruptly killing the Blazor SignalR circuit.
3. **Unguarded Duplicate Audits:** Duplicate query lookups assumed `_run.GlBatchId` was non-null, crashing when evaluating runs without GL attachments.

### Implementation Details
- **Exceptions Direct Deep-Link:**
  - Added query parameter `[SupplyParameterFromQuery(Name = "filter")] public string? InitialFilter { get; set; }` to `RunDetail.razor`.
  - When accessed with `?filter=exception`, `_matchFilter` defaults to `"exception"`.
  - Updated `Exceptions.razor` row click handler to navigate to `/recon/run/{id}?filter=exception`.
- **Fault-Tolerant Input Resolution (`ReconAnalysisInputs.cs`):**
  - Replaced hard exceptions with safe fallbacks: if GL batch metadata is missing or partial, it gracefully returns empty GL collections rather than crashing the component.
- **Defensive Lifecycle Guards (`RunDetail.razor`):**
  - Wrapped staging and GL duplicate queries in `if (_run.GlBatchId is int glBatchId)` null checks.
  - Wrapped `OnParametersSetAsync` in a `try/catch` block that renders a visible warning banner (`_loadError`) rather than severing the Blazor WebSocket circuit.

### Files Modified
- `Recon.Web/Components/Pages/Exceptions.razor`
- `Recon.Web/Components/Pages/Reconcile/RunDetail.razor`
- `Recon.Web/Services/Recon/ReconAnalysisInputs.cs`

---

## Phase 2: Exceptions & Source Record Persistence During Re-Reconciliation

### Problem
1. **Loss of Manual Investigation Work:** When re-running reconciliation or executing subsequent passes for an existing batch or period, `ReconEngine` generated fresh `ReconException` records with `Status = Open`.
2. Any manual resolutions, investigation statuses (`Investigating`, `Resolved`, `WrittenOff`), resolver audit timestamps, or analyst notes (`ReconExceptionComment`) were wiped out, forcing analysts to re-investigate settled items.

### Implementation Details
- **Historical Resolution Carry-Forward (`ReconEngine.cs`):**
  - Implemented `CarryForwardResolutionsAsync(...)` in `ReconEngine`.
  - Before committing newly generated exceptions, it queries previous runs sharing the same `ProcessorBatchId`, `GlBatchId`, or source analysis run ID.
  - Matches records by `ProcessorStagingId` or `GlStagingId`.
  - Automatically copies forward:
    - `Status` (`Resolved`, `Investigating`, `WrittenOff`)
    - Resolver identity (`ResolvedBy`) and timestamp (`ResolvedDate`)
    - Narrative history annotations (`[Prior resolution: ...]`)
    - All associated analyst comments (`ReconExceptionComment`)
- **Maintains Zero-Schema Footprint:**
  - Uses existing database structures without requiring breaking schema migrations or table locks.

### Files Modified
- `Recon.Web/Services/Recon/ReconEngine.cs`

---

## Phase 3: Consolidated Cross-Batch Exceptions Report ("Batch Reports")

### Problem
- Users could only inspect individual exception line items by drilling into a single batch at a time (`/recon/run/{id}`). 
- There was no unified cross-batch view to search, filter, or export exception records across multiple runs, date ranges, or payment schemes simultaneously.

### Implementation Details
- **Consolidated Batch Reports Screen (`BatchReports.razor`):**
  - New route: `/reports/batch-exceptions`.
  - **Filter Controls:**
    - Function filter: All, Bulk (`InterchangeToGl`), ATM (`AtmToGl`), POS (`PosToGl`), Network (`NetworkToAtm`).
    - Exception status: `Open`, `Investigating`, `Resolved`, `WrittenOff`.
    - Exception type: `No GL match`, `GL unmatched`, `Amount mismatch`, `Ambiguous`, `Duplicate`.
    - Currency filter based on active system currencies.
    - Transaction date range pickers (`From` and `To`).
    - Free-text search matching reference numbers, processor accounts, and GL accounts.
  - **Interactive Batch Scope Multi-Select:**
    - Dynamic chips showing batch ID, period key, and exception counts.
    - Includes "Select all" and "Clear" scope triggers.
  - **Unified Table Layout:**
    - Standardized line-item view matching batch detail exceptions: Batch link, Status badge, Type, Reference, Account details, Processor Amount, GL Amount, Difference, Currency, Date, Narrative, and "Open" direct navigation action.
    - Server-side pagination (50 items per page).
- **Streamed Export Endpoints (`Program.cs`):**
  - `GET /api/reports/batch-exceptions.xlsx`: Uses `ClosedXML` to generate formatted Excel spreadsheets with headers, numeric currency formatting, and auto-adjusted columns.
  - `GET /api/reports/batch-exceptions.csv`: High-performance UTF-8 CSV stream for large-volume reporting.
  - Both endpoints enforce authentication and permission validation (`EXCEPTIONS`).
- **Sidebar Navigation & Permissions (`DbSeeder.cs`):**
  - Registered `BATCHREPORTS` ("Batch reports") under the **Reconciliation** group in `DbSeeder.cs`.
  - Configured baseline permissions for `Administrator`, `Recon Manager`, `Recon Officer`, and `Viewer`.
- **Styling (`app.min.css`):**
  - Recompiled Tailwind CSS bundle using `npm run build:css`.

### Files Modified / Created
- `Recon.Web/Components/Pages/BatchReports.razor` *(New)*
- `Recon.Web/Program.cs`
- `Recon.Web/Data/DbSeeder.cs`
- `Recon.Web/wwwroot/css/app.min.css`

---

## Verification & Deployment Checklist
1. **Build Verification:**
   - Execute `npm run build:css` in `Recon.Web` if any Tailwind classes change.
   - Run `dotnet build` to ensure all projects compile cleanly.
2. **Runtime Restart:**
   - Stop: `powershell -ExecutionPolicy Bypass -File .\Scripts\Stop-ReconTool.ps1`
   - Start: `powershell -ExecutionPolicy Bypass -File .\Scripts\Start-ReconTool.ps1`
3. **Database Seeding:**
   - On application startup, `DbSeeder` automatically registers the new `BATCHREPORTS` page and access grants.
