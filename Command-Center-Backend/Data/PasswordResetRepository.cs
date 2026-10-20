
using System.Security.Cryptography;
using System.Text;
using Dixon.CommandCenter.API.Services;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Data;

public sealed class PasswordResetRepository(
    SqlConnectionFactory connectionFactory)
{
    public async Task<string?> CreateResetAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var token = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE dbo.UserPasswordReset
            SET UsedAt = SYSUTCDATETIME()
            WHERE UserId = @UserId
              AND UsedAt IS NULL;

            IF EXISTS
            (
                SELECT 1
                FROM dbo.[User]
                WHERE UserId = @UserId
                  AND IsActive = 1
            )
            BEGIN
                INSERT INTO dbo.UserPasswordReset
                    (UserId, TokenHash, ExpiresAt, CreatedAt, UsedAt)
                VALUES
                    (
                        @UserId,
                        @TokenHash,
                        DATEADD(MINUTE, 30, SYSUTCDATETIME()),
                        SYSUTCDATETIME(),
                        NULL
                    );

                SELECT CAST(1 AS INT);
            END
            ELSE
            BEGIN
                SELECT CAST(0 AS INT);
            END;
            """;

        await using var command = new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@TokenHash", tokenHash);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is not null && Convert.ToInt32(result) == 1
            ? token
            : null;
    }

    public async Task<bool> ResetPasswordAsync(
        string token,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(password) ||
            password.Length < 8 ||
            password.Length > 128)
        {
            return false;
        }

        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            const string sql = """
                UPDATE u
                SET PasswordHash = @PasswordHash
                FROM dbo.[User] AS u
                INNER JOIN dbo.UserPasswordReset AS r
                    ON r.UserId = u.UserId
                WHERE r.TokenHash = @TokenHash
                  AND r.UsedAt IS NULL
                  AND r.ExpiresAt > SYSUTCDATETIME()
                  AND u.IsActive = 1;

                IF @@ROWCOUNT = 1
                BEGIN
                    UPDATE dbo.UserPasswordReset
                    SET UsedAt = SYSUTCDATETIME()
                    WHERE TokenHash = @TokenHash
                      AND UsedAt IS NULL;
                END;

                SELECT @@ROWCOUNT;
                """;

            await using var command = new SqlCommand(
                sql, connection, transaction);

            command.Parameters.AddWithValue("@TokenHash", tokenHash);
            command.Parameters.AddWithValue(
                "@PasswordHash", PasswordService.Hash(password));

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var tokenMarkedUsed = result is not null &&
                                  Convert.ToInt32(result) == 1;

            if (!tokenMarkedUsed)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
