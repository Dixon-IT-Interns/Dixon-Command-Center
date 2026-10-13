USE Dixon_Command_Center;
GO

IF OBJECT_ID(N'dbo.DailyReport', N'U') IS NULL
    THROW 51000, 'dbo.DailyReport was not found.', 1;
IF OBJECT_ID(N'dbo.KPI_Daily', N'U') IS NULL
    THROW 51000, 'dbo.KPI_Daily was not found.', 1;
GO

IF COL_LENGTH(N'dbo.DailyReport', N'UPPHTarget') IS NULL
    ALTER TABLE dbo.DailyReport ADD UPPHTarget DECIMAL(18,4) NULL;
GO

IF COL_LENGTH(N'dbo.DailyReport', N'OSDTarget') IS NULL
    ALTER TABLE dbo.DailyReport ADD OSDTarget DECIMAL(18,4) NULL;
GO

IF COL_LENGTH(N'dbo.DailyReport', N'OTTarget') IS NULL
    ALTER TABLE dbo.DailyReport ADD OTTarget DECIMAL(18,4) NULL;
GO

IF COL_LENGTH(N'dbo.KPI_Daily', N'UPPHTarget') IS NULL
    ALTER TABLE dbo.KPI_Daily ADD UPPHTarget DECIMAL(18,4) NULL;
GO

IF COL_LENGTH(N'dbo.KPI_Daily', N'OSDTarget') IS NULL
    ALTER TABLE dbo.KPI_Daily ADD OSDTarget DECIMAL(18,4) NULL;
GO

IF COL_LENGTH(N'dbo.KPI_Daily', N'OTTarget') IS NULL
    ALTER TABLE dbo.KPI_Daily ADD OTTarget DECIMAL(18,4) NULL;
GO
