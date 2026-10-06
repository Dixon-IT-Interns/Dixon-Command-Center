# Contributor reporting setup

The contributor flow now uses the existing `DailyReport` + `KPI_Daily` schema and the `KPI_Master` month/model/line targets.

## Run order on an existing database

1. The supplied `full-schema.sql` is the full schema reference used for the current database.
2. Run `seed.sql` after the base/catalog data exists. It also creates September 2026 sample `KPI_Master` rows for every active model/line combination.
3. If your database already contains the tables, do not recreate the schema; run the seed section only.

## Contributor API flow

- `GET /api/catalog/customers`
- `GET /api/daily-reports/config`
- `GET /api/daily-reports/draft`
- `GET /api/daily-reports/line-status`
- `POST /api/daily-reports/draft`
- `POST /api/daily-reports/submit`

A submitted report is stored in `DailyReport` with `Status = 'SUBMITTED'` and its KPI values in `KPI_Daily`. The line-status endpoint is the source of truth for the green submitted state on the line selection page.

Approval and admin reporting remain outside the contributor workflow for now.
