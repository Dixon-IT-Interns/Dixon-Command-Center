# Command Center Admin / KPI Plan setup

## 1. Run database migration
Run `Database/004_admin_catalog_and_line_model.sql` after the existing schema and approval queue scripts.

It creates `dbo.LineModel`, preserves existing line/model relationships found in `KPI_Master`, and ensures the monthly KPI plan columns exist.

## 2. What is stored where
- `KPI_Master`: monthly production plans and target configuration.
- `DailyReport`: contributor staging/manual daily entry.
- `KPI_Daily`: daily KPI values used by the current reporting/approval flow.
- `ApprovalQueue`: submitted reports waiting for an approver.
- `LineModel`: controls which models can appear under each production line.

The contributor Daily Report does **not** fetch its manual daily Plan/Target/Actual inputs from `KPI_Master`.

## 3. Admin APIs
- `GET /api/admin/catalog/overview`
- `POST/PUT/DELETE /api/admin/catalog/plants`
- `POST/PUT/DELETE /api/admin/catalog/customers`
- `POST/PUT/DELETE /api/admin/catalog/categories`
- `POST/PUT/DELETE /api/admin/catalog/models`
- `POST/PUT/DELETE /api/admin/catalog/lines`
- `POST/DELETE /api/admin/catalog/line-models`
- `GET/POST/DELETE /api/admin/catalog/plans`
- `GET/PUT/DELETE /api/admin/users`

## 4. Approval plan reference
`GET /api/approvals/{id}` returns the submitted daily values plus the active monthly `KPI_Master` plan for the same customer/category/line/model/year/month. The approval UI shows both sections separately.

## 5. Frontend
Run `npm install` in `Command-Center-Frontend`, then `npm run dev`.
