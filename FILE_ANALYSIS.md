# BankOne MasterCard Reconciliation — Source File Analysis

Profiling of the five sample exports in `SharedFiles/`. Base currency **MUR (ISO 480)**; FX legs USD
(840) and EUR (978). All figures below are measured from the files, not assumed.

## The five files

| # | File | Sheet | Rows | Cols | Role |
|---|---|---|---|---|---|
| 1 | Copy of T464.xlsx | `Output` | 11,533 | 49 | MasterCard **network settlement** (MTI `FREC`), acquiring side |
| 2 | INTERCHANGE_FEE_REPORT_CREDIT_AND_DEBIT (1).xlsx | `INTERCHANGE_FEE…` | 107,546 | 26 | Processor **master** report (credit + debit) — superset |
| 3 | POS MARCH 2026 DEBIT_SETTELMENT_REPORT.xlsx | `Sheet1` | 71,641 | 22 | Processor **POS debit** settlement (purchases) |
| 4 | ATM MARCH 2026 DEBIT_SETTELMENT_REPORT.xlsx | `Sheet1` | 22,987 | 22 | Processor **ATM debit** settlement (withdrawals) |
| 5 | GL_Dump.xlsx | `Sheet1` | 106,282 | 24 | Bank **general ledger** postings (Finacle) |

> Header quirks: the Interchange file has its title in row 1, a blank row 2, and the real header on
> **row 3**. ATM/POS/GL/T464 headers are on row 1.

## Join-key overlap (measured)

`REFERENCE_NUMBER` (12-digit, e.g. `605827366477`) is the debit/network key. Credit-card GL matching uses a separate key:
`MICROFILM REF NUMBER` ↔ `TRAN_PARTICULAR_2.Substring(12)`. The first twelve GL characters are the posting prefix.

| Test | Result | Meaning |
|---|---|---|
| POS ∩ Interchange | 71,030 / 71,031 | POS debit is a clean subset of the master |
| ATM ∩ Interchange | 22,983 / 22,983 | ATM debit is 100% within the master |
| (POS ∪ ATM) ∩ Interchange | 94,009 | debit reports fully contained; ~12k master rows are **credit-only** |
| ATM ∩ T464 | 5,683 | T464 covers cash/ATM network traffic |
| POS ∩ T464 | 4 | T464 is **not** a POS source |

**GL** `REF_NUM` is populated on only **2,734 / 106,282** rows. Debit uses `TRAN_PARTICULAR_2`; credit uses
its value after the first twelve characters. There is currently no ATM GL dataset, so ATM is reconciled only to T464.

## GL account breakdown (by `GL_SUB_HEAD_CODE`)

| Code | Account name | Rows |
|---|---|---|
| 63530 | MASTERCARD CARDHOLDER SETTLEMENT | 85,985 |
| 63531 | MC – INTERMEDIARY SETTLEMENT | 13,613 |
| 54000 | CITIBANK NY / LONDON nostro (USD/EUR) | 5,824 |
| 44006 / 44001 | MC CARD RECEIVABLE / ATM-POS INTERCHANGE FEE | 589 |
| 91121 / 91122 | MC CARD / MCARD CHARGES | 271 |

Currency mix in GL: MUR 95,485 · USD 6,928 · EUR 3,869.

## Data-shape gotchas the importer must handle

- **Amounts are text**, zero-padded (`0000003000.00`), with the **sign in a separate column**
  (`TRANSACTION_SIGN`, `INTERCHANGE_FEES_SIGN`, or the `*Indicator` C/D columns in T464). Normalize to a
  signed `decimal`.
- **Currencies are numeric ISO** (`480/840/978`). Some transactions transact, bill, and settle in
  different currencies (e.g. AliExpress: txn `480`, settle `840`) → **reconcile per currency**.
- Literal string **`"NULL"`** appears in ATM/POS fee columns and GL ref/particular columns → treat as null.
- Three dates per processor row: `TRANSACTION_DATE`, `PROCESSING_DATE`, `EXTRACTED_DATE` — key recon
  windows on **processing/settlement date**; watch cross-period lag (Feb txns settling in March).
- PANs are masked (`2231********4596`); cards also carry a stable `CARD_NUMBER_HASHED`.
- T464 amounts/fields are fixed-width padded strings requiring positional parsing.

## Three-pass reconciliation model

1. **Network ↔ Processor (ATM):** T464 vs ATM Debit Settlement on ref + amount + interchange fee.
2. **Processor completeness:** POS + ATM debit rows must all appear in the Interchange master; isolate
   the credit-only remainder.
3. **Processor ↔ GL:** processor totals by date + currency vs GL movements on `63530/63531/44006`, with
   USD/EUR legs settling through the `54000` nostros.

Each pass emits Matched / Mismatch / Missing / Duplicate outcomes → exception workflow. See
[DataModel.md](DataModel.md) for the persisted schema.
