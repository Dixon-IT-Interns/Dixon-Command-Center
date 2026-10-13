using System.Data;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Data;

public sealed class ViewerRepository(SqlConnectionFactory connectionFactory)
{
    private const string AccessibleLines = """
        WITH AccessibleLines AS
        (
            SELECT
                c.CustomerId,
                c.CustomerName,
                cat.CategoryId,
                cat.CategoryName AS Process,
                pl.LineId,
                pl.LineName
            FROM PlantCustomer pc
            INNER JOIN Customer c ON c.CustomerId = pc.CustomerId
            INNER JOIN Category cat
                ON cat.CustomerId = c.CustomerId
               AND cat.PlantId = pc.PlantId
               AND cat.IsActive = 1
            INNER JOIN ProductionLine pl
                ON pl.CategoryId = cat.CategoryId
               AND pl.PlantId = pc.PlantId
               AND pl.IsActive = 1
            INNER JOIN Plant p ON p.PlantId = pc.PlantId
            WHERE pc.PlantId = @PlantId
              AND c.IsActive = 1
              AND p.IsActive = 1
              AND (@CustomerId IS NULL OR c.CustomerId = @CustomerId)
              AND (@Process = 'ALL' OR cat.CategoryName = @Process)
              AND EXISTS
              (
                  SELECT 1
                  FROM UserCustomer uc
                  WHERE uc.UserId = @UserId
                    AND uc.CustomerId = c.CustomerId
              )
        )
        """;

