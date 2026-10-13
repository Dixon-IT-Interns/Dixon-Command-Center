USE Dixon_Command_Center;
GO

/*
    One-time, manually reviewed migration for plant-scoped operations.

    This script is intentionally not called by application startup. Review all
    preflight output and enter only verified legacy ownership overrides below
    before running it against a production database.

    Categories with a single PlantCustomer mapping are assigned automatically.
    A category mapped to multiple plants can be inferred only when all existing
    reports for it were created by users at exactly one of those plants.
    Existing reports inherit their creator's current PlantId unless an explicit
    report override is provided. Existing DailyReport.PlantId values are
    preserved on reruns. If historical user assignments have changed beforeA
    the first run, add overrides; the database has no plant-change history.

    The nullable PlantId columns are added before preflight so SQL Server can
    compile later batches even when preflight finds unresolved legacy rows.
    A failed preflight leaves those columns nullable and makes no data changes.
*/
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.PlantCustomer', N'U') IS NULL
    THROW 51000, 'PlantCustomer is required. Run the reviewed PlantCustomer migration first.', 1;
IF OBJECT_ID(N'dbo.Plant', N'U') IS NULL
    THROW 51000, 'dbo.Plant was not found.', 1;
IF OBJECT_ID(N'dbo.Category', N'U') IS NULL
    THROW 51000, 'dbo.Category was not found.', 1;
IF OBJECT_ID(N'dbo.ProductionLine', N'U') IS NULL
    THROW 51000, 'dbo.ProductionLine was not found.', 1;
IF OBJECT_ID(N'dbo.KPI_Master', N'U') IS NULL
    THROW 51000, 'dbo.KPI_Master was not found.', 1;
IF OBJECT_ID(N'dbo.DailyReport', N'U') IS NULL
    THROW 51000, 'dbo.DailyReport was not found.', 1;

IF OBJECT_ID(N'tempdb..#MigrationGate') IS NOT NULL DROP TABLE #MigrationGate;
IF OBJECT_ID(N'tempdb..#CategoryPlant') IS NOT NULL DROP TABLE #CategoryPlant;
IF OBJECT_ID(N'tempdb..#CategoryPlantOverrides') IS NOT NULL DROP TABLE #CategoryPlantOverrides;
IF OBJECT_ID(N'tempdb..#DailyReportPlantOverrides') IS NOT NULL DROP TABLE #DailyReportPlantOverrides;
IF OBJECT_ID(N'tempdb..#ExistingReportPlant') IS NOT NULL DROP TABLE #ExistingReportPlant;

CREATE TABLE #MigrationGate (CanProceed BIT NOT NULL);
INSERT INTO #MigrationGate (CanProceed) VALUES (0);

IF COL_LENGTH(N'dbo.Category', N'PlantId') IS NULL
    EXEC(N'ALTER TABLE dbo.Category ADD PlantId INT NULL;');
IF COL_LENGTH(N'dbo.ProductionLine', N'PlantId') IS NULL
    EXEC(N'ALTER TABLE dbo.ProductionLine ADD PlantId INT NULL;');
IF COL_LENGTH(N'dbo.KPI_Master', N'PlantId') IS NULL
    EXEC(N'ALTER TABLE dbo.KPI_Master ADD PlantId INT NULL;');
IF COL_LENGTH(N'dbo.DailyReport', N'PlantId') IS NULL
    EXEC(N'ALTER TABLE dbo.DailyReport ADD PlantId INT NULL;');
GO

CREATE TABLE #CategoryPlantOverrides
(
    CategoryId INT NOT NULL PRIMARY KEY,
    PlantId INT NOT NULL
);
CREATE TABLE #DailyReportPlantOverrides
(
    DailyReportId INT NOT NULL PRIMARY KEY,
    PlantId INT NOT NULL
);
CREATE TABLE #ExistingReportPlant
(
    DailyReportId INT NOT NULL PRIMARY KEY,
    PlantId INT NOT NULL
);
INSERT INTO #ExistingReportPlant(DailyReportId, PlantId)
SELECT DailyReportId, PlantId
FROM dbo.DailyReport
WHERE PlantId IS NOT NULL;

/*
    Verified legacy ownership overrides. Categories 5 and 6 belong to PlantId 2.
*/
INSERT INTO #CategoryPlantOverrides (CategoryId, PlantId)
VALUES (5, 2), (6, 2);

CREATE TABLE #CategoryPlant
(
    CategoryId INT NOT NULL PRIMARY KEY,
    PlantId INT NOT NULL
);

