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
            WHERE r.RoleName = 'Admin'
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
                "Seed the Admin role and at least one active plant before initial setup.");
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
                AND p.IsActive = 1
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
            ORDER BY u.FullName
            """,
            connection))
        {
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
            """,
            connection);

        command.Parameters.AddWithValue(
            "@UserId",
            id);

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
            ORDER BY RoleName
            """,
            connection);

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
            ORDER BY PlantName
            """,
            connection);

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

    public async Task UpdateCustomerAssignmentsAsync(
        int userId,
        IReadOnlyList<int> customerIds,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
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
            var sql = passwordHash is null
                ? """
                  UPDATE [User]
                  SET
                      FullName = @FullName,
                      Email = @Email,
                      RoleId = @RoleId,
                      PlantId = @PlantId
                  WHERE UserId = @UserId
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
                  """;

            await using var command = new SqlCommand(
                sql,
                connection,
                transaction);

            command.Parameters.AddWithValue(
                "@UserId",
                userId);

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
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(
            """
            UPDATE [User]
            SET IsActive = @IsActive
            WHERE UserId = @UserId
            """,
            connection);

        command.Parameters.AddWithValue(
            "@IsActive",
            isActive);

        command.Parameters.AddWithValue(
            "@UserId",
            userId);

        return await command.ExecuteNonQueryAsync(
            cancellationToken) > 0;
    }

    public async Task<int> CreateAsync(
        CreateUserRequest request,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
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
                VALUES
                (
                    @FullName,
                    @Email,
                    @PasswordHash,
                    @RoleId,
                    @PlantId
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

            var result = await userCommand.ExecuteScalarAsync(
                cancellationToken);

            if (result is null)
            {
                throw new InvalidOperationException(
                    "User could not be created.");
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
}

public sealed record StoredUser(
    AuthenticatedUser User,
    string? PasswordHash);