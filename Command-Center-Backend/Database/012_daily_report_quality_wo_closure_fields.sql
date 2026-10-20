USE DixonCommandCenterBackup;
GO

IF OBJECT_ID(N'dbo.DailyReport', N'U') IS NULL
    THROW 51000, 'dbo.DailyReport was not found.', 1;
IF OBJECT_ID(N'dbo.KPI_Daily', N'U') IS NULL
    THROW 51000, 'dbo.KPI_Daily was not found.', 1;
GO

DECLARE @tables TABLE (TableName SYSNAME);
INSERT INTO @tables (TableName) VALUES (N'DailyReport'), (N'KPI_Daily');

DECLARE @table SYSNAME;
DECLARE table_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT TableName FROM @tables;

OPEN table_cursor;
FETCH NEXT FROM table_cursor INTO @table;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @sql NVARCHAR(MAX) = N'';
    DECLARE @fields TABLE
    (
        ColumnName SYSNAME,
        ColumnType NVARCHAR(100)
    );

    INSERT INTO @fields (ColumnName, ColumnType) VALUES
        (N'FPYIntViTarget', N'DECIMAL(18,4) NULL'),
        (N'FPYIntViActual', N'DECIMAL(18,4) NULL'),
        (N'FPYPTTarget', N'DECIMAL(18,4) NULL'),
        (N'FPYPTActual', N'DECIMAL(18,4) NULL'),
        (N'FPYRiTarget', N'DECIMAL(18,4) NULL'),
        (N'FPYRiActual', N'DECIMAL(18,4) NULL'),
        (N'FPYRiImageTarget', N'DECIMAL(18,4) NULL'),
        (N'FPYRiImageActual', N'DECIMAL(18,4) NULL'),
        (N'FPYPackingViTarget', N'DECIMAL(18,4) NULL'),
        (N'FPYPackingViActual', N'DECIMAL(18,4) NULL'),
        (N'WOCount0To3Days', N'INT NULL'),
        (N'WOCount4To7Days', N'INT NULL'),
        (N'WOCountGreater7Days', N'INT NULL'),
        (N'WOCountTotal', N'INT NULL'),
        (N'WOQty0To3Days', N'INT NULL'),
        (N'WOQty4To7Days', N'INT NULL'),
        (N'WOQtyGreater7Days', N'INT NULL'),
        (N'WOQtyTotal', N'INT NULL'),
        (N'WOClearancePlanGreater7Days', N'INT NULL'),
        (N'WOResponsible', N'NVARCHAR(250) NULL'),
        (N'WOTimeline', N'NVARCHAR(250) NULL');

    SELECT @sql = @sql
        + N'IF COL_LENGTH(N''dbo.' + @table + N''', N''' + ColumnName + N''') IS NULL'
        + N' ALTER TABLE dbo.' + QUOTENAME(@table)
        + N' ADD ' + QUOTENAME(ColumnName) + N' ' + ColumnType + N';'
        + CHAR(13) + CHAR(10)
    FROM @fields;

    EXEC sys.sp_executesql @sql;
    DELETE FROM @fields;
    FETCH NEXT FROM table_cursor INTO @table;
END;

CLOSE table_cursor;
DEALLOCATE table_cursor;
GO