INSERT INTO #CategoryPlant(CategoryId, PlantId)
SELECT CategoryId, PlantId FROM dbo.Category WHERE PlantId IS NOT NULL;

IF EXISTS
(
    SELECT 1
    FROM #CategoryPlantOverrides o
    INNER JOIN #CategoryPlant existing ON existing.CategoryId = o.CategoryId
    WHERE existing.PlantId <> o.PlantId
)
    THROW 51000, 'A CategoryPlant override conflicts with an existing PlantId. Review the category ownership before retrying.', 1;

INSERT INTO #CategoryPlant(CategoryId, PlantId)
SELECT o.CategoryId, o.PlantId
FROM #CategoryPlantOverrides o
WHERE NOT EXISTS (SELECT 1 FROM #CategoryPlant p WHERE p.CategoryId = o.CategoryId);

IF EXISTS
(
    SELECT 1
    FROM #CategoryPlantOverrides o
    LEFT JOIN dbo.Category cat ON cat.CategoryId = o.CategoryId
    WHERE cat.CategoryId IS NULL
)
    THROW 51000, 'A CategoryPlant override references a CategoryId that does not exist.', 1;

IF EXISTS
(
    SELECT 1
    FROM #DailyReportPlantOverrides o
    LEFT JOIN dbo.DailyReport dr ON dr.DailyReportId = o.DailyReportId
    WHERE dr.DailyReportId IS NULL
)
    THROW 51000, 'A DailyReportPlant override references a DailyReportId that does not exist.', 1;

;WITH CustomerPlantCounts AS
(
    SELECT CustomerId, COUNT(*) AS PlantCount, MIN(PlantId) AS PlantId
    FROM dbo.PlantCustomer
    GROUP BY CustomerId
),
ReportPlantEvidence AS
(
    SELECT cat.CategoryId,
           COUNT(DISTINCT COALESCE(existingPlant.PlantId, u.PlantId)) AS PlantCount,
           MIN(COALESCE(existingPlant.PlantId, u.PlantId)) AS PlantId
    FROM dbo.Category cat
    INNER JOIN dbo.DailyReport dr ON dr.CategoryId = cat.CategoryId
    INNER JOIN dbo.[User] u ON u.UserId = dr.CreatedBy
    LEFT JOIN #ExistingReportPlant existingPlant ON existingPlant.DailyReportId = dr.DailyReportId
    GROUP BY cat.CategoryId
)
INSERT INTO #CategoryPlant(CategoryId, PlantId)
SELECT cat.CategoryId,
       CASE WHEN pc.PlantCount = 1 THEN pc.PlantId ELSE evidence.PlantId END
FROM dbo.Category cat
INNER JOIN CustomerPlantCounts pc ON pc.CustomerId = cat.CustomerId
LEFT JOIN ReportPlantEvidence evidence ON evidence.CategoryId = cat.CategoryId
WHERE NOT EXISTS (SELECT 1 FROM #CategoryPlant assigned WHERE assigned.CategoryId = cat.CategoryId)
  AND (pc.PlantCount = 1 OR (evidence.PlantCount = 1 AND EXISTS
       (SELECT 1 FROM dbo.PlantCustomer mapped
        WHERE mapped.PlantId = evidence.PlantId AND mapped.CustomerId = cat.CustomerId)));

DECLARE @UnmappedCategories NVARCHAR(2048);
SELECT @UnmappedCategories = STRING_AGG(CONVERT(VARCHAR(12), cat.CategoryId), ', ')
FROM dbo.Category cat
WHERE NOT EXISTS (SELECT 1 FROM #CategoryPlant mapped WHERE mapped.CategoryId = cat.CategoryId);
IF @UnmappedCategories IS NOT NULL
BEGIN
    SELECT cat.CategoryId, cat.CustomerId, pc.PlantId AS CandidatePlantId
    FROM dbo.Category cat
    INNER JOIN dbo.PlantCustomer pc ON pc.CustomerId = cat.CustomerId
    WHERE NOT EXISTS
    (
        SELECT 1 FROM #CategoryPlant assigned
        WHERE assigned.CategoryId = cat.CategoryId
    )
    ORDER BY cat.CategoryId, pc.PlantId;

    DECLARE @CategoryError NVARCHAR(2048) =
        CONCAT('Ambiguous/unmapped CategoryId(s): ', @UnmappedCategories,
               '. Add verified rows to #CategoryPlantOverrides and rerun.');
    THROW 51000, @CategoryError, 1;
END;

IF EXISTS
(
    SELECT 1
    FROM #CategoryPlant ownership
    INNER JOIN dbo.Category cat ON cat.CategoryId = ownership.CategoryId
    WHERE NOT EXISTS
    (
        SELECT 1 FROM dbo.PlantCustomer pc
        WHERE pc.PlantId = ownership.PlantId AND pc.CustomerId = cat.CustomerId
    )
)
    THROW 51000, 'A category ownership mapping does not match PlantCustomer. Correct the mapping before retrying.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.DailyReport dr
    INNER JOIN dbo.[User] u ON u.UserId = dr.CreatedBy
    INNER JOIN #CategoryPlant cp ON cp.CategoryId = dr.CategoryId
    LEFT JOIN #ExistingReportPlant existingPlant ON existingPlant.DailyReportId = dr.DailyReportId
    LEFT JOIN #DailyReportPlantOverrides reportOverride ON reportOverride.DailyReportId = dr.DailyReportId
    WHERE cp.PlantId <> COALESCE(reportOverride.PlantId, existingPlant.PlantId, u.PlantId)
)
BEGIN
    DECLARE @MismatchedReports NVARCHAR(2048);
    SELECT @MismatchedReports = STRING_AGG(CONVERT(VARCHAR(12), dr.DailyReportId), ', ')
    FROM dbo.DailyReport dr
    INNER JOIN dbo.[User] u ON u.UserId = dr.CreatedBy
    INNER JOIN #CategoryPlant cp ON cp.CategoryId = dr.CategoryId
    LEFT JOIN #ExistingReportPlant existingPlant ON existingPlant.DailyReportId = dr.DailyReportId
    LEFT JOIN #DailyReportPlantOverrides reportOverride ON reportOverride.DailyReportId = dr.DailyReportId
    WHERE cp.PlantId <> COALESCE(reportOverride.PlantId, existingPlant.PlantId, u.PlantId);
    DECLARE @ReportError NVARCHAR(2048) =
        CONCAT('Report/category plant conflicts for DailyReportId(s): ', @MismatchedReports,
               '. Reconcile legacy ownership (including category overrides) before retrying.');
    THROW 51000, @ReportError, 1;
END;

IF EXISTS
(
    SELECT 1
    FROM dbo.DailyReport dr
    LEFT JOIN dbo.[User] u ON u.UserId = dr.CreatedBy
    LEFT JOIN #ExistingReportPlant existingPlant ON existingPlant.DailyReportId = dr.DailyReportId
    LEFT JOIN #DailyReportPlantOverrides reportOverride ON reportOverride.DailyReportId = dr.DailyReportId
    WHERE COALESCE(reportOverride.PlantId, existingPlant.PlantId, u.PlantId) IS NULL
)
    THROW 51000, 'A report creator has no resolvable plant. Add a verified row to #DailyReportPlantOverrides.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.KPI_Master km
    INNER JOIN dbo.Category cat ON cat.CategoryId = km.CategoryId
    INNER JOIN dbo.ProductionLine pl ON pl.LineId = km.LineId
    INNER JOIN dbo.Model m ON m.ModelId = km.ModelId
    WHERE km.CustomerId <> cat.CustomerId
       OR pl.CategoryId <> km.CategoryId
       OR m.CustomerId <> km.CustomerId
)
    THROW 51000, 'KPI_Master contains a customer/category/line/model relationship mismatch. Correct those records before retrying.', 1;

UPDATE #MigrationGate SET CanProceed = 1;
GO

IF EXISTS (SELECT 1 FROM #MigrationGate WHERE CanProceed = 1)
BEGIN
    UPDATE #MigrationGate SET CanProceed = 0;
    BEGIN TRANSACTION;

UPDATE cat SET PlantId = ownership.PlantId
FROM dbo.Category cat
INNER JOIN #CategoryPlant ownership ON ownership.CategoryId = cat.CategoryId;

UPDATE pl SET PlantId = cat.PlantId
FROM dbo.ProductionLine pl
INNER JOIN dbo.Category cat ON cat.CategoryId = pl.CategoryId;

UPDATE km SET PlantId = cat.PlantId
FROM dbo.KPI_Master km
INNER JOIN dbo.Category cat ON cat.CategoryId = km.CategoryId;

UPDATE dr SET PlantId = COALESCE(reportOverride.PlantId, existingPlant.PlantId, u.PlantId)
FROM dbo.DailyReport dr
INNER JOIN dbo.[User] u ON u.UserId = dr.CreatedBy
LEFT JOIN #ExistingReportPlant existingPlant ON existingPlant.DailyReportId = dr.DailyReportId
LEFT JOIN #DailyReportPlantOverrides reportOverride ON reportOverride.DailyReportId = dr.DailyReportId;

IF EXISTS (SELECT 1 FROM dbo.Category WHERE PlantId IS NULL)
   OR EXISTS (SELECT 1 FROM dbo.ProductionLine WHERE PlantId IS NULL)
   OR EXISTS (SELECT 1 FROM dbo.KPI_Master WHERE PlantId IS NULL)
   OR EXISTS (SELECT 1 FROM dbo.DailyReport WHERE PlantId IS NULL)
    THROW 51000, 'Plant ownership backfill left NULL records; transaction rolled back.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.DailyReport dr
    INNER JOIN dbo.Category cat ON cat.CategoryId = dr.CategoryId
    INNER JOIN dbo.ProductionLine pl ON pl.LineId = dr.LineId
    WHERE dr.PlantId <> cat.PlantId
       OR dr.PlantId <> pl.PlantId
       OR dr.CustomerId <> cat.CustomerId
       OR dr.CategoryId <> pl.CategoryId
)
    THROW 51000, 'DailyReport ownership does not match its customer/category/line. Reconcile data before retrying.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.KPI_Master km
    INNER JOIN dbo.Category cat ON cat.CategoryId = km.CategoryId
    INNER JOIN dbo.ProductionLine pl ON pl.LineId = km.LineId
    INNER JOIN dbo.Model m ON m.ModelId = km.ModelId
    WHERE km.PlantId <> cat.PlantId
       OR km.PlantId <> pl.PlantId
       OR km.CustomerId <> cat.CustomerId
       OR km.CategoryId <> pl.CategoryId
       OR km.CustomerId <> m.CustomerId
)
    THROW 51000, 'KPI_Master ownership does not match its customer/category/line/model. Reconcile data before retrying.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.DailyReport
    GROUP BY PlantId, ReportDate, CustomerId, CategoryId, LineId, ModelId
    HAVING COUNT(*) > 1
)
    THROW 51000, 'Duplicate DailyReport rows exist for the new plant/report/model key. Resolve duplicates before retrying.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.KPI_Master
    GROUP BY PlantId, CustomerId, CategoryId, ModelId, LineId, KPIYEAR, KPIMonth
    HAVING COUNT(*) > 1
)
    THROW 51000, 'Duplicate KPI_Master rows exist for the new plant/plan key. Resolve duplicates before retrying.', 1;

