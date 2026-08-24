# ReconTool — Data Model (Multi-Scheme Reconciliation Platform)

**EF Core 9, code-first, Fluent API only**, provider **SQL Server**. Mirrors the SBI_IA Tool
conventions so the two apps are structurally identical. Types are shown as .NET → indicative SQL.

> Contract document, built step by step. Source data = five BankOne sample exports profiled in
> [FILE_ANALYSIS.md](FILE_ANALYSIS.md). This model is the database contract the Blazor app implements.
> Section **A2** covers the multi-scheme platform tables added on branch `recon_platform`
> (see [PLATFORM_ARCHITECTURE.md](PLATFORM_ARCHITECTURE.md)).

## Conventions (standing, unless overridden)

- **PK**: `int` identity for high-volume staging/result tables (`{Entity}Id`); `Guid` only where it
  mirrors Identity. Configured with `HasKey` in Fluent.
- **Fluent API only** — one `IEntityTypeConfiguration<T>` per entity in `Recon.Web/Data/Configurations/`,
  applied with `ApplyConfigurationsFromAssembly`. No data annotations, no provider-specific defaults.
- **Audit columns on every non-staging table**: `CreatedBy Guid NOT NULL`, `CreatedDate datetime2(3) NOT NULL`,
  `UpdatedBy Guid NULL`, `UpdatedDate datetime2(3) NULL`, stamped by a `SaveChanges` interceptor.
- **Staging tables are immutable raw captures** — they carry `ImportBatchId`, `SourceRowNumber`, and the
  parsed/normalized columns, but no audit/soft-delete (the `ImportBatch` row above them is audited).
- **Enums** stored as `tinyint`.
- **Money**: every amount is stored twice — the raw text as-received (`*Raw`) and a normalized signed
  `decimal(19,4)`. Currencies are numeric ISO (`480`=MUR, `840`=USD, `978`=EUR) stored as `int` +
  a 3-letter `*CcyCode` for display.
- **Reference number** (`ReferenceNumber`, `char(12)`) is the transaction join key across the network
  and processor files, indexed on every staging table that has it.

---

## A. Import & run control

### ImportBatch
One uploaded file. Type-checked, row-counted, content-hashed for duplicate-upload detection.

| Column | .NET → SQL | Notes |
|---|---|---|
| ImportBatchId | int PK | identity |
| SourceType | enum tinyint | `NetworkT464`, `InterchangeFee`, `PosSettlement`, `AtmSettlement`, `GlDump` |
| FileName | string(260) | original upload name |
| ContentHash | string(64) | SHA-256; unique per SourceType to block re-upload |
| PeriodKey | string(7) | `YYYY-MM` the file covers, e.g. `2026-03` |
| RowCount | int | data rows parsed |
| Status | enum tinyint | `Uploaded`, `Parsed`, `Failed` |
| ParseError | string(max) null | first fatal parse error, if any |
| SchemeProfileId | int FK null | owning scheme (platform); null = legacy unscoped Mastercard data |
| UploadedBy / UploadedDate | Guid / datetime2(3) | audited |

### ReconRun
A reconciliation execution for one `PeriodKey`, binding one batch of each required type.

| Column | .NET → SQL | Notes |
|---|---|---|
| ReconRunId | int PK | identity |
| Function | enum tinyint | `AtmToGl`, `PosToGl`, `InterchangeToGl`, `NetworkToAtm` |
| PeriodKey / PeriodStart / PeriodEnd | string(7) / date null | run period and user-selected range |
| Status | enum tinyint | `Draft`, `Running`, `Completed`, `Failed` |
| ProcessorBatchId | int FK | ATM, POS, or Interchange batch, depending on function |
| GlBatchId | int FK null | GL batch for the three processor → GL functions; null for `NetworkToAtm` |
| NetworkBatchId | int FK null | T464 batch for `NetworkToAtm`; null otherwise |
| SchemeProfileId | int FK null | owning scheme (platform); null = legacy unscoped Mastercard data |
| StartedDate / CompletedDate | datetime2(3) null | |
| audit cols | | IAuditable |

---

