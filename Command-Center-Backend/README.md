# Dixon Command Center API

The API uses the existing SQL Server schema in `Dixon_Command_Center`. Create or verify tables with `Database/schema.sql`. For an existing database, run `Database/007_plant_customer.sql`, then `Database/008_super_admin_hierarchy.sql`, before `Database/seed.sql` or starting the updated API. The first migration backfills plant/customer mappings. The second adds the `SuperAdmin` role and promotes the first existing Admin to SuperAdmin; remaining Admins are plant-scoped. Then use `Database/seed.sql` for the roles, plant, plant/customer assignments, categories, models, and production lines. The seed is rerunnable and includes a guarded correction for the existing Acer-SMT/Gigabyte-FATP line mapping error. On a new database, run `Database/schema.sql` and `Database/seed.sql`; initial setup creates the first SuperAdmin.

## Local configuration

Set the SQL Server connection string in the environment so credentials are not committed:

```powershell
$env:ConnectionStrings__DixonDb = "Server=YOUR_SERVER;Database=Dixon_Command_Center;Trusted_Connection=True;TrustServerCertificate=True"
```

The API uses the `http` launch profile at `http://localhost:5211`. In development, it generates a temporary signing key if `Jwt:Secret` is not configured, so issued tokens expire or become invalid after the API restarts. For a stable local key, set a random secret before starting:

```powershell
$env:Jwt__Secret = "replace-with-a-long-random-secret"
dotnet run --launch-profile http
```

Outside development, `Jwt:Secret` is required. Configure `Frontend:Origin` if Vite is not running at `http://localhost:5173`.

## API routes

- `GET /api/auth/setup-status` reports whether first-admin setup is required.
- `POST /api/auth/bootstrap` creates the first administrator exactly once.
- `POST /api/auth/login` returns a 30-minute bearer token.
- `GET /api/auth/me` returns the authenticated identity.
- `GET /api/catalog/customers` returns customers with models, categories, and production lines.
- `GET /api/viewer/progress?reportDate=YYYY-MM-DD&customerId={id}&process=ALL|FATP|SMT` returns read-only line progress for assigned customers (Viewer role).
- `GET /api/viewer/trend?startDate=YYYY-MM-DD&endDate=YYYY-MM-DD&customerId={id}&process=ALL|FATP|SMT` returns the production plan/actual trend (Viewer role).
- `GET /api/viewer/export?reportDate=YYYY-MM-DD&customerId={id}&process=ALL|FATP|SMT` downloads the filtered progress as an Excel workbook (Viewer role).
- `GET /api/admin/catalog/plants/{id}/customers` and `PUT /api/admin/catalog/plants/{id}/customers` manage a plant's customer associations (Admin role).
- `GET /api/admin/users`, `/roles`, and `/plants` are scoped to the Admin's plant; SuperAdmins can manage users across plants.
- `POST /api/admin/users` lets SuperAdmins create plant Admins. Plant Admins can create only non-admin roles in their own plant.

Run the frontend from `Command-Center-Frontend` with `npm run dev`. Set `VITE_API_URL` if the API is hosted at a different URL.

## Approval workflow

Run `Database/003_approval_queue.sql` once on the existing `Dixon_Command_Center` database.

API:
- `GET /api/approvals/pending` — pending reports for the logged-in Approver's assigned brands.
- `GET /api/approvals/history` — recently reviewed reports for assigned brands.
- `GET /api/approvals/{id}` — full KPI detail.
- `POST /api/approvals/{id}/approve` — approve and lock.
- `POST /api/approvals/{id}/reject` — reject with a required comment.
- `GET /api/contributor/dashboard?days=7` — contributor activity/history.

Plant customer access is controlled by `PlantCustomer`; individual user assignments remain controlled by `UserCustomer`. Both are enforced by the backend catalog/reporting APIs.