ALTER TABLE dbo.Category ALTER COLUMN PlantId INT NOT NULL;
ALTER TABLE dbo.ProductionLine ALTER COLUMN PlantId INT NOT NULL;
ALTER TABLE dbo.KPI_Master ALTER COLUMN PlantId INT NOT NULL;
ALTER TABLE dbo.DailyReport ALTER COLUMN PlantId INT NOT NULL;

DECLARE @DropCategoryConstraints NVARCHAR(MAX) = N'';
SELECT @DropCategoryConstraints = @DropCategoryConstraints +
       N'ALTER TABLE dbo.Category DROP CONSTRAINT ' + QUOTENAME(k.name) + N';'
FROM sys.key_constraints k
WHERE k.parent_object_id = OBJECT_ID(N'dbo.Category')
  AND k.type = 'UQ'
  AND EXISTS
      (SELECT 1 FROM sys.index_columns ic INNER JOIN sys.columns c
       ON c.object_id = ic.object_id AND c.column_id = ic.column_id
       WHERE ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id AND c.name = N'CustomerId')
  AND EXISTS
      (SELECT 1 FROM sys.index_columns ic INNER JOIN sys.columns c
       ON c.object_id = ic.object_id AND c.column_id = ic.column_id
       WHERE ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id AND c.name = N'CategoryName');
