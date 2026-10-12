# Reconciliation Rule Updates (2026-08-25, corrected 2026-08-27)

## On-Us ATM withdrawals (2026-08-27)

Interchange rows with **`ORIGINE CODE = 0`** are On-Us ATM withdrawals. They are retained for analyst review in the **On-Us ATM transactions** results tab, but are excluded from Bulk-to-GL reconciliation, reconciliation counts, summaries, and exceptions because they have no corresponding GL posting.

> **Measured correction (authoritative):** the supplied credit examples show a twelve-character BankOne
> posting prefix, such as `270326PPMNFT`, before the Microfilm Reference. Mastercard credit matching uses
> `TRAN_PARTICULAR_2.Substring(12)`. Earlier references to `Substring(6)`, the seventh character, or a
> six-character prefix are superseded.

This document records business-rule corrections that supersede previous assumptions in the project documentation. These updates must be reflected throughout the documentation and implemented consistently to ensure Codex generates the correct reconciliation logic.

---

# 1. Credit Card Matching Rules

## Previous Assumption

All processor transactions use `REFERENCE_NUMBER` as the primary reconciliation key.

## Updated Rule

**Credit card transactions do not use `REFERENCE_NUMBER` for GL matching.**

Instead:

- Processor key: **`MICROFILM REF NUMBER`**
- GL key: **`TRAN_PARTICULAR_2`**, starting from the **13th character**
- The first twelve characters of `TRAN_PARTICULAR_2` are the BankOne posting prefix and **must be ignored** during credit reconciliation.
- This rule applies specifically to **credit-card reconciliation**.

### Example

```text
Processor:
MICROFILM REF NUMBER = 123456789012

GL:
TRAN_PARTICULAR_2 = 270326PPMNFT123456789012

Comparison:
TRAN_PARTICULAR_2.Substring(12)
→ 123456789012
```

---

# 2. ATM General Ledger

## Previous Assumption

ATM transactions participate in ATM → GL reconciliation.

## Updated Rule

**There is currently no ATM General Ledger.**

Therefore:

- ATM transactions are **not reconciled directly against GL**.
- ATM reconciliation is limited to:

```
T464 Network Settlement
            ↕
ATM Settlement
```

- Processor → GL reconciliation only applies where a corresponding GL source exists.

---

# 3. Duplicate Transaction Handling

## Previous Assumption

Duplicate references represent duplicate transactions.

## Updated Rule

A repeated transaction reference does **not** necessarily indicate a duplicate.

Repeated references may represent different business events, including:

- Original transaction
- Financial credit
- Reversal

These must be differentiated using **`TRANSACTION_CODE`**.

Known values include:

| Transaction Code | Meaning |
|------------------|---------|
| **01** | Purchase |
| **04** | Cash Transaction |
| **09** | Credit |

Therefore:

- Duplicate detection **must not rely solely on Reference Number**.
- `TRANSACTION_CODE` must be included when determining transaction identity.
- Transactions with the same reference but different transaction codes must be treated as separate business events unless business rules explicitly state otherwise.

---

# Documentation Changes Required

The following project documents should be updated.

## RECON_ENGINE.md

Update the reconciliation logic to reflect:

- Credit-card matching uses **Microfilm Reference**.
- GL matching uses `TRAN_PARTICULAR_2` from the 13th character onwards.
- ATM has no GL reconciliation.
- Duplicate analysis includes `TRANSACTION_CODE`.

---

## DataModel.md

Clarify that:

### StgInterchangeFee

`MicrofilmRefNumber`

- Primary reconciliation key for credit-card transactions.
- Used instead of `ReferenceNumber` for GL matching.

### StgGlEntry

`TranParticular2`

- For credit-card reconciliation, matching begins at the **13th character**.
- The first twelve characters are ignored.

---

## FILE_ANALYSIS.md

Document separate reconciliation keys.

### Debit

```
ReferenceNumber
```

### Credit

```
MicrofilmRefNumber
        ↕
TranParticular2.Substring(12)
```

Also document:

- ATM currently has no corresponding GL dataset.

---

## Generic_Multi_Scheme_Reconciliation_Platform.md

Update the matching architecture to state:

Matching keys are **adapter-defined**, not engine-defined.

Examples:

- Debit → Reference Number
- Credit → Microfilm Reference
- Future schemes → Retrieval Reference, Authorization Code, Trace Number, etc.

---

## PLATFORM_ARCHITECTURE.md

Clarify that determining the business matching key is the responsibility of the adapter.

Example:

```text
BankOne Debit Adapter
    MatchKey = ReferenceNumber

BankOne Credit Adapter
    MatchKey = MicrofilmRefNumber

Future Visa Adapter
    MatchKey = RetrievalReferenceNumber
```

The reconciliation engine consumes only the normalized match key.

---

## PROJECT_STATUS.md

Add the following domain facts:

- Credit-card reconciliation uses **Microfilm Reference** rather than `REFERENCE_NUMBER`.
- GL matching uses `TRAN_PARTICULAR_2` beginning from the **13th character**.
- ATM currently has no GL reconciliation.
- Duplicate analysis must consider `TRANSACTION_CODE` (01, 04, 09) and not rely solely on repeated references.

---

## IMPLEMENTATION_PLAN.md

Update the implementation rules to reflect:

- Debit transactions use `ReferenceNumber`.
- Credit transactions use `MicrofilmRefNumber`.
- ATM → GL reconciliation is not applicable because no ATM GL currently exists.
- Duplicate detection incorporates `TRANSACTION_CODE`.

---

# Summary

These are **business-rule corrections**, not documentation improvements.

They affect the reconciliation logic itself and must be treated as authoritative requirements for all future development.

The key changes are:

1. **Credit-card reconciliation uses `MICROFILM REF NUMBER` instead of `REFERENCE_NUMBER`.**
2. **GL matching uses `TRAN_PARTICULAR_2` beginning at the 13th character.**
3. **ATM currently has no GL reconciliation.**
4. **Duplicate detection must incorporate `TRANSACTION_CODE` and not rely solely on repeated references.**
