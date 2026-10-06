USE Dixon_Command_Center;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

INSERT INTO Role (RoleName)
SELECT names.RoleName
FROM (VALUES ('Admin'), ('Approver'), ('Contributor'), ('Viewer')) AS names(RoleName)
WHERE NOT EXISTS (SELECT 1 FROM Role r WHERE r.RoleName = names.RoleName);

INSERT INTO Plant (PlantName, Location)
SELECT 'Dixon Chennai', 'Chennai'
WHERE NOT EXISTS (SELECT 1 FROM Plant WHERE PlantName = 'Dixon Chennai');

INSERT INTO Customer (CustomerName)
SELECT names.CustomerName
FROM (VALUES ('HP'), ('ASUS'), ('Acer'), ('Gigabyte')) AS names(CustomerName)
WHERE NOT EXISTS (SELECT 1 FROM Customer c WHERE c.CustomerName = names.CustomerName);

INSERT INTO Category (CustomerId, CategoryName)
SELECT c.CustomerId, names.CategoryName
FROM (VALUES
    ('HP', 'FATP'), ('HP', 'SMT'),
    ('ASUS', 'FATP'), ('ASUS', 'SMT'),
    ('Acer', 'FATP'), ('Acer', 'SMT'),
    ('Gigabyte', 'FATP'), ('Gigabyte', 'SMT')
) AS names(CustomerName, CategoryName)
INNER JOIN Customer c ON c.CustomerName = names.CustomerName
WHERE NOT EXISTS (
    SELECT 1 FROM Category existing
    WHERE existing.CustomerId = c.CustomerId AND existing.CategoryName = names.CategoryName
);

INSERT INTO Model (CustomerId, ModelName)
SELECT c.CustomerId, names.ModelName
FROM (VALUES
    ('HP', 'AIO'), ('HP', 'NB'), ('HP', 'NB(Sebastian)'), ('HP', 'AIO/NB'),
    ('ASUS', 'X VAPB'), ('ASUS', 'X,F, M Series'), ('ASUS', 'E Series'),
    ('ASUS', 'X Series (3100)'), ('ASUS', 'X Series (4100)'),
    ('Acer', 'Travel lite'), ('Gigabyte', 'Gigabyte Model A')
) AS names(CustomerName, ModelName)
INNER JOIN Customer c ON c.CustomerName = names.CustomerName
WHERE NOT EXISTS (
    SELECT 1 FROM Model existing
    WHERE existing.CustomerId = c.CustomerId AND existing.ModelName = names.ModelName
);

UPDATE line
SET CategoryId = targetCategory.CategoryId
FROM ProductionLine line
INNER JOIN Category currentCategory ON currentCategory.CategoryId = line.CategoryId
INNER JOIN Customer currentCustomer ON currentCustomer.CustomerId = currentCategory.CustomerId
INNER JOIN Customer targetCustomer ON targetCustomer.CustomerName = 'Gigabyte'
INNER JOIN Category targetCategory ON targetCategory.CustomerId = targetCustomer.CustomerId AND targetCategory.CategoryName = 'FATP'
WHERE currentCustomer.CustomerName = 'Acer'
    AND currentCategory.CategoryName = 'SMT'
    AND line.LineName = 'FA-06'
    AND line.SAPLocation = '3FAI'
    AND NOT EXISTS (
            SELECT 1 FROM ProductionLine existing
            WHERE existing.CategoryId = targetCategory.CategoryId AND existing.LineName = line.LineName
    );

INSERT INTO ProductionLine (CategoryId, LineName, SAPLocation)
SELECT c.CategoryId, lines.LineName, lines.SAPLocation
FROM (VALUES
    ('HP', 'FATP', 'FA-01', '3FAI'), ('HP', 'FATP', 'FA-02', '3FAI'), ('HP', 'FATP', 'FA-03', '3FAI'),
    ('HP', 'SMT', 'SMT-01', '3SAI'), ('HP', 'SMT', 'SMT-02', '3SAI'),
    ('ASUS', 'FATP', 'FA-04', '3FAI'), ('ASUS', 'FATP', 'FA-05', '3FAI'),
    ('ASUS', 'SMT', 'SMT-04', '3SAI'), ('ASUS', 'SMT', 'SMT-05', '3SAI'),
    ('Acer', 'FATP', 'FA-06', '3FAI'),
    ('Gigabyte', 'FATP', 'FA-06', '3FAI')
) AS lines(CustomerName, CategoryName, LineName, SAPLocation)
INNER JOIN Customer customer ON customer.CustomerName = lines.CustomerName
INNER JOIN Category c ON c.CustomerId = customer.CustomerId AND c.CategoryName = lines.CategoryName
WHERE NOT EXISTS (
    SELECT 1 FROM ProductionLine existing
    WHERE existing.CategoryId = c.CategoryId AND existing.LineName = lines.LineName
);

COMMIT TRANSACTION;
GO
/* ============================================================
   CONTRIBUTOR DAILY REPORT KPI MASTER SEED
   September 2026 sample targets for the current Command Center
   reporting flow. Safe to run repeatedly because KPI_Master has
   a unique key across customer/category/model/line/year/month.
   ============================================================ */

DECLARE @SeedYear INT = 2026;
DECLARE @SeedMonth INT = 9;

INSERT INTO KPI_Master
(
    CustomerId,
    CategoryId,
    ModelId,
    LineId,
    KPIYEAR,
    KPIMonth,
    ProductionPlan,
    UPHTarget,
    UPPHTarget,
    CPHTarget,
    FPYTarget,
    FTYTarget,
    RTYTarget,
    OSDTarget,
    OTTarget,
    IsActive,
    CreatedAt,
    UpdatedAt
)
SELECT
    c.CustomerId,
    cat.CategoryId,
    m.ModelId,
    pl.LineId,
    @SeedYear,
    @SeedMonth,
    CASE
        WHEN cat.CategoryName = 'FATP' THEN 12000
        ELSE 15000
    END AS ProductionPlan,
    CASE
        WHEN cat.CategoryName = 'FATP' THEN 55
        ELSE 80
    END AS UPHTarget,
    CASE
        WHEN cat.CategoryName = 'FATP' THEN 45
        ELSE 65
    END AS UPPHTarget,
    CASE
        WHEN cat.CategoryName = 'FATP' THEN 50
        ELSE 72
    END AS CPHTarget,
    0.98 AS FPYTarget,
    0.97 AS FTYTarget,
    0.95 AS RTYTarget,
    0.02 AS OSDTarget,
    8.00 AS OTTarget,
    1,
    SYSUTCDATETIME(),
    SYSUTCDATETIME()
FROM Customer c
INNER JOIN Category cat
    ON cat.CustomerId = c.CustomerId
   AND cat.IsActive = 1
INNER JOIN ProductionLine pl
    ON pl.CategoryId = cat.CategoryId
   AND pl.IsActive = 1
INNER JOIN Model m
    ON m.CustomerId = c.CustomerId
   AND m.IsActive = 1
WHERE c.IsActive = 1
  AND NOT EXISTS
  (
      SELECT 1
      FROM KPI_Master existing
      WHERE existing.CustomerId = c.CustomerId
        AND existing.CategoryId = cat.CategoryId
        AND existing.ModelId = m.ModelId
        AND existing.LineId = pl.LineId
        AND existing.KPIYEAR = @SeedYear
        AND existing.KPIMonth = @SeedMonth
  );
GO