IF @DropCategoryConstraints <> N'' EXEC sys.sp_executesql @DropCategoryConstraints;

DECLARE @DropPlanConstraints NVARCHAR(MAX) = N'';
SELECT @DropPlanConstraints = @DropPlanConstraints +
       N'ALTER TABLE dbo.KPI_Master DROP CONSTRAINT ' + QUOTENAME(k.name) + N';'
FROM sys.key_constraints k
WHERE k.parent_object_id = OBJECT_ID(N'dbo.KPI_Master')
  AND k.type = 'UQ'
  AND EXISTS
      (SELECT 1 FROM sys.index_columns ic INNER JOIN sys.columns c
       ON c.object_id = ic.object_id AND c.column_id = ic.column_id
       WHERE ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id AND c.name = N'CustomerId')
  AND EXISTS
      (SELECT 1 FROM sys.index_columns ic INNER JOIN sys.columns c
       ON c.object_id = ic.object_id AND c.column_id = ic.column_id
       WHERE ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id AND c.name = N'KPIYEAR');
IF @DropPlanConstraints <> N'' EXEC sys.sp_executesql @DropPlanConstraints;

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name=N'UQ_Category_Plant_Customer_Id' AND parent_object_id=OBJECT_ID(N'dbo.Category'))
    ALTER TABLE dbo.Category ADD CONSTRAINT UQ_Category_Plant_Customer_Id UNIQUE (PlantId, CustomerId, CategoryId);
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name=N'UQ_Category_Plant_Customer_Name' AND parent_object_id=OBJECT_ID(N'dbo.Category'))
    ALTER TABLE dbo.Category ADD CONSTRAINT UQ_Category_Plant_Customer_Name UNIQUE (PlantId, CustomerId, CategoryName);
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name=N'UQ_Category_Plant_Id' AND parent_object_id=OBJECT_ID(N'dbo.Category'))
    ALTER TABLE dbo.Category ADD CONSTRAINT UQ_Category_Plant_Id UNIQUE (PlantId, CategoryId);
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name=N'UQ_ProductionLine_Plant_Category_Line' AND parent_object_id=OBJECT_ID(N'dbo.ProductionLine'))
    ALTER TABLE dbo.ProductionLine ADD CONSTRAINT UQ_ProductionLine_Plant_Category_Line UNIQUE (PlantId, CategoryId, LineId);
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name=N'UQ_Model_Customer_Model' AND parent_object_id=OBJECT_ID(N'dbo.Model'))
    ALTER TABLE dbo.Model ADD CONSTRAINT UQ_Model_Customer_Model UNIQUE (CustomerId, ModelId);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ProductionLine_PlantCategory' AND parent_object_id=OBJECT_ID(N'dbo.ProductionLine'))
    ALTER TABLE dbo.ProductionLine ADD CONSTRAINT FK_ProductionLine_PlantCategory
        FOREIGN KEY (PlantId, CategoryId) REFERENCES dbo.Category(PlantId, CategoryId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Category_Plant' AND parent_object_id=OBJECT_ID(N'dbo.Category'))
    ALTER TABLE dbo.Category ADD CONSTRAINT FK_Category_Plant
        FOREIGN KEY (PlantId) REFERENCES dbo.Plant(PlantId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_KPI_Master_PlantCategory' AND parent_object_id=OBJECT_ID(N'dbo.KPI_Master'))
    ALTER TABLE dbo.KPI_Master ADD CONSTRAINT FK_KPI_Master_PlantCategory
        FOREIGN KEY (PlantId, CustomerId, CategoryId) REFERENCES dbo.Category(PlantId, CustomerId, CategoryId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_KPI_Master_PlantLine' AND parent_object_id=OBJECT_ID(N'dbo.KPI_Master'))
    ALTER TABLE dbo.KPI_Master ADD CONSTRAINT FK_KPI_Master_PlantLine
        FOREIGN KEY (PlantId, CategoryId, LineId) REFERENCES dbo.ProductionLine(PlantId, CategoryId, LineId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_KPI_Master_CustomerModel' AND parent_object_id=OBJECT_ID(N'dbo.KPI_Master'))
    ALTER TABLE dbo.KPI_Master ADD CONSTRAINT FK_KPI_Master_CustomerModel
        FOREIGN KEY (CustomerId, ModelId) REFERENCES dbo.Model(CustomerId, ModelId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_DailyReport_PlantCategory' AND parent_object_id=OBJECT_ID(N'dbo.DailyReport'))
    ALTER TABLE dbo.DailyReport ADD CONSTRAINT FK_DailyReport_PlantCategory
        FOREIGN KEY (PlantId, CustomerId, CategoryId) REFERENCES dbo.Category(PlantId, CustomerId, CategoryId);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_DailyReport_PlantLine' AND parent_object_id=OBJECT_ID(N'dbo.DailyReport'))
    ALTER TABLE dbo.DailyReport ADD CONSTRAINT FK_DailyReport_PlantLine
        FOREIGN KEY (PlantId, CategoryId, LineId) REFERENCES dbo.ProductionLine(PlantId, CategoryId, LineId);

DECLARE @DropReportConstraints NVARCHAR(MAX) = N'';
SELECT @DropReportConstraints = @DropReportConstraints +
       N'ALTER TABLE dbo.DailyReport DROP CONSTRAINT ' + QUOTENAME(k.name) + N';'
FROM sys.key_constraints k
WHERE k.parent_object_id = OBJECT_ID(N'dbo.DailyReport')
  AND k.type = 'UQ'
  AND EXISTS
      (SELECT 1 FROM sys.index_columns ic INNER JOIN sys.columns c
       ON c.object_id = ic.object_id AND c.column_id = ic.column_id
       WHERE ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id AND c.name = N'ReportDate')
  AND EXISTS
      (SELECT 1 FROM sys.index_columns ic INNER JOIN sys.columns c
       ON c.object_id = ic.object_id AND c.column_id = ic.column_id
       WHERE ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id AND c.name = N'CustomerId')
  AND EXISTS
      (SELECT 1 FROM sys.index_columns ic INNER JOIN sys.columns c
       ON c.object_id = ic.object_id AND c.column_id = ic.column_id
       WHERE ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id AND c.name = N'CategoryId')
  AND EXISTS
      (SELECT 1 FROM sys.index_columns ic INNER JOIN sys.columns c
       ON c.object_id = ic.object_id AND c.column_id = ic.column_id
       WHERE ic.object_id = k.parent_object_id AND ic.index_id = k.unique_index_id AND c.name = N'LineId');
IF @DropReportConstraints <> N'' EXEC sys.sp_executesql @DropReportConstraints;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UQ_KPI_Master' AND object_id=OBJECT_ID(N'dbo.KPI_Master'))
    DROP INDEX UQ_KPI_Master ON dbo.KPI_Master;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UQ_KPI_Master_Plant' AND object_id=OBJECT_ID(N'dbo.KPI_Master'))
    CREATE UNIQUE INDEX UQ_KPI_Master_Plant
        ON dbo.KPI_Master(PlantId, CustomerId, CategoryId, ModelId, LineId, KPIYEAR, KPIMonth);

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UQ_DailyReport_Date_Customer_Category_Line_Model' AND object_id=OBJECT_ID(N'dbo.DailyReport'))
    DROP INDEX UQ_DailyReport_Date_Customer_Category_Line_Model ON dbo.DailyReport;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UQ_DailyReport_Model' AND object_id=OBJECT_ID(N'dbo.DailyReport'))
    DROP INDEX UQ_DailyReport_Model ON dbo.DailyReport;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UQ_DailyReport_Plant_Date_Customer_Category_Line_Model' AND object_id=OBJECT_ID(N'dbo.DailyReport'))
    CREATE UNIQUE INDEX UQ_DailyReport_Plant_Date_Customer_Category_Line_Model
        ON dbo.DailyReport(PlantId, ReportDate, CustomerId, CategoryId, LineId, ModelId)
        WHERE ModelId IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UQ_DailyReport_Plant_Date_Null_Model' AND object_id=OBJECT_ID(N'dbo.DailyReport'))
    CREATE UNIQUE INDEX UQ_DailyReport_Plant_Date_Null_Model
        ON dbo.DailyReport(PlantId, ReportDate, CustomerId, CategoryId, LineId)
        WHERE ModelId IS NULL;

COMMIT TRANSACTION;
    UPDATE #MigrationGate SET CanProceed = 1;
END;
GO

/*
    Post-migration verification (run manually after successful execution):
*/
IF EXISTS (SELECT 1 FROM #MigrationGate WHERE CanProceed = 1)
BEGIN
    SELECT PlantId, CustomerId, COUNT(*) AS Categories
    FROM dbo.Category GROUP BY PlantId, CustomerId ORDER BY PlantId, CustomerId;
    SELECT PlantId, CategoryId, COUNT(*) AS Lines
    FROM dbo.ProductionLine GROUP BY PlantId, CategoryId ORDER BY PlantId, CategoryId;
    SELECT PlantId, COUNT(*) AS Reports FROM dbo.DailyReport GROUP BY PlantId;
    SELECT PlantId, CustomerId, CategoryId, ModelId, LineId, KPIYEAR, KPIMonth, COUNT(*) AS Duplicates
    FROM dbo.KPI_Master
    GROUP BY PlantId, CustomerId, CategoryId, ModelId, LineId, KPIYEAR, KPIMonth
    HAVING COUNT(*) > 1;
END;