## A2. Platform (multi-scheme, branch `recon_platform`)

### SchemeProfile
One configured reconciliation scheme: a payment scheme + CMS pairing with its adapters and enabled functions.
Adding a scheme/CMS = new adapter class + one row here; no engine changes.

| Column | .NET → SQL | Notes |
|---|---|---|
| SchemeProfileId | int PK | identity |
| Name / Scheme / Cms | string(100)/50/50 | e.g. `Mastercard — BankOne`; unique (Scheme, Cms) |
| TransactionAdapterCode | string(100) | `ITransactionAdapter.Code` for CMS files |
| GlcAdapterCode | string(100) | `IGlcAdapter.Code` for GL files |
| Functions | string(200) | `;`-separated enabled functions, e.g. `AtmToGl;PosToGl;InterchangeToGl;NetworkToAtm` |
| MatchThresholdOverride / DateToleranceDaysOverride | decimal(5,2) / int null | sparse overrides; null = inherit global `MatchWeights` |
| IsActive | bit | inactive profiles hide their scheme from navigation |
| audit cols | | IAuditable |

### NormalizedTransaction
Scheme-agnostic transaction model the engine consumes (populated by projection from legacy staging or
directly by future adapters). Immutable, no audit columns.

| Column | .NET → SQL | Notes |
|---|---|---|
| NormalizedTransactionId | bigint PK | identity |
| ImportBatchId / SourceRowNumber | int | provenance |
| SourceTable / SourceStagingId | string(64) / int null | legacy staging row this was projected from |
| Reference / ExternalReference / AuthorizationCode / TraceNumber / BatchNumber | string | identity fields |
| TransactionDate / PostingDate / SettlementDate / TransactionDateTime | date ×3 / datetime2 null | |
| Amount / Currency | decimal(19,4) / int null | signed, normalized |
| BillingAmount / BillingCurrency / SettlementAmount / SettlementCurrency | decimal(19,4) / int null | |
| DebitCreditIndicator | string(2) null | D/C as received |
| Merchant / Terminal / Acquirer / Issuer / MaskedCardNumber / CardType / Scheme / Processor | string | parties & card |
| Account / Description / TransactionType | string | ledger routing |
| IsCash / IsPos | bit | channel flags derived by the adapter |
| SettlementLeg | tinyint | 1 = debit cardholder, 2 = credit intermediary |

Indexes: `(ImportBatchId, Reference)`, `(ImportBatchId, Currency, Amount)`.

### NormalizedGlEntry
Scheme-agnostic GL entry model from a CBS adapter.

| Column | .NET → SQL | Notes |
|---|---|---|
| NormalizedGlEntryId | bigint PK | identity |
| ImportBatchId / SourceRowNumber / SourceTable / SourceStagingId | | provenance |
| Account | string(48) | GL account number |
| PostingDate / PostingDateTime | date / datetime2 null | |
| Amount / Currency | decimal(19,4) / int | signed; BankOne cardholder accounts post in MUR (480) |
| Reference / Description / DebitCreditIndicator | string | match key + narrative |
| IsCash / IsPos / SettlementLeg | bit ×2 / tinyint | channel + leg routed by the adapter |

Indexes: `(ImportBatchId, Reference)`, `(ImportBatchId, Account)`.

---

## B. Staging tables (normalized raw)

### StgNetworkT464  (from `Copy of T464.xlsx`, 11.5k rows)
Acquiring-side MasterCard network settlement (`MTI=FREC`). Fixed-width padded source; parsed here.

