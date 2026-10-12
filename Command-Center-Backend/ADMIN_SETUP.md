# Command Center Admin / KPI Plan setup

## 1. Run database migration
Run `Database/004_admin_catalog_and_line_model.sql` after the existing schema and approval queue scripts. For an existing database, run `Database/007_plant_customer.sql` followed by `Database/008_super_admin_hierarchy.sql` before starting the updated API. The latter promotes the first existing Admin to SuperAdmin; subsequent Admins remain plant-scoped. New databases get the table from `Database/schema.sql`, and the initial setup creates the first SuperAdmin.

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
- SuperAdmin: create/edit/deactivate plants and create Admin users assigned to a plant.
- SuperAdmin: link each customer to one plant at a time from the Plant Customers section. Resolve any legacy multi-plant customer links here before plant users manage that customer.
- Plant Admin: manage users and catalog only for their assigned plant; cannot create Admins or change plant records.
- `POST/PUT/DELETE /api/admin/catalog/plants`
- `GET/PUT /api/admin/catalog/plants/{id}/customers`
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
