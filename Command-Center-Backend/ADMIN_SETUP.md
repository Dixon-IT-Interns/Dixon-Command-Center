# Command Center administration setup

## Database migrations

For an existing database, review and apply the required scripts manually, in order:

1. `Database/004_admin_catalog_and_line_model.sql` for the existing admin catalog and compatibility table.
2. `Database/007_plant_customer.sql` for the many-to-many plant/customer mapping.
3. `Database/008_super_admin_hierarchy.sql` for SuperAdmin role setup.
4. `Database/009_plant_scoped_operational_configuration.sql` for plant ownership of categories, production lines, KPI plans and daily reports.

Do not run these scripts from application startup. Back up the database first. Run migration 009 as a whole in one SSMS query window. If its preflight reports ambiguous categories, use the candidate plant IDs it returns and verified historical ownership to add rows to `#CategoryPlantOverrides`; add report overrides only when creator/current-plant evidence is not reliable. The migration stops before data backfill when preflight fails. Its nullable `PlantId` columns may remain after such a failure; rerunning the script is supported. Run the verification queries after a successful execution. No migration script has been run by the application or this change.

## Data relationships

- `PlantCustomer` is the many-to-many source of truth for plant/customer access.
- `UserCustomer` limits a user to customer records within their assigned plant.
- Customers and models are global master data. Categories, production lines, KPI plans and daily reports are plant-scoped.
- `LineModel` remains in the database for compatibility/history only. Daily Report, KPI plan and Excel-import runtime flows load models from the selected customer.
- `DailyReport` and approval history are retained and receive plant ownership during migration.

## Administration access

- SuperAdmin manages plants, plant/customer assignments and global customer/model records, and can manage configuration across plants.
- Plant Admin is restricted to their assigned plant. They can manage users, categories, production lines and KPI plans, and view assigned customers and active models read-only.
- Backend APIs validate plant/customer/configuration ownership; UI filtering is not the authorization boundary.

## Relevant APIs

- `GET /api/admin/catalog/overview`
- `GET/PUT /api/admin/catalog/plants/{id}/customers` (SuperAdmin)
- `POST/PUT/DELETE /api/admin/catalog/plants` (SuperAdmin)
- `POST/PUT/DELETE /api/admin/catalog/customers` (SuperAdmin)
- `POST/PUT/DELETE /api/admin/catalog/categories`
- `POST/PUT/DELETE /api/admin/catalog/models` (SuperAdmin)
- `POST/PUT/DELETE /api/admin/catalog/lines`
- `GET/POST/DELETE /api/admin/catalog/plans`
- `GET/PUT/DELETE /api/admin/users`

There is no runtime LineModel administration endpoint. Daily report and Excel import verify the customer, plant, category, line and active model independently.

## Frontend

Run `npm install` from `Command-Center-Frontend`, then `npm run dev`.
