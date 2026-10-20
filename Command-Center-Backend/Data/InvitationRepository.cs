using System.Security.Cryptography;
using System.Text;
using Dixon.CommandCenter.API.Services;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Data;

public sealed class InvitationRepository(SqlConnectionFactory connectionFactory)
{
    public async Task<string> CreateInvitationAsync(
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

        await using var command = new SqlCommand(
            """
            INSERT INTO UserInvitation
                (UserId, TokenHash, ExpiresAt, CreatedAt, UsedAt)
            VALUES
                (@UserId, @TokenHash,
                 DATEADD(HOUR, 24, SYSUTCDATETIME()),
                 SYSUTCDATETIME(), NULL)
            """,
            connection);

        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@TokenHash", tokenHash);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return token;
    }

    public async Task<bool> SetPasswordAsync(
        string token,
        string password,
        CancellationToken cancellationToken)
    {
        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                cancellationToken);

        try
        {
            await using var command = new SqlCommand(
                """
                UPDATE u
                SET PasswordHash = @PasswordHash
                FROM [User] AS u
                INNER JOIN UserInvitation AS i
                    ON i.UserId = u.UserId
                WHERE i.TokenHash = @TokenHash
                  AND i.UsedAt IS NULL
                  AND i.ExpiresAt > SYSUTCDATETIME()
                  AND u.IsActive = 1
                  AND NOT EXISTS
                  (
                      SELECT 1
                      FROM UserInvitation AS newer
                      WHERE newer.UserId = i.UserId
                        AND newer.InvitationId > i.InvitationId
                        AND newer.UsedAt IS NULL
                  );

                IF @@ROWCOUNT = 1
                BEGIN
                    UPDATE UserInvitation
                    SET UsedAt = SYSUTCDATETIME()
                    WHERE TokenHash = @TokenHash
                      AND UsedAt IS NULL;
                END
                """,
                connection,
                transaction);

            command.Parameters.AddWithValue("@TokenHash", tokenHash);
            command.Parameters.AddWithValue(
                "@PasswordHash",
                PasswordService.Hash(password));

            var changed = await command.ExecuteNonQueryAsync(
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return changed >= 2;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
