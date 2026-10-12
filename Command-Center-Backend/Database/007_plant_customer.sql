USE Dixon_Command_Center;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.PlantCustomer', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlantCustomer
    (
        PlantId INT NOT NULL,
        CustomerId INT NOT NULL,
        CreatedAt DATETIME2(7) NOT NULL CONSTRAINT DF_PlantCustomer_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_PlantCustomer PRIMARY KEY (PlantId, CustomerId),
        CONSTRAINT FK_PlantCustomer_Plant FOREIGN KEY (PlantId) REFERENCES dbo.Plant(PlantId),
        CONSTRAINT FK_PlantCustomer_Customer FOREIGN KEY (CustomerId) REFERENCES dbo.Customer(CustomerId)
    );
END;

IF EXISTS
(
    SELECT 1
    FROM sys.key_constraints
    WHERE name = N'UQ_Customer_Name'
      AND parent_object_id = OBJECT_ID(N'dbo.Customer')
)
BEGIN
    ALTER TABLE dbo.Customer DROP CONSTRAINT UQ_Customer_Name;
END;

-- Keep single-plant installations working and recover assignments from existing user access.
IF (SELECT COUNT_BIG(*) FROM dbo.Plant) = 1
BEGIN
    INSERT INTO dbo.PlantCustomer (PlantId, CustomerId)
    SELECT p.PlantId, c.CustomerId
    FROM dbo.Plant p
    CROSS JOIN dbo.Customer c
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.PlantCustomer pc
        WHERE pc.PlantId = p.PlantId
          AND pc.CustomerId = c.CustomerId
    );
END;

INSERT INTO dbo.PlantCustomer (PlantId, CustomerId)
SELECT DISTINCT u.PlantId, uc.CustomerId
FROM dbo.UserCustomer uc
INNER JOIN dbo.[User] u ON u.UserId = uc.UserId
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.PlantCustomer pc
    WHERE pc.PlantId = u.PlantId
      AND pc.CustomerId = uc.CustomerId
);

IF OBJECT_ID(N'dbo.DailyReport', N'U') IS NOT NULL
BEGIN
    INSERT INTO dbo.PlantCustomer (PlantId, CustomerId)
    SELECT DISTINCT u.PlantId, dr.CustomerId
    FROM dbo.DailyReport dr
    INNER JOIN dbo.[User] u ON u.UserId = dr.CreatedBy
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.PlantCustomer pc
        WHERE pc.PlantId = u.PlantId
          AND pc.CustomerId = dr.CustomerId
    );
END;

COMMIT TRANSACTION;
GO
