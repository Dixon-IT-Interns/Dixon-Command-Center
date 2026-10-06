# Dixon Command Center API

The API uses the existing SQL Server schema in `Dixon_Command_Center`. Create or verify tables with `Database/schema.sql`, then use `Database/seed.sql` for the roles, plant, customers, categories, models, and production lines. The seed is rerunnable and includes a guarded correction for the existing Acer-SMT/Gigabyte-FATP line mapping error. The first account is created through the login page's one-time administrator setup; the `Admin` role and at least one active plant must already exist.

## Local configuration

Set the SQL Server connection string in the environment so credentials are not committed:

```powershell
$env:ConnectionStrings__DixonDb = "Server=YOUR_SERVER;Database=Dixon_Command_Center;Trusted_Connection=True;TrustServerCertificate=True"
```

The API uses the `http` launch profile at `http://localhost:5210`. In development, it generates a temporary signing key if `Jwt:Secret` is not configured, so issued tokens expire or become invalid after the API restarts. For a stable local key, set a random secret before starting:

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
- `GET /api/admin/users`, `/roles`, and `/plants` require the `Admin` role.
- `POST /api/admin/users` adds an account with a PBKDF2 password hash.

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

Brand access is controlled by `UserCustomer` and enforced by the backend catalog/reporting APIs.
