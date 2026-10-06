# Approval workflow setup

The current Command Center keeps the existing `DailyReport`, `KPI_Daily`, and `ReportApproval` tables. One new table, `ApprovalQueue`, is used to represent the pending approval state.

## 1. Run the migration once

Run:

`003_approval_queue.sql`

against `Dixon_Command_Center`.

If the queue table was already created from an earlier version, the migration is idempotent for the table/index creation.

## 2. Snapshot uniqueness migration

Run `005_kpi_daily_snapshot_uniqueness.sql` against `Dixon_Command_Center` after the base schema is installed. It replaces the legacy `(KPI_MasterId, ReportDate)` unique constraint with:

- a filtered unique index for rows that have a non-null `KPI_MasterId`;
- a filtered unique index on `DailyReportId`, which ensures one final snapshot per approved daily report while allowing multiple reports on the same date with `KPI_MasterId = NULL`.

The migration stops without changing the schema if duplicate snapshots already exist for a `DailyReportId`.

## 3. Workflow

Contributor:

`Save Draft` -> `DailyReport = DRAFT`

`Submit for Approval` -> `DailyReport = SUBMITTED` + `ApprovalQueue = PENDING`

Approver:

`Approve` -> `ApprovalQueue = APPROVED` + `DailyReport = APPROVED` + `ReportApproval = APPROVED`

`Reject` -> `ApprovalQueue = REJECTED` + `DailyReport = REJECTED` + `RejectionReason` + `ReportApproval = REJECTED`

Rejected reports remain editable by the contributor and can be submitted again. The same queue row is reset to `PENDING` on resubmission.

## 4. Brand binding

`UserCustomer` controls brand access. Admin can assign brands from the Administration page.

- Contributor sees only assigned brands.
- Approver sees only approval items for assigned brands.
- Admin sees all brands.

The backend also checks the assignment for reporting APIs; frontend filtering alone is not used as the security boundary.

## 5. Model behavior

Models are fetched from `KPI_Master` by Customer + Category + Line, not only by the current month. This preserves the line/model relationship even when the current month's KPI targets still need to be configured.

The selected model still needs an active `KPI_Master` row for the reporting month before it can be saved/submitted.
