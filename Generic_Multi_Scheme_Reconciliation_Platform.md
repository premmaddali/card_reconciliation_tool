# Generic Multi-Scheme Reconciliation Platform

## Objective

The current application is a Mastercard reconciliation solution. It
successfully reconciles Mastercard interchange transactions against
General Ledger (GL) transactions.

The objective is **not** to simply add Visa support.

The objective is to transform the application into a **generic
reconciliation platform** capable of supporting multiple payment
schemes, multiple Card Management Systems (CMS), and multiple Core
Banking Systems without requiring changes to the reconciliation engine.

The existing Mastercard implementation should become the **first adapter
implementation** of the new architecture.

All existing functionality must continue to work exactly as it does
today.

------------------------------------------------------------------------

# Existing Product

The application currently provides:

-   End-to-end reconciliation between processor/interchange files and
    General Ledger files.
-   Transaction matching engine.
-   Confidence scoring.
-   Exception classification.
-   Duplicate detection.
-   Dashboard and statistics.
-   Manual matching.
-   Maker/Checker workflow.
-   Approval process.
-   CSV export.
-   Advanced analysis.
-   High-volume batch processing.

The existing reconciliation features must remain unchanged.

------------------------------------------------------------------------

# Vision

The product should become a configurable reconciliation framework.

Instead of coding reconciliation logic for each scheme, the platform
should support:

``` text
               +----------------------+
               | Reconciliation Core  |
               +----------+-----------+
                          |
        +-----------------+----------------+
        |                                  |
  CMS Adapters                     GL Adapters
        |                                  |
  OpenWay CMS                     CBS A
  Compass Plus                    CBS B
  BPC                             Oracle
  Future CMS                      CSV
                                  Excel
```

The reconciliation engine should consume only a **normalized transaction
model**.

It must not know whether a transaction originated from Mastercard, Visa,
UnionPay, RuPay, or a domestic switch.

------------------------------------------------------------------------

# Supported Schemes

The architecture should support:

-   Mastercard
-   Visa
-   UnionPay
-   RuPay
-   Domestic card schemes
-   Future payment schemes

Adding a new scheme should require configuration and a parser rather
than changes to the reconciliation engine.

------------------------------------------------------------------------

# Card Management Systems (CMS)

Transaction files may originate from different Card Management Systems,
including:

-   OpenWay
-   Compass Plus
-   BPC
-   Other CMS platforms

Each CMS produces different layouts.

The platform must support adding new CMS formats independently.

------------------------------------------------------------------------

# Core Banking Systems

GL or Account files may come from different systems, such as:

-   Oracle FLEXCUBE
-   Temenos T24
-   Finacle
-   Other Core Banking Systems
-   CSV exports
-   Excel exports
-   Database extracts

Each system has different layouts.

These should also be implemented as adapters.

------------------------------------------------------------------------

# Normalized Transaction Model

Every imported file should be mapped into a common transaction model
before reconciliation.

Typical fields include:

-   Reference
-   External Reference
-   Transaction Date
-   Posting Date
-   Settlement Date
-   Amount
-   Billing Amount
-   Settlement Amount
-   Currency
-   Billing Currency
-   Settlement Currency
-   Debit/Credit Indicator
-   Merchant
-   Terminal
-   Acquirer
-   Issuer
-   Masked Card Number
-   Card Type
-   Scheme
-   Processor
-   Account
-   Description
-   Transaction Type
-   Authorization Code
-   Trace Number
-   Batch Number

The reconciliation engine should operate exclusively on this normalized
model.

------------------------------------------------------------------------

# File Import Architecture

Replace hard-coded import logic with pluggable import adapters.

Each adapter should define:

-   Supported file type
-   Excel/CSV parser
-   Column mapping
-   Data validation
-   Transformation rules
-   Normalization logic

Future adapters should be added without modifying existing code.

------------------------------------------------------------------------

# Matching Engine

The matching engine should remain generic.

Matching rules should be configurable.

Rules may include combinations of:

-   Reference
-   Amount
-   Currency
-   Account
-   Merchant
-   Transaction Date
-   Settlement Date
-   Billing Amount
-   Settlement Amount
-   Authorization Code
-   Trace Number
-   Card Type

Avoid embedding Mastercard-specific assumptions into the engine.

------------------------------------------------------------------------

# Configuration

The platform should support configurable:

-   Scheme definitions
-   CMS definitions
-   Core Banking definitions
-   File layouts
-   Column mappings
-   Date formats
-   Amount formats
-   Currency mappings
-   Matching rules
-   Matching tolerances
-   Duplicate rules
-   Exception rules

Prefer configuration over code wherever practical.

------------------------------------------------------------------------

# User Workflow

The user workflow should be:

1.  Create a reconciliation batch.
2.  Select the payment scheme.
3.  Select the CMS type.
4.  Upload the CMS transaction file.
5.  Select the GL/Core Banking source type.
6.  Upload the GL file.
7.  Import data.
8.  Normalize records.
9.  Reconcile.
10. Review exceptions.
11. Perform manual matching where required.
12. Complete approval workflow.
13. Export results.

------------------------------------------------------------------------

# Existing Features to Preserve

The following features must continue to work without regression:

-   End-to-end card-to-bank GL reconciliation for Interchange, ATM, and
    POS transactions.
-   Support for debit cardholder settlement accounts and credit
    intermediary settlement accounts.
-   Configurable reconciliation batches with source file upload.
-   High-performance Excel and CSV imports using SQL bulk insert.
-   Transaction-level matching.
-   Separate handling of GL bulk settlement postings.
-   Match confidence scoring.
-   Advanced matching using reference, amount, currency, account,
    processor, merchant, and settlement information.
-   Exception classifications.
-   Evidence drawer.
-   Duplicate analysis.
-   Dashboard statistics.
-   Filtering, sorting, and pagination.
-   CSV export.
-   Manual exception resolution.
-   Bulk manual matching.
-   Maker/Checker workflow.
-   Configurable workflow steps.
-   Workflow history.
-   Role-based security.
-   Advanced Analysis page.
-   Import and reconciliation progress indicators.
-   Scalability for large monthly and multi-year reconciliation
    datasets.

------------------------------------------------------------------------

# Deliverables

Before implementation:

1.  Study the existing solution.
2.  Understand the current architecture.
3.  Identify Mastercard-specific components.
4.  Identify reusable generic components.
5.  Produce a target architecture for a reusable reconciliation
    platform.
6.  Describe the refactoring strategy.
7.  Identify technical risks.
8.  Recommend interfaces, abstractions, extension points, and
    configuration model.
9.  Obtain architectural agreement before implementation begins.

**Do not begin refactoring immediately. Start with a complete
architectural assessment and implementation roadmap.**

------------------------------------------------------------------------

# Design Philosophy

This project should evolve from a **Mastercard Reconciliation Tool**
into a **Generic Reconciliation Platform**.

The reconciliation engine should remain independent of schemes, CMS
vendors, and Core Banking Systems.

New schemes, processors, CMS platforms, and banking systems should be
introduced through adapters and configuration, minimizing future
development effort and ensuring long-term maintainability and
extensibility.
