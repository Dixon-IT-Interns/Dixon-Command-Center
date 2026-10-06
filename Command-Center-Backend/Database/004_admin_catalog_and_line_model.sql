USE Dixon_Command_Center;
GO

IF OBJECT_ID(N'dbo.LineModel', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LineModel
    (
        LineModelId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LineId INT NOT NULL,
        ModelId INT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_LineModel_IsActive DEFAULT 1,
        CONSTRAINT FK_LineModel_Line FOREIGN KEY(LineId) REFERENCES dbo.ProductionLine(LineId),
        CONSTRAINT FK_LineModel_Model FOREIGN KEY(ModelId) REFERENCES dbo.Model(ModelId),
        CONSTRAINT UQ_LineModel UNIQUE(LineId,ModelId)
    );
END;
GO

-- Preserve any existing line/model relationships already present in KPI_Master.
IF OBJECT_ID(N'dbo.KPI_Master', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.LineModel(LineId,ModelId,IsActive)
    SELECT DISTINCT km.LineId,km.ModelId,1
    FROM dbo.KPI_Master km
    INNER JOIN dbo.ProductionLine pl ON pl.LineId=km.LineId
    INNER JOIN dbo.Model m ON m.ModelId=km.ModelId
    WHERE NOT EXISTS (SELECT 1 FROM dbo.LineModel lm WHERE lm.LineId=km.LineId AND lm.ModelId=km.ModelId);
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'IX_LineModel_Line' AND object_id=OBJECT_ID(N'dbo.LineModel'))
    CREATE INDEX IX_LineModel_Line ON dbo.LineModel(LineId,IsActive);
GO

-- KPI_Master is the configuration/master-plan table. DailyReport remains manual entry.
-- These fields are intentionally NOT fetched by the contributor Daily Report.
IF OBJECT_ID(N'dbo.KPI_Master', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.KPI_Master','ProductionPlan') IS NULL ALTER TABLE dbo.KPI_Master ADD ProductionPlan DECIMAL(18,2) NULL;
    IF COL_LENGTH('dbo.KPI_Master','UPHTarget') IS NULL ALTER TABLE dbo.KPI_Master ADD UPHTarget DECIMAL(18,2) NULL;
    IF COL_LENGTH('dbo.KPI_Master','UPPHTarget') IS NULL ALTER TABLE dbo.KPI_Master ADD UPPHTarget DECIMAL(18,2) NULL;
    IF COL_LENGTH('dbo.KPI_Master','CPHTarget') IS NULL ALTER TABLE dbo.KPI_Master ADD CPHTarget DECIMAL(18,2) NULL;
    IF COL_LENGTH('dbo.KPI_Master','FPYTarget') IS NULL ALTER TABLE dbo.KPI_Master ADD FPYTarget DECIMAL(10,4) NULL;
    IF COL_LENGTH('dbo.KPI_Master','FTYTarget') IS NULL ALTER TABLE dbo.KPI_Master ADD FTYTarget DECIMAL(10,4) NULL;
    IF COL_LENGTH('dbo.KPI_Master','RTYTarget') IS NULL ALTER TABLE dbo.KPI_Master ADD RTYTarget DECIMAL(10,4) NULL;
    IF COL_LENGTH('dbo.KPI_Master','OSDTarget') IS NULL ALTER TABLE dbo.KPI_Master ADD OSDTarget DECIMAL(18,2) NULL;
    IF COL_LENGTH('dbo.KPI_Master','OTTarget') IS NULL ALTER TABLE dbo.KPI_Master ADD OTTarget DECIMAL(18,2) NULL;
END;
GO
