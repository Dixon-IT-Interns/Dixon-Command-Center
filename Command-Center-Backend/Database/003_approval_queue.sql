USE Dixon_Command_Center;
GO

IF OBJECT_ID(N'dbo.ApprovalQueue', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApprovalQueue
    (
        ApprovalQueueId INT IDENTITY(1,1) NOT NULL,
        DailyReportId INT NOT NULL,
        Status VARCHAR(30) NOT NULL CONSTRAINT DF_ApprovalQueue_Status DEFAULT ('PENDING'),
        SubmittedBy INT NOT NULL,
        SubmittedAt DATETIME2(7) NOT NULL CONSTRAINT DF_ApprovalQueue_SubmittedAt DEFAULT (SYSUTCDATETIME()),
        AssignedTo INT NULL,
        AssignedAt DATETIME2(7) NULL,
        ReviewedBy INT NULL,
        ReviewedAt DATETIME2(7) NULL,
        Comments VARCHAR(1000) NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_ApprovalQueue_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt DATETIME2(7) NULL,
        CONSTRAINT PK_ApprovalQueue PRIMARY KEY CLUSTERED (ApprovalQueueId),
        CONSTRAINT FK_ApprovalQueue_Report FOREIGN KEY (DailyReportId) REFERENCES dbo.DailyReport(DailyReportId),
        CONSTRAINT FK_ApprovalQueue_SubmittedBy FOREIGN KEY (SubmittedBy) REFERENCES dbo.[User](UserId),
        CONSTRAINT FK_ApprovalQueue_AssignedTo FOREIGN KEY (AssignedTo) REFERENCES dbo.[User](UserId),
        CONSTRAINT FK_ApprovalQueue_ReviewedBy FOREIGN KEY (ReviewedBy) REFERENCES dbo.[User](UserId)
    );
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_ApprovalQueue_DailyReport'
      AND object_id = OBJECT_ID(N'dbo.ApprovalQueue')
)
BEGIN
    CREATE UNIQUE INDEX UX_ApprovalQueue_DailyReport
        ON dbo.ApprovalQueue(DailyReportId);
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_ApprovalQueue_Status'
      AND object_id = OBJECT_ID(N'dbo.ApprovalQueue')
)
BEGIN
    CREATE INDEX IX_ApprovalQueue_Status
        ON dbo.ApprovalQueue(Status, SubmittedAt DESC);
END;
GO
