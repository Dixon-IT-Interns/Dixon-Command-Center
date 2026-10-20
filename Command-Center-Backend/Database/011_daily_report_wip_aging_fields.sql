USE DixonCommandCenterBackup;
GO

IF OBJECT_ID(N'dbo.DailyReport', N'U') IS NULL
    THROW 51000, 'dbo.DailyReport was not found.', 1;
IF OBJECT_ID(N'dbo.KPI_Daily', N'U') IS NULL
    THROW 51000, 'dbo.KPI_Daily was not found.', 1;
GO

IF COL_LENGTH(N'dbo.DailyReport', N'wip_lying_greater_3_days') IS NULL
    ALTER TABLE dbo.DailyReport
        ADD wip_lying_greater_3_days INT NOT NULL
            CONSTRAINT DF_DailyReport_wip_lying_greater_3_days DEFAULT (0);
GO

IF COL_LENGTH(N'dbo.DailyReport', N'wip_lying_greater_7_days') IS NULL
    ALTER TABLE dbo.DailyReport
        ADD wip_lying_greater_7_days INT NOT NULL
            CONSTRAINT DF_DailyReport_wip_lying_greater_7_days DEFAULT (0);
GO

IF COL_LENGTH(N'dbo.KPI_Daily', N'wip_lying_greater_3_days') IS NULL
    ALTER TABLE dbo.KPI_Daily
        ADD wip_lying_greater_3_days INT NOT NULL
            CONSTRAINT DF_KPI_Daily_wip_lying_greater_3_days DEFAULT (0);
GO

IF COL_LENGTH(N'dbo.KPI_Daily', N'wip_lying_greater_7_days') IS NULL
    ALTER TABLE dbo.KPI_Daily
        ADD wip_lying_greater_7_days INT NOT NULL
            CONSTRAINT DF_KPI_Daily_wip_lying_greater_7_days DEFAULT (0);
GO
