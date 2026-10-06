USE Dixon_Command_Center;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.KPI_Daily', N'U') IS NULL
    THROW 51000, 'dbo.KPI_Daily does not exist.', 1;

IF EXISTS
(
    SELECT DailyReportId
    FROM dbo.KPI_Daily
    WHERE DailyReportId IS NOT NULL
    GROUP BY DailyReportId
    HAVING COUNT_BIG(*) > 1
)
    THROW 51001, 'Duplicate KPI_Daily rows exist for a DailyReportId; resolve them before applying this migration.', 1;

IF EXISTS
(
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.KPI_Daily')
      AND name = N'UQ_KPI_Daily_Master_Date'
)
BEGIN
    ALTER TABLE dbo.KPI_Daily
        DROP CONSTRAINT UQ_KPI_Daily_Master_Date;
END;
ELSE IF EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.KPI_Daily')
      AND name = N'UQ_KPI_Daily_Master_Date'
)
BEGIN
    DROP INDEX UQ_KPI_Daily_Master_Date
        ON dbo.KPI_Daily;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.KPI_Daily')
      AND name = N'UX_KPI_Daily_Master_ReportDate'
)
BEGIN
    CREATE UNIQUE INDEX UX_KPI_Daily_Master_ReportDate
        ON dbo.KPI_Daily(KPI_MasterId, ReportDate)
        WHERE KPI_MasterId IS NOT NULL;
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.KPI_Daily')
      AND name = N'UX_KPI_Daily_DailyReportId'
)
BEGIN
    CREATE UNIQUE INDEX UX_KPI_Daily_DailyReportId
        ON dbo.KPI_Daily(DailyReportId)
        WHERE DailyReportId IS NOT NULL;
END;

COMMIT TRANSACTION;
GO
