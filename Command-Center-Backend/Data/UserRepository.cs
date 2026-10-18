using Dixon.CommandCenter.API.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Data;

public sealed class UserRepository(SqlConnectionFactory connectionFactory)
{
    public async Task<bool> IsBootstrapRequiredAsync(
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            """
            SELECT CASE
                WHEN EXISTS (SELECT 1 FROM [User])
                THEN CAST(0 AS bit)
                ELSE CAST(1 AS bit)
            END
            """,
            connection);

        return (bool)(await command.ExecuteScalarAsync(
            cancellationToken))!;
    }

    public async Task<int?> BootstrapAdminAsync(
        BootstrapAdminRequest request,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        await using var countCommand = new SqlCommand(
            """
            SELECT COUNT_BIG(*)
            FROM [User] WITH (TABLOCKX, HOLDLOCK)
            """,
            connection,
            transaction);

        if ((long)(await countCommand.ExecuteScalarAsync(
            cancellationToken))! > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await using var insertCommand = new SqlCommand(
            """
            INSERT INTO [User]
            (
                FullName,
                Email,
                PasswordHash,
                RoleId,
                PlantId
            )
            OUTPUT INSERTED.UserId
            SELECT
                @FullName,
                @Email,
                @PasswordHash,
                r.RoleId,
                p.PlantId
            FROM Role r
            CROSS JOIN
            (
                SELECT TOP (1) PlantId
                FROM Plant
                WHERE IsActive = 1
                ORDER BY PlantId
            ) p
            WHERE r.RoleName = 'SuperAdmin'
            """,
            connection,
            transaction);

        insertCommand.Parameters.AddWithValue(
            "@FullName",
            request.FullName.Trim());

        insertCommand.Parameters.AddWithValue(
            "@Email",
            request.Email.Trim());

        insertCommand.Parameters.AddWithValue(
            "@PasswordHash",
            passwordHash);

        var result = await insertCommand.ExecuteScalarAsync(
            cancellationToken);

        if (result is null)
        {
            await transaction.RollbackAsync(cancellationToken);

            throw new InvalidOperationException(
                "Seed the SuperAdmin role and at least one active plant before initial setup.");
        }

        await transaction.CommitAsync(cancellationToken);

        return (int)result;
    }

    public async Task<StoredUser?> FindForLoginAsync(
        string email,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            """
            SELECT
                u.UserId,
                u.FullName,
                u.Email,
                u.PasswordHash,
                u.RoleId,
                r.RoleName,
                u.PlantId,
                p.PlantName
            FROM [User] u
            INNER JOIN Role r
                ON r.RoleId = u.RoleId
            INNER JOIN Plant p
                ON p.PlantId = u.PlantId
            WHERE
                u.Email = @Email
                AND u.IsActive = 1
                -- Super Admin is global: deactivating its originally assigned plant
                -- must not disable the Super Admin account. Other users still require
                -- an active plant to sign in.
                AND (r.RoleName = 'SuperAdmin' OR p.IsActive = 1)
            """,
            connection);

        command.Parameters.AddWithValue(
            "@Email",
            email);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredUser(
            new AuthenticatedUser(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetString(7)),
            reader.IsDBNull(3)
                ? null
                : reader.GetString(3));
    }

    public async Task<IReadOnlyList<UserResponse>> GetUsersAsync(
        int? plantId,
        bool includeSuperAdmins,
        CancellationToken cancellationToken)
    {
        var users = new List<UserResponse>();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        // -------------------------------------------------
        // 1. LOAD USERS
        // -------------------------------------------------

        await using (var command = new SqlCommand(
            """
            SELECT
                u.UserId,
                u.FullName,
                u.Email,
                u.RoleId,
                r.RoleName,
                u.PlantId,
                p.PlantName,
                u.IsActive
            FROM [User] u
            INNER JOIN Role r
                ON r.RoleId = u.RoleId
            INNER JOIN Plant p
                ON p.PlantId = u.PlantId
            WHERE (@PlantId IS NULL OR u.PlantId = @PlantId)
              AND (@IncludeSuperAdmins = 1 OR r.RoleName NOT IN ('Admin', 'SuperAdmin'))
              AND r.RoleName <> 'SuperAdmin'
            ORDER BY u.FullName
            """,
            connection))
        {
            command.Parameters.AddWithValue("@PlantId", (object?)plantId ?? DBNull.Value);
            command.Parameters.AddWithValue("@IncludeSuperAdmins", includeSuperAdmins);
            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                users.Add(
                    new UserResponse(
                        reader.GetInt32(0),
                        reader.GetString(1),
                        reader.IsDBNull(2)
                            ? null
                            : reader.GetString(2),
                        reader.GetInt32(3),
                        reader.GetString(4),
                        reader.GetInt32(5),
                        reader.GetString(6),
                        reader.GetBoolean(7),
                        []));
            }
        }

        // IMPORTANT:
        // The first DataReader is completely disposed before
        // opening the second command on the same SQL connection.

        // -------------------------------------------------
        // 2. LOAD USER-CUSTOMER MAPPINGS
        // -------------------------------------------------

        await using (var mappingCommand = new SqlCommand(
            """
            SELECT
                uc.UserId,
                c.CustomerId,
                c.CustomerName
            FROM UserCustomer uc
            INNER JOIN [User] u
                ON u.UserId = uc.UserId
            INNER JOIN PlantCustomer pc
                ON pc.PlantId = u.PlantId
               AND pc.CustomerId = uc.CustomerId
            INNER JOIN Customer c
                ON c.CustomerId = uc.CustomerId
            ORDER BY c.CustomerName
            """,
            connection))
        {
            await using var mappingReader =
                await mappingCommand.ExecuteReaderAsync(
                    cancellationToken);

            var map = users.ToDictionary(
                user => user.UserId,
                user => user.Customers.ToList());

            while (await mappingReader.ReadAsync(
                cancellationToken))
            {
                var userId = mappingReader.GetInt32(0);

                if (map.TryGetValue(
                    userId,
                    out var assigned))
                {
                    assigned.Add(
                        new UserCustomerResponse(
                            mappingReader.GetInt32(1),
                            mappingReader.GetString(2)));
                }
            }

            users = users
                .Select(user =>
                    user with
                    {
                        Customers = map[user.UserId]
                    })
                .ToList();
        }

        return users;
    }

    public async Task<UserResponse?> GetUserAsync(
        int id,
        int? plantId,
        bool includeSuperAdmins,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            """
            SELECT
                u.UserId,
                u.FullName,
                u.Email,
                u.RoleId,
                r.RoleName,
                u.PlantId,
                p.PlantName,
                u.IsActive
            FROM [User] u
            INNER JOIN Role r
                ON r.RoleId = u.RoleId
            INNER JOIN Plant p
                ON p.PlantId = u.PlantId
            WHERE u.UserId = @UserId
              AND (@PlantId IS NULL OR u.PlantId = @PlantId)
              AND (@IncludeSuperAdmins = 1 OR r.RoleName NOT IN ('Admin', 'SuperAdmin'))
              AND r.RoleName <> 'SuperAdmin'
            """,
            connection);

        command.Parameters.AddWithValue(
            "@UserId",
            id);
        command.Parameters.AddWithValue("@PlantId", (object?)plantId ?? DBNull.Value);
        command.Parameters.AddWithValue("@IncludeSuperAdmins", includeSuperAdmins);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var user = new UserResponse(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2)
                ? null
                : reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetInt32(5),
            reader.GetString(6),
            reader.GetBoolean(7),
            []);

        await reader.CloseAsync();

        // -------------------------------------------------
        // LOAD CUSTOMER ASSIGNMENTS
        // -------------------------------------------------

        await using var mappingCommand = new SqlCommand(
            """
            SELECT
                c.CustomerId,
                c.CustomerName
            FROM UserCustomer uc
            INNER JOIN [User] u
                ON u.UserId = uc.UserId
            INNER JOIN PlantCustomer pc
                ON pc.PlantId = u.PlantId
               AND pc.CustomerId = uc.CustomerId
            INNER JOIN Customer c
                ON c.CustomerId = uc.CustomerId
            WHERE uc.UserId = @UserId
            ORDER BY c.CustomerName
            """,
            connection);

        mappingCommand.Parameters.AddWithValue(
            "@UserId",
            id);

        var assigned =
            new List<UserCustomerResponse>();

        await using var mappingReader =
            await mappingCommand.ExecuteReaderAsync(
                cancellationToken);

        while (await mappingReader.ReadAsync(
            cancellationToken))
        {
            assigned.Add(
                new UserCustomerResponse(
                    mappingReader.GetInt32(0),
                    mappingReader.GetString(1)));
        }

        return user with
        {
            Customers = assigned
        };
    }

    public async Task<IReadOnlyList<RoleResponse>> GetRolesAsync(
        bool superAdmin,
        CancellationToken cancellationToken)
    {
        var roles = new List<RoleResponse>();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            """
            SELECT
                RoleId,
                RoleName
            FROM Role
            WHERE RoleName <> 'SuperAdmin'
              AND (@SuperAdmin = 1 OR RoleName NOT IN ('Admin', 'Viewer'))
            ORDER BY RoleName
            """,
            connection);
        command.Parameters.AddWithValue("@SuperAdmin", superAdmin);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            roles.Add(
                new RoleResponse(
                    reader.GetInt32(0),
                    reader.GetString(1)));
        }

        return roles;
    }

    public async Task<IReadOnlyList<PlantResponse>> GetPlantsAsync(
        int? plantId,
        CancellationToken cancellationToken)
    {
        var plants = new List<PlantResponse>();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            """
            SELECT
                PlantId,
                PlantName,
                Location
            FROM Plant
            WHERE IsActive = 1
              AND (@PlantId IS NULL OR PlantId = @PlantId)
            ORDER BY PlantName
            """,
            connection);
        command.Parameters.AddWithValue("@PlantId", (object?)plantId ?? DBNull.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            plants.Add(
                new PlantResponse(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2)
                        ? null
                        : reader.GetString(2)));
        }

        return plants;
    }

    public async Task<bool> UpdateCustomerAssignmentsAsync(
        int userId,
        IReadOnlyList<int> customerIds,
        int? plantScope,
        bool superAdmin,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            await using var userCommand = new SqlCommand(
                """
                SELECT u.PlantId
                FROM [User] u
                INNER JOIN Role r ON r.RoleId = u.RoleId
                WHERE u.UserId = @UserId
                  AND (@PlantScope IS NULL OR u.PlantId = @PlantScope)
                  AND r.RoleName <> 'SuperAdmin'
                  AND (@SuperAdmin = 1 OR r.RoleName <> 'Admin')
                """,
                connection,
                transaction);
            userCommand.Parameters.AddWithValue("@UserId", userId);
            userCommand.Parameters.AddWithValue("@PlantScope", (object?)plantScope ?? DBNull.Value);
            userCommand.Parameters.AddWithValue("@SuperAdmin", superAdmin);
            var plantResult = await userCommand.ExecuteScalarAsync(cancellationToken);
            int? plantId = plantResult is null or DBNull ? null : Convert.ToInt32(plantResult);

            if (plantId is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await EnsureCustomersBelongToPlantAsync(
                connection,
                transaction,
                plantId.Value,
                customerIds,
                cancellationToken);

            await using (var clear = new SqlCommand(
                """
                DELETE FROM UserCustomer
                WHERE UserId = @UserId
                """,
                connection,
                transaction))
            {
                clear.Parameters.AddWithValue(
                    "@UserId",
                    userId);

                await clear.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            foreach (var customerId in customerIds.Distinct())
            {
                await using var add = new SqlCommand(
                    """
                    INSERT INTO UserCustomer
                    (
                        UserId,
                        CustomerId
                    )
                    VALUES
                    (
                        @UserId,
                        @CustomerId
                    )
                    """,
                    connection,
                    transaction);

                add.Parameters.AddWithValue(
                    "@UserId",
                    userId);

                add.Parameters.AddWithValue(
                    "@CustomerId",
                    customerId);

                await add.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await transaction.CommitAsync(
                cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw;
        }
    }

    public async Task<UserResponse?> UpdateAsync(
        int userId,
        UpdateUserRequest request,
        string? passwordHash,
        int? plantScope,
        bool superAdmin,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            await EnsureCustomersBelongToPlantAsync(
                connection,
                transaction,
                request.PlantId,
                request.CustomerIds,
                cancellationToken);

            await ValidateRoleAssignmentAsync(
                connection,
                transaction,
                request.RoleId,
                superAdmin,
                cancellationToken);

            var sql = passwordHash is null
                ? """
                  UPDATE [User]
                  SET
                      FullName = @FullName,
                      Email = @Email,
                      RoleId = @RoleId,
                      PlantId = @PlantId
                  WHERE UserId = @UserId
                    AND (@PlantScope IS NULL OR UserId IN (SELECT UserId FROM [User] WHERE PlantId = @PlantScope))
                    AND (@SuperAdmin = 1 OR UserId IN
                    (
                        SELECT u.UserId FROM [User] u
                        INNER JOIN Role r ON r.RoleId = u.RoleId
                        WHERE r.RoleName NOT IN ('Admin', 'SuperAdmin')
                    ))
                    AND (@PlantScope IS NULL OR PlantId = @PlantScope)
                    AND (@SuperAdmin = 1 OR RoleId <> (SELECT RoleId FROM Role WHERE RoleName = 'Admin'))
                    AND RoleId <> (SELECT RoleId FROM Role WHERE RoleName = 'SuperAdmin')
                  """
                : """
                  UPDATE [User]
                  SET
                      FullName = @FullName,
                      Email = @Email,
                      RoleId = @RoleId,
                      PlantId = @PlantId,
                      PasswordHash = @PasswordHash
                  WHERE UserId = @UserId
                    AND (@PlantScope IS NULL OR PlantId = @PlantScope)
                    AND (@SuperAdmin = 1 OR RoleId <> (SELECT RoleId FROM Role WHERE RoleName = 'Admin'))
                    AND RoleId <> (SELECT RoleId FROM Role WHERE RoleName = 'SuperAdmin')
                  """;

            await using var command = new SqlCommand(
                sql,
                connection,
                transaction);

            command.Parameters.AddWithValue(
                "@UserId",
                userId);
            command.Parameters.AddWithValue("@PlantScope", (object?)plantScope ?? DBNull.Value);
            command.Parameters.AddWithValue("@SuperAdmin", superAdmin);

            command.Parameters.AddWithValue(
                "@FullName",
                request.FullName.Trim());

            command.Parameters.AddWithValue(
                "@Email",
                request.Email.Trim());

            command.Parameters.AddWithValue(
                "@RoleId",
                request.RoleId);

            command.Parameters.AddWithValue(
                "@PlantId",
                request.PlantId);

            if (passwordHash is not null)
            {
                command.Parameters.AddWithValue(
                    "@PasswordHash",
                    passwordHash);
            }

            if (await command.ExecuteNonQueryAsync(
                    cancellationToken) == 0)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return null;
            }

            // -------------------------------------------------
            // CLEAR OLD CUSTOMER ASSIGNMENTS
            // -------------------------------------------------

            await using var clear = new SqlCommand(
                """
                DELETE FROM UserCustomer
                WHERE UserId = @UserId
                """,
                connection,
                transaction);

            clear.Parameters.AddWithValue(
                "@UserId",
                userId);

            await clear.ExecuteNonQueryAsync(
                cancellationToken);

            // -------------------------------------------------
            // ADD NEW CUSTOMER ASSIGNMENTS
            // -------------------------------------------------

            foreach (var customerId in
                     request.CustomerIds.Distinct())
            {
                await using var add = new SqlCommand(
                    """
                    INSERT INTO UserCustomer
                    (
                        UserId,
                        CustomerId
                    )
                    VALUES
                    (
                        @UserId,
                        @CustomerId
                    )
                    """,
                    connection,
                    transaction);

                add.Parameters.AddWithValue(
                    "@UserId",
                    userId);
                add.Parameters.AddWithValue(
                    "@CustomerId",
                    customerId);

                await add.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await transaction.CommitAsync(
                cancellationToken);

            return await GetUserAsync(
                userId,
                plantScope,
                superAdmin,
                cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw;
        }
    }

    public async Task<bool> SetActiveAsync(
        int userId,
        bool isActive,
        int? plantScope,
        bool superAdmin,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            """
            UPDATE [User]
            SET IsActive = @IsActive
            WHERE UserId = @UserId
              AND (@PlantScope IS NULL OR PlantId = @PlantScope)
              AND RoleId <> (SELECT RoleId FROM Role WHERE RoleName = 'SuperAdmin')
              AND (@SuperAdmin = 1 OR RoleId <> (SELECT RoleId FROM Role WHERE RoleName = 'Admin'))
            """,
            connection);

        command.Parameters.AddWithValue(
            "@IsActive",
            isActive);

        command.Parameters.AddWithValue(
            "@UserId",
            userId);
        command.Parameters.AddWithValue("@PlantScope", (object?)plantScope ?? DBNull.Value);
        command.Parameters.AddWithValue("@SuperAdmin", superAdmin);

        return await command.ExecuteNonQueryAsync(
            cancellationToken) > 0;
    }

    public async Task<int> CreateAsync(
        CreateUserRequest request,
        string passwordHash,
        bool superAdmin,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            await ValidateRoleAssignmentAsync(
                connection,
                transaction,
                request.RoleId,
                superAdmin,
                cancellationToken);

            await EnsureCustomersBelongToPlantAsync(
                connection,
                transaction,
                request.PlantId,
                request.CustomerIds,
                cancellationToken);

            // -------------------------------------------------
            // 1. CREATE USER
            // -------------------------------------------------

            await using var userCommand = new SqlCommand(
                """
                INSERT INTO [User]
                (
                    FullName,
                    Email,
                    PasswordHash,
                    RoleId,
                    PlantId
                )
                OUTPUT INSERTED.UserId
                SELECT
                    @FullName,
                    @Email,
                    @PasswordHash,
                    @RoleId,
                    @PlantId
                WHERE EXISTS
                (
                    SELECT 1
                    FROM Role
                    WHERE RoleId = @RoleId
                      AND RoleName <> 'SuperAdmin'
                      AND (@SuperAdmin = 1 OR RoleName NOT IN ('Admin', 'SuperAdmin', 'Viewer'))
                )
                """,
                connection,
                transaction);

            userCommand.Parameters.AddWithValue(
                "@FullName",
                request.FullName.Trim());

            userCommand.Parameters.AddWithValue(
                "@Email",
                request.Email.Trim());

            userCommand.Parameters.AddWithValue(
                "@PasswordHash",
                passwordHash);

            userCommand.Parameters.AddWithValue(
                "@RoleId",
                request.RoleId);

            userCommand.Parameters.AddWithValue(
                "@PlantId",
                request.PlantId);
            userCommand.Parameters.AddWithValue("@SuperAdmin", superAdmin);

            var result = await userCommand.ExecuteScalarAsync(
                cancellationToken);

            if (result is null)
            {
                throw new UserRoleAssignmentException();
            }

            var userId = Convert.ToInt32(result);

            // -------------------------------------------------
            // 2. ASSIGN CUSTOMERS TO USER
            // -------------------------------------------------

            foreach (var customerId in
                     request.CustomerIds.Distinct())
            {
                await using var customerCommand =
                    new SqlCommand(
                        """
                        INSERT INTO UserCustomer
                        (
                            UserId,
                            CustomerId
                        )
                        VALUES
                        (
                            @UserId,
                            @CustomerId
                        )
                        """,
                        connection,
                        transaction);

                customerCommand.Parameters.AddWithValue(
                    "@UserId",
                    userId);

                customerCommand.Parameters.AddWithValue(
                    "@CustomerId",
                    customerId);

                await customerCommand.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            // -------------------------------------------------
            // 3. COMMIT
            // -------------------------------------------------

            await transaction.CommitAsync(
                cancellationToken);

            return userId;
        }
        catch
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw;
        }
    }

    private static async Task EnsureCustomersBelongToPlantAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int plantId,
        IReadOnlyList<int> customerIds,
        CancellationToken cancellationToken)
    {
        var distinctCustomerIds = customerIds.Distinct().ToArray();
        if (distinctCustomerIds.Length == 0)
        {
            return;
        }

        if (distinctCustomerIds.Any(customerId => customerId <= 0))
        {
            throw new PlantCustomerAssignmentException();
        }

        var parameters = distinctCustomerIds
            .Select((_, index) => $"@CustomerId{index}")
            .ToArray();
        var sql = $"""
            SELECT COUNT(DISTINCT pc.CustomerId)
            FROM PlantCustomer pc
            INNER JOIN Customer c ON c.CustomerId = pc.CustomerId
            WHERE pc.PlantId = @PlantId
              AND c.IsActive = 1
              AND pc.CustomerId IN ({string.Join(", ", parameters)})
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@PlantId", plantId);
        for (var index = 0; index < distinctCustomerIds.Length; index++)
        {
            command.Parameters.AddWithValue(parameters[index], distinctCustomerIds[index]);
        }

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (count != distinctCustomerIds.Length)
        {
            throw new PlantCustomerAssignmentException();
        }
    }

    private static async Task ValidateRoleAssignmentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int roleId,
        bool superAdmin,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            """
            SELECT COUNT(1)
            FROM Role
            WHERE RoleId = @RoleId
              AND RoleName <> 'SuperAdmin'
              AND (@SuperAdmin = 1 OR RoleName NOT IN ('Admin', 'SuperAdmin', 'Viewer'))
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("@RoleId", roleId);
        command.Parameters.AddWithValue("@SuperAdmin", superAdmin);

        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
        {
            throw new UserRoleAssignmentException();
        }
    }
}

public sealed record StoredUser(
    AuthenticatedUser User,
    string? PasswordHash);

public sealed class PlantCustomerAssignmentException : Exception
{
    public PlantCustomerAssignmentException()
        : base("One or more selected customers are not assigned to the selected plant.")
    {
    }
}

public sealed class UserRoleAssignmentException : Exception
{
    public UserRoleAssignmentException()
        : base("You are not permitted to assign that role.")
    {
    }
}