    public async Task<bool> HasCustomerAccessAsync(
        int userId,
        int plantId,
        int customerId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT COUNT(1)
            FROM PlantCustomer pc
            INNER JOIN Customer c ON c.CustomerId = pc.CustomerId
            INNER JOIN Plant p ON p.PlantId = pc.PlantId
            WHERE pc.PlantId = @PlantId
              AND pc.CustomerId = @CustomerId
              AND c.IsActive = 1
              AND p.IsActive = 1
              AND EXISTS
              (
                  SELECT 1
                  FROM UserCustomer uc
                  WHERE uc.UserId = @UserId
                    AND uc.CustomerId = pc.CustomerId
              );
            """, connection);

        Add(command, "@UserId", SqlDbType.Int, userId);
        Add(command, "@PlantId", SqlDbType.Int, plantId);
        Add(command, "@CustomerId", SqlDbType.Int, customerId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<ViewerProgressResponse> GetProgressAsync(
        int userId,
        int plantId,
        int? customerId,
        string process,
        DateTime reportDate,
        CancellationToken cancellationToken)
    {
        var lines = new List<ViewerLineProgress>();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(AccessibleLines + """
            SELECT
                al.CustomerId,
                al.CustomerName,
                al.Process,
                al.LineId,
                al.LineName,
                COALESCE(SUM(dr.ProductionPlan), 0) AS ProductionPlan,
                COALESCE(SUM(dr.ProductionActual), 0) AS ProductionActual,
                COALESCE(SUM(dr.OpenWOQty), 0) AS OpenWOQty,
                COALESCE(SUM(dr.Over7DaysWOBalanceQty), 0) AS Over7DaysWOBalanceQty,
                COUNT(dr.DailyReportId) AS ReportCount,
                SUM(CASE WHEN dr.Status = 'APPROVED' THEN 1 ELSE 0 END) AS ApprovedCount,
                SUM(CASE WHEN dr.Status = 'SUBMITTED' THEN 1 ELSE 0 END) AS SubmittedCount
            FROM AccessibleLines al
            LEFT JOIN DailyReport dr
                ON dr.CustomerId = al.CustomerId
               AND dr.CategoryId = al.CategoryId
               AND dr.LineId = al.LineId
               AND dr.PlantId = @PlantId
               AND dr.ReportDate = @ReportDate
               AND dr.IsActive = 1
               AND dr.ModelId IS NOT NULL
               AND dr.Status <> 'INACTIVE'
            GROUP BY
                al.CustomerId,
                al.CustomerName,
                al.Process,
                al.LineId,
                al.LineName
            ORDER BY al.CustomerName, al.Process, al.LineName;
            """, connection);

        AddScopeParameters(command, userId, plantId, customerId, process);
        Add(command, "@ReportDate", SqlDbType.Date, reportDate.Date);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var plan = reader.GetDecimal(5);
            var actual = reader.GetDecimal(6);
            var reportCount = reader.GetInt32(9);
            var approvedCount = reader.IsDBNull(10) ? 0 : reader.GetInt32(10);
            var submittedCount = reader.IsDBNull(11) ? 0 : reader.GetInt32(11);
            var status = reportCount == 0
                ? "NOT_REPORTED"
                : approvedCount == reportCount
                    ? "APPROVED"
                    : submittedCount + approvedCount == reportCount
                        ? "SUBMITTED"
                        : "IN_PROGRESS";

            lines.Add(new ViewerLineProgress(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                plan,
                actual,
                plan > 0 ? decimal.Round(actual / plan * 100, 1) : 0,
                status,
                reader.GetInt32(7),
                reader.GetInt32(8)));
        }

        var totalPlan = lines.Sum(line => line.ProductionPlan);
        var totalActual = lines.Sum(line => line.ProductionActual);
        var summary = new ViewerSummary(
            totalPlan,
            totalActual,
            totalPlan > 0 ? decimal.Round(totalActual / totalPlan * 100, 1) : 0,
            lines.Count(line => line.Status == "APPROVED"),
            lines.Count);

        return new ViewerProgressResponse(summary, lines);
    }

    public async Task<IReadOnlyList<ViewerTrendPoint>> GetTrendAsync(
        int userId,
        int plantId,
        int? customerId,
        string process,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken)
    {
        var trend = new List<ViewerTrendPoint>();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(AccessibleLines + """
            SELECT
                dr.ReportDate,
                COALESCE(SUM(dr.ProductionPlan), 0) AS ProductionPlan,
                COALESCE(SUM(dr.ProductionActual), 0) AS ProductionActual
            FROM AccessibleLines al
            INNER JOIN DailyReport dr
                ON dr.CustomerId = al.CustomerId
               AND dr.CategoryId = al.CategoryId
               AND dr.LineId = al.LineId
               AND dr.PlantId = @PlantId
               AND dr.ReportDate >= @StartDate
               AND dr.ReportDate <= @EndDate
               AND dr.IsActive = 1
               AND dr.ModelId IS NOT NULL
               AND dr.Status <> 'INACTIVE'
            GROUP BY dr.ReportDate
            ORDER BY dr.ReportDate;
            """, connection);

        AddScopeParameters(command, userId, plantId, customerId, process);
        Add(command, "@StartDate", SqlDbType.Date, startDate.Date);
        Add(command, "@EndDate", SqlDbType.Date, endDate.Date);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            trend.Add(new ViewerTrendPoint(
                reader.GetDateTime(0).ToString("yyyy-MM-dd"),
                reader.GetDecimal(1),
                reader.GetDecimal(2)));
        }

        return trend;
    }

    private static void AddScopeParameters(
        SqlCommand command,
        int userId,
        int plantId,
        int? customerId,
        string process)
    {
        Add(command, "@UserId", SqlDbType.Int, userId);
        Add(command, "@PlantId", SqlDbType.Int, plantId);
        Add(command, "@CustomerId", SqlDbType.Int, customerId);
        Add(command, "@Process", SqlDbType.VarChar, process, 10);
    }

    private static void Add(
        SqlCommand command,
        string name,
        SqlDbType type,
        object? value,
        int? size = null)
    {
        var parameter = size is null
            ? command.Parameters.Add(name, type)
            : command.Parameters.Add(name, type, size.Value);
        parameter.Value = value ?? DBNull.Value;
    }
}

public sealed record ViewerSummary(
    decimal ProductionPlan,
    decimal ProductionActual,
    decimal Achievement,
    int CompletedLines,
    int TotalLines);

public sealed record ViewerLineProgress(
    int CustomerId,
    string CustomerName,
    string Process,
    int LineId,
    string LineName,
    decimal ProductionPlan,
    decimal ProductionActual,
    decimal Achievement,
    string Status,
    int OpenWOQty,
    int Over7DaysWOBalanceQty);

public sealed record ViewerProgressResponse(
    ViewerSummary Summary,
    IReadOnlyList<ViewerLineProgress> Lines);

public sealed record ViewerTrendPoint(
    string Date,
    decimal ProductionPlan,
    decimal ProductionActual);