| Column | Source col | Type | Notes |
|---|---|---|---|
| StgNetworkT464Id | — | int PK | |
| ImportBatchId / SourceRowNumber | — | int | provenance |
| HeaderCode | Header Code | string(8) | e.g. `4802A` |
| MessageTypeIndicator | Message Type Indicator | string(8) | `FREC` |
| SwitchSerialNumber | Switch Serial Number | string(12) | |
| ProcessorId | Processor ID | string(8) | |
| TransactionDate | Transaction Date | date | `MMDDYY`/`DDMMYY` → parsed |
| TransactionTime | Transaction Time | time | `HHMMSS` |
| Pan | PAN | string(19) | masked `536895XXXXXX7777` |
| ProcessingCode | Processing Code | string(6) | `011000/012000`=cash |
| TraceNumber | Trace Number | string(12) | |
| MerchantType | Merchant Type | string(4) | MCC |
| ReferenceNumber | Reference Number | char(12) | **join key**, indexed |
| AcquirerInstitutionId | Acquirer InstitutionID | string(12) | |
| TerminalId | Terminal ID | string(16) | |
| ResponseCode | Response Code | string(4) | |
| TxnCcy / TxnCcyCode | Currency Code Transaction | int / string(3) | |
| CompletedAmtLocalRaw | Completed Amt TransLocal | string(16) | zero-padded text |
| CompletedAmtLocal | + Indicator (C/D) | decimal(19,4) | signed |
| SettleCcy / SettleCcyCode | Currency Code Settlement | int / string(3) | |
| ConversionRate | Conversion Rate Settlement | decimal(19,8) | |
| CompletedAmtSettleRaw / CompletedAmtSettle | Completed Amt Settlement (+Ind) | string / decimal(19,4) | signed |
| InterchangeFeeRaw / InterchangeFee | Interchange Fee (+Ind) | string / decimal(19,4) | signed |
| RequestedAmtLocal | Requested Amt TransLocal | decimal(19,4) | |

### StgDebitSettlement  (from ATM + POS `DEBIT_SETTELMENT_REPORT.xlsx`)
**Consolidation:** ATM (22.9k) and POS (71.6k) files share an identical 22-column schema, so they load
into one table with a `Channel` discriminator. *(Value-add flag — confirm.)*

| Column | Source col | Type | Notes |
|---|---|---|---|
| StgDebitSettlementId | — | int PK | |
| ImportBatchId / SourceRowNumber | — | int | |
| Channel | (file) | enum tinyint | `Atm` / `Pos` |
| ExtractedDate | EXTRACTED_DATE | date | |
| MerchantAcronym | MERCHANT_ACRONYM | string(64) | |
| ProcessingCode | PROCESSING_CODE | string(6) | `000000`=purchase, `01x000`=cash |
| ReferenceNumber | REFERENCE_NUMBER | char(12) | **join key**, indexed |
| CardNumberHashed | CARD_NUMBER_HASHED | string(48) | stable per-card key |
| CardNumber | CARD_NUMBER | string(19) | masked |
| Wording | WORDING | string(64) | product wording |
| ProductType | PRODUCT_TYPE | string(4) | `DC`/`CR` |
| TransactionSign | TRANSACTION_SIGN | char(1) | D/C |
| TransactionWording | TRANSACTION_WORDING | string(64) | PURCHASE / ATM WITHDRAWAL |
| TransactionDate | TRANSACTION_DATE | date | |
| ProcessingDate | PROCESSING_DATE | date | recon window key |
| TxnAmountRaw / TxnAmount | TRANSACTION_AMOUNT (×SIGN) | string / decimal(19,4) | signed |
| TxnCcy / TxnCcyCode | TRANSACTION_CURRENCY | int / string(3) | |
| InterchangeFeesRaw / InterchangeFees | INTERCHANGE_FEES (+SIGN) | string / decimal(19,4) | `NULL` → null |
| IntFeesCcy / IntFeesCcyCode | INTERCHANGE_FEES_CURRENCY | int / string(3) | |
| BillingAmount / BillingCcy | BILLING_AMOUNT / _CURRENCY | decimal / int | |
| ConversionFees | CONVERSION_FEES | decimal(19,4) null | |
| SettlementAmount / SettlementCcy | SETTLEMENT_AMOUNT / _CURRENCY | decimal / int | |

### StgInterchangeFee  (from `INTERCHANGE_FEE_REPORT_CREDIT_AND_DEBIT.xlsx`, 107k rows)
Processor master transaction report — **superset** of POS+ATM debit plus credit-card rows. Header is on
sheet row 3 (title row 1, blank row 2).

