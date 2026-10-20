USE Dixon_Command_Center;
GO

IF COL_LENGTH(N'dbo.DailyReport', N'CPHTarget') IS NULL
BEGIN
    ALTER TABLE dbo.DailyReport
        ADD CPHTarget DECIMAL(18,4) NULL;
END;
GO

IF COL_LENGTH(N'dbo.DailyReport', N'CPHActual') IS NULL
BEGIN
    ALTER TABLE dbo.DailyReport
        ADD CPHActual DECIMAL(18,4) NULL;
END;
GO

IF COL_LENGTH(N'dbo.KPI_Daily', N'CPHTarget') IS NULL
BEGIN
    ALTER TABLE dbo.KPI_Daily
        ADD CPHTarget DECIMAL(18,4) NULL;
END;
GO
