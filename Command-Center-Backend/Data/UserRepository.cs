using Dixon.CommandCenter.API.Models;
using System.Data;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Data;

public sealed class UserRepository(SqlConnectionFactory connectionFactory)
{
    public async Task<bool> IsBootstrapRequiredAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT CASE WHEN EXISTS (SELECT 1 FROM [User]) THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END", connection);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    public async Task<int?> BootstrapAdminAsync(BootstrapAdminRequest request, string passwordHash, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var countCommand = new SqlCommand("SELECT COUNT_BIG(*) FROM [User] WITH (TABLOCKX, HOLDLOCK)", connection, transaction);
        if ((long)(await countCommand.ExecuteScalarAsync(cancellationToken))! > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await using var insertCommand = new SqlCommand("""
            INSERT INTO [User] (FullName, Email, PasswordHash, RoleId, PlantId)
            OUTPUT INSERTED.UserId
            SELECT @FullName, @Email, @PasswordHash, r.RoleId, p.PlantId
            FROM Role r
            CROSS JOIN (SELECT TOP (1) PlantId FROM Plant WHERE IsActive = 1 ORDER BY PlantId) p
            WHERE r.RoleName = 'Admin'
            """, connection, transaction);
        insertCommand.Parameters.AddWithValue("@FullName", request.FullName.Trim());
        insertCommand.Parameters.AddWithValue("@Email", request.Email.Trim());
        insertCommand.Parameters.AddWithValue("@PasswordHash", passwordHash);
        var result = await insertCommand.ExecuteScalarAsync(cancellationToken);
        if (result is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("Seed the Admin role and at least one active plant before initial setup.");
        }

        await transaction.CommitAsync(cancellationToken);
        return (int)result;
    }

    public async Task<StoredUser?> FindForLoginAsync(string email, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT u.UserId, u.FullName, u.Email, u.PasswordHash, u.RoleId, r.RoleName,
                   u.PlantId, p.PlantName
            FROM [User] u
            INNER JOIN Role r ON r.RoleId = u.RoleId
            INNER JOIN Plant p ON p.PlantId = u.PlantId
            WHERE u.Email = @Email AND u.IsActive = 1 AND p.IsActive = 1
            """, connection);
        command.Parameters.AddWithValue("@Email", email);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredUser(
            new AuthenticatedUser(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(4), reader.GetString(5), reader.GetInt32(6), reader.GetString(7)),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    public async Task<IReadOnlyList<UserResponse>> GetUsersAsync(CancellationToken cancellationToken)
    {
        var users = new List<UserResponse>();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT u.UserId, u.FullName, u.Email, u.RoleId, r.RoleName, u.PlantId, p.PlantName, u.IsActive
            FROM [User] u
            INNER JOIN Role r ON r.RoleId = u.RoleId
            INNER JOIN Plant p ON p.PlantId = u.PlantId
            ORDER BY u.FullName
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            users.Add(new UserResponse(
                reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3), reader.GetString(4), reader.GetInt32(5), reader.GetString(6), reader.GetBoolean(7)));
        }

        return users;
    }

    public async Task<UserResponse?> GetUserAsync(int id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT u.UserId, u.FullName, u.Email, u.RoleId, r.RoleName, u.PlantId, p.PlantName, u.IsActive
            FROM [User] u
            INNER JOIN Role r ON r.RoleId = u.RoleId
            INNER JOIN Plant p ON p.PlantId = u.PlantId
            WHERE u.UserId = @UserId
            """, connection);
        command.Parameters.AddWithValue("@UserId", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new UserResponse(
            reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetInt32(3), reader.GetString(4), reader.GetInt32(5), reader.GetString(6), reader.GetBoolean(7));
    }

    public async Task<IReadOnlyList<RoleResponse>> GetRolesAsync(CancellationToken cancellationToken)
    {
        var roles = new List<RoleResponse>();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT RoleId, RoleName FROM Role ORDER BY RoleName", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            roles.Add(new RoleResponse(reader.GetInt32(0), reader.GetString(1)));
        }

        return roles;
    }

    public async Task<IReadOnlyList<PlantResponse>> GetPlantsAsync(CancellationToken cancellationToken)
    {
        var plants = new List<PlantResponse>();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT PlantId, PlantName, Location FROM Plant WHERE IsActive = 1 ORDER BY PlantName", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            plants.Add(new PlantResponse(reader.GetInt32(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return plants;
    }

    public async Task<int> CreateAsync(CreateUserRequest request, string passwordHash, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            INSERT INTO [User] (FullName, Email, PasswordHash, RoleId, PlantId)
            OUTPUT INSERTED.UserId
            VALUES (@FullName, @Email, @PasswordHash, @RoleId, @PlantId)
            """, connection);
        command.Parameters.AddWithValue("@FullName", request.FullName.Trim());
        command.Parameters.AddWithValue("@Email", request.Email.Trim());
        command.Parameters.AddWithValue("@PasswordHash", passwordHash);
        command.Parameters.AddWithValue("@RoleId", request.RoleId);
        command.Parameters.AddWithValue("@PlantId", request.PlantId);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}

public sealed record StoredUser(AuthenticatedUser User, string? PasswordHash);