| Column | Source col | Type | Notes |
|---|---|---|---|
| StgInterchangeFeeId | — | int PK | |
| ImportBatchId / SourceRowNumber | — | int | |
| MicrofilmRefNumber | MICROFILM REF NUMBER | string(24) | |
| MicrofilmRefSeq | MICROFILM REF SEQ | int | |
| ReferenceNumber | REFERENCE NUMBER | char(12) | **join key**, indexed |
| CardNumberHashed | CARD NUMBER | string(48) | hashed |
| ProductCode | PRODUCT CODE | string(4) | |
| ProductType | PRODUCT TYPE | string(4) | `DC`/`CR` — separates debit vs credit |
| ClientCode | CLIENT CODE | string(16) | |
| CardAccountNumber | CARD ACCOUNT NUMBER | string(24) | |
| TransactionDate | TRANSACTION DATE | datetime2 | |
| ProcessingDate | PROCESSING DATE | date | |
| TransactionCode | TRANSACTION CODE | string(4) | `01`=purchase, `04`=cash |
| TransactionWording | TRANSACTION WORDING | string(64) | |
| TxnAmount / TxnCcy | TRANSACTION AMOUNT / CURRENCY | decimal / int | |
| BillingAmount / BillingCcy | BILLING AMOUNT / CURRENCY | decimal / int | |
| SettlementAmount / SettlementCcy | SETTLEMENT AMOUNT / CURRENCY | decimal / int | |
| InterchangeFeesRaw / InterchangeFees | INTERCHANGE FEES (+SIGN) | string / decimal(19,4) | signed |
| IntFeesCcy | INTERCHANGE FEES CURRENCY | int | |
| OrigineCode | ORIGINE CODE | string(4) | |
| TerminalId | TERMINAL ID | string(16) | |
| MerchantAcronym | MERCHANT ACRONYM | string(64) | |
| IssMarkupAmount | ISS MARKUP AMOUNT | decimal(19,4) null | |
| ConversionFees | CONVERSION FEES | decimal(19,4) null | |

### StgGlEntry  (from `GL_Dump.xlsx`, 106k rows)
Finacle general-ledger postings. `RefNum` is populated on only ~2.7k rows, so GL reconciles at the
**account/total** level, not per-transaction.

| Column | Source col | Type | Notes |
|---|---|---|---|
| StgGlEntryId | — | int PK | |
| ImportBatchId / SourceRowNumber | — | int | |
| SolId | SOL_ID | string(8) | branch/sol |
| AcctNum | ACCT_NUM | string(20) | |
| GlSubHeadCode | GL_SUB_HEAD_CODE | string(8) | `63530/63531/44006/54000…`, indexed |
| GlAccount | GL_ACCOUNT | string(20) | |
| AcctName | ACCT_NAME | string(64) | |
| TranDate | TRAN_DATE | date | indexed |
| TranId | TRAN_ID | string(16) | |
| RefNum | REF_NUM | string(24) null | `NULL` string → null |
| TranParticular / TranParticular2 | TRAN_PARTICULAR(_2) | string(128) | |
| Debit | DEBIT | decimal(19,4) | |
| Credit | CREDIT | decimal(19,4) | |
| TranAmt | TRAN_AMT | decimal(19,4) | |
| TranCcyCode | TRAN_CRNCY_CODE | string(3) | `MUR/USD/EUR` |
| Rate / MurAmt | RATE / MUR_AMT | decimal | FX + MUR equivalent |
| EntryUserId / VfdUserId | ENTRY/VFD_USER_ID | string(16) | |
| EntryDate / VfdDate | ENTRY/VFD_DATE | datetime2 | |
| PartTranSrlNum | PART_TRAN_SRL_NUM | int | |

**Reference GL accounts** (seeded lookup `GlAccountMap`): `63530` Cardholder Settlement · `63531`
Intermediary Settlement · `44006/44001` Interchange Fee Receivable · `91121/91122` MC Charges ·
`54000` Citibank NY/London Nostro (USD/EUR).

---

## C. Reconciliation results

### AtmNetworkValidation
One persisted outcome per nonblank T464/ATM reference for a standalone `NetworkToAtm` run. It keeps both
staging identifiers, reference occurrence counts, amount/currency/fee/date evidence, and a narrative.

