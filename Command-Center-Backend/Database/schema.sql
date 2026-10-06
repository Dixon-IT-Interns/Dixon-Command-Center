IF DB_ID(N'Dixon_Command_Center') IS NULL
BEGIN
    EXEC(N'CREATE DATABASE Dixon_Command_Center');
END;
GO

USE Dixon_Command_Center;
GO

IF OBJECT_ID(N'dbo.Role', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Role
    (
        RoleId INT IDENTITY(1,1) PRIMARY KEY,
        RoleName VARCHAR(50) NOT NULL,
        CONSTRAINT UQ_Role_Name UNIQUE (RoleName)
    );
END;
GO

IF OBJECT_ID(N'dbo.Plant', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Plant
    (
        PlantId INT IDENTITY(1,1) PRIMARY KEY,
        PlantName VARCHAR(100) NOT NULL,
        Location VARCHAR(150) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Plant_IsActive DEFAULT 1,
        CONSTRAINT UQ_Plant_Name UNIQUE (PlantName)
    );
END;
GO

IF OBJECT_ID(N'dbo.Customer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Customer
    (
        CustomerId INT IDENTITY(1,1) PRIMARY KEY,
        CustomerName VARCHAR(100) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Customer_IsActive DEFAULT 1,
        CONSTRAINT UQ_Customer_Name UNIQUE (CustomerName)
    );
END;
GO

IF OBJECT_ID(N'dbo.Category', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Category
    (
        CategoryId INT IDENTITY(1,1) PRIMARY KEY,
        CustomerId INT NOT NULL,
        CategoryName VARCHAR(20) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Category_IsActive DEFAULT 1,
        CONSTRAINT FK_Category_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customer(CustomerId),
        CONSTRAINT UQ_Category_Customer UNIQUE (CustomerId, CategoryName)
    );
END;
GO

IF OBJECT_ID(N'dbo.Model', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Model
    (
        ModelId INT IDENTITY(1,1) PRIMARY KEY,
        CustomerId INT NOT NULL,
        ModelName VARCHAR(100) NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_Model_IsActive DEFAULT 1,
        CONSTRAINT FK_Model_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customer(CustomerId),
        CONSTRAINT UQ_Model_Customer UNIQUE (CustomerId, ModelName)
    );
END;
GO

IF OBJECT_ID(N'dbo.ProductionLine', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductionLine
    (
        LineId INT IDENTITY(1,1) PRIMARY KEY,
        CategoryId INT NOT NULL,
        LineName VARCHAR(30) NOT NULL,
        SAPLocation VARCHAR(50) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_ProductionLine_IsActive DEFAULT 1,
        CONSTRAINT FK_Line_Category FOREIGN KEY (CategoryId) REFERENCES dbo.Category(CategoryId),
        CONSTRAINT UQ_Line_Category UNIQUE (CategoryId, LineName)
    );
END;
GO

IF OBJECT_ID(N'dbo.User', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.[User]
    (
        UserId INT IDENTITY(1,1) PRIMARY KEY,
        FullName VARCHAR(100) NOT NULL,
        Email VARCHAR(150) NULL,
        PasswordHash VARCHAR(255) NULL,
        RoleId INT NOT NULL,
        PlantId INT NOT NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_User_IsActive DEFAULT 1,
        CONSTRAINT FK_User_Plant FOREIGN KEY (PlantId) REFERENCES dbo.Plant(PlantId),
        CONSTRAINT UQ_User_Email UNIQUE (Email),
        CONSTRAINT FK_User_Role FOREIGN KEY (RoleId) REFERENCES dbo.Role(RoleId)
    );
END;
GO
/* Approval queue: submitted reports wait here until an assigned Approver reviews them. */
IF OBJECT_ID(N'dbo.ApprovalQueue', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ApprovalQueue
    (
        ApprovalQueueId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
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
        CONSTRAINT FK_ApprovalQueue_Report FOREIGN KEY (DailyReportId) REFERENCES dbo.DailyReport(DailyReportId),
        CONSTRAINT FK_ApprovalQueue_SubmittedBy FOREIGN KEY (SubmittedBy) REFERENCES dbo.[User](UserId),
        CONSTRAINT FK_ApprovalQueue_AssignedTo FOREIGN KEY (AssignedTo) REFERENCES dbo.[User](UserId),
        CONSTRAINT FK_ApprovalQueue_ReviewedBy FOREIGN KEY (ReviewedBy) REFERENCES dbo.[User](UserId)
    );
END;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name=N'UX_ApprovalQueue_DailyReport' AND object_id=OBJECT_ID(N'dbo.ApprovalQueue'))
    CREATE UNIQUE INDEX UX_ApprovalQueue_DailyReport ON dbo.ApprovalQueue(DailyReportId);
GO
