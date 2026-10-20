USE Dixon_Command_Center;
GO

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.Role WHERE RoleName = 'SuperAdmin')
BEGIN
    INSERT INTO dbo.Role (RoleName) VALUES ('SuperAdmin');
END;

-- Preserve the existing system-wide administrator as the initial SuperAdmin.
IF NOT EXISTS
(
    SELECT 1
    FROM dbo.[User] u
    INNER JOIN dbo.Role r ON r.RoleId = u.RoleId
    WHERE r.RoleName = 'SuperAdmin'
)
BEGIN
    ;WITH FirstExistingAdmin AS
    (
        SELECT TOP (1) u.UserId
        FROM dbo.[User] u
        INNER JOIN dbo.Role currentRole ON currentRole.RoleId = u.RoleId
        WHERE currentRole.RoleName = 'Admin'
        ORDER BY u.UserId
    )
    UPDATE u
    SET RoleId = superAdminRole.RoleId
    FROM dbo.[User] u
    INNER JOIN FirstExistingAdmin firstAdmin ON firstAdmin.UserId = u.UserId
    CROSS JOIN dbo.Role superAdminRole
    WHERE superAdminRole.RoleName = 'SuperAdmin';
END;

COMMIT TRANSACTION;
GO