| Column | Type | Notes |
|---|---|---|
| AtmNetworkValidationId / ReconRunId | int PK / FK | identity and owning network run |
| NetworkStagingId / AtmStagingId | int null | source row identifiers |
| ReferenceNumber | char(12) | comparison key |
| Status | enum tinyint | `Matched`, amount/fee/currency mismatch, missing network/ATM, duplicate |
| Network* / Atm* | amount, ccy, fee, date | normalized comparison evidence |
| NetworkReferenceCount / AtmReferenceCount | int | duplicate-review evidence |
| Narrative | string(512) | analyst-facing outcome description |

### ReconMatch
One processor-to-GL outcome for the three GL-backed functions. The standalone Network ↔ ATM function uses
`AtmNetworkValidation` instead.

| Column | Type | Notes |
|---|---|---|
| ReconMatchId | int PK | |
| ReconRunId | int FK | |
| Pass | enum tinyint | `NetworkVsProcessor`, `ProcessorCompleteness`, `ProcessorVsGl` |
| ReferenceNumber | char(12) null | null for GL total-level rows |
| LeftStagingId / LeftSource | int / enum | e.g. T464 row |
| RightStagingId / RightSource | int / enum | e.g. ATM row |
| AmountLeft / AmountRight | decimal(19,4) | |
| FeeLeft / FeeRight | decimal(19,4) null | |
| Ccy | int | |
| MatchStatus | enum tinyint | `Matched`, `AmountMismatch`, `FeeMismatch`, `MissingLeft`, `MissingRight`, `Duplicate` |

### ReconException
Every non-`Matched` outcome surfaces here for analyst workflow.

| Column | Type | Notes |
|---|---|---|
| ReconExceptionId | int PK | |
| ReconRunId | int FK | |
| Pass | enum tinyint | |
| ReferenceNumber | char(12) null | |
| ExceptionType | enum tinyint | mirrors MatchStatus non-matched values |
| Ccy | int | |
| ExpectedAmount / ActualAmount | decimal(19,4) null | |
| Difference | decimal(19,4) null | |
| Narrative | string(512) | auto-generated description |
| Status | enum tinyint | `Open`, `Investigating`, `Resolved`, `WrittenOff` |
| ResolvedBy / ResolvedDate | Guid / datetime2 null | |
| audit cols | | IAuditable |

### ReconExceptionComment
Analyst notes thread on an exception (HtmlSanitizer-cleaned, like SBI_IA audit notes).

| Column | Type | Notes |
|---|---|---|
| ReconExceptionCommentId | int PK | |
| ReconExceptionId | int FK | |
| Body | string(max) | sanitized |
| audit cols | | IAuditable |

### ReconPassSummary
Frozen roll-up per run+pass for the dashboard (counts + amounts by status/currency).

| Column | Type |
|---|---|
| ReconPassSummaryId | int PK |
| ReconRunId / Pass | int / enum |
| MatchedCount / MatchedAmount | int / decimal(19,4) |
| ExceptionCount / ExceptionAmount | int / decimal(19,4) |
| Ccy | int |

---

## D. Identity (carried over from SBI_IA, unchanged)
`AspNetUsers/Roles`, `AppAccess`, `AppAccessMenu`, `AppPage`, `UserAppAccess` — same permission-driven
menu and local+AD login. New `AppPage` rows: Upload, Runs, Exceptions, Dashboard, GL Mapping (admin).

---

## Changelog
- **Step 0 (2026-08-14)** — Initial model from five-file profiling: import control, 4 staging tables
  (ATM+POS consolidated), 3-pass result model, exception workflow, identity reuse.
- **Platform (2026-08-24, branch `recon_platform`)** — Added `SchemeProfile`, `NormalizedTransaction`,
  `NormalizedGlEntry` (section A2); `SchemeProfileId` ownership columns on `ImportBatch` and `ReconRun`.
  Migrations: `PlatformNormalizedModel`, `SchemeProfileFunctions`, `SchemeOwnership`. Legacy staging
  tables unchanged; engine matching math untouched.
