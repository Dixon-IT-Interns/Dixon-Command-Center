# Daily Report Save / Multi-Model Fix

No new database table is required for this fix. The current schema already supports:

- `DailyReport.ModelId` for one daily report row per line + model.
- `DailyReport.IsActive` for today's line state.
- `UQ_DailyReport_Model` for `ReportDate + CustomerId + CategoryId + LineId + ModelId`.
- `KPI_Master` per customer + category + model + line + year + month.
- `KPI_Daily` per `KPI_MasterId + ReportDate`.

## Why the old save flow failed

The old controller searched `DailyReport` by only date/customer/category/line. That meant two models on the same line collided. It also inserted `DailyReport` without `ModelId`, which was inconsistent with the multi-model unique index.

The fixed controller now saves by:

`ReportDate + CustomerId + CategoryId + LineId + ModelId`

## Line inactive behavior

The line status endpoint uses the existing `DailyReport` rows. When a line is made inactive for a date:

1. Existing daily report rows for that line/date are marked inactive.
2. All daily actual KPI values are set to SQL `NULL`.
3. If the line had no model row yet, a line-level inactive marker is created with `ModelId = NULL`.
4. Reactivating removes only that marker and restores existing model rows as editable drafts.

## Monthly KPI prerequisite

Before saving a model/line, the selected month must have a matching `KPI_Master` row. Check it with:

```sql
SELECT
    KPI_MasterId,
    CustomerId,
    CategoryId,
    ModelId,
    LineId,
    KPIYEAR,
    KPIMonth,
    ProductionPlan,
    UPHTarget,
    UPPHTarget,
    CPHTarget,
    FPYTarget,
    FTYTarget,
    RTYTarget
FROM KPI_Master
WHERE KPIYEAR = 2026
  AND KPIMonth = 10
ORDER BY CustomerId, CategoryId, LineId, ModelId;
```

If this returns no rows for the October 2026 line/model combination, the application will correctly refuse to save because there is no monthly target/master record to attach `KPI_Daily` to.
