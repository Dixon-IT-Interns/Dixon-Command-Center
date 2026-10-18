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
                pl.LineName,
                pl.SAPLocation
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
                al.SAPLocation,
                dr.ModelId,
                m.ModelName,
                dr.DailyReportId,
                dr.Status,
                dr.MonthPlan,
                dr.MonthActual,
                dr.ProductionPlan,
                dr.ProductionActual,
                dr.UPHTarget,
                dr.UPHActual,
                dr.UPPHInstalled,
                dr.UPPHActual,
                dr.FPYTarget,
                dr.FPYActual,
                dr.FTYTarget,
                dr.FTYActual,
                dr.RTYTarget,
                dr.RTYActual,
                dr.OSDReportingDateValue,
                dr.OSDReportingDatePercent,
                dr.OSDMTDValue,
                dr.OSDMTDPercent,
                dr.ActualOTManhours,
                dr.PlannedOTManhours,
                dr.UnplannedOTManhours,
                dr.OpenWOQty,
                dr.Over7DaysWOBalanceQty,
                dr.DailyTRCInQty,
                dr.DailyTRCOutQty,
                dr.TRCOverallFailureInflowPercent,
                dr.TRCLyingOver3DaysCr,
                dr.IssueDescription
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
            LEFT JOIN Model m ON m.ModelId = dr.ModelId
            ORDER BY
                CASE al.Process WHEN 'FATP' THEN 0 WHEN 'SMT' THEN 1 ELSE 2 END,
                al.CustomerName,
                al.LineName,
                m.ModelName;
            """, connection);

        AddScopeParameters(command, userId, plantId, customerId, process);
        Add(command, "@ReportDate", SqlDbType.Date, reportDate.Date);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new ViewerLineProgress(
                CustomerId: reader.GetInt32(0),
                CustomerName: reader.GetString(1),
                Process: reader.GetString(2),
                LineId: reader.GetInt32(3),
                LineName: reader.GetString(4),
                SAPLocation: GetString(reader, 5),
                ModelId: GetInt(reader, 6),
                ModelName: GetString(reader, 7),
                DailyReportId: GetInt(reader, 8),
                Status: GetInt(reader, 8) is null ? "NOT_REPORTED" : reader.GetString(9),
                MonthPlan: GetDecimal(reader, 10),
                MonthActual: GetDecimal(reader, 11),
                ProductionPlan: GetDecimal(reader, 12),
                ProductionActual: GetDecimal(reader, 13),
                UPHTarget: GetDecimal(reader, 14),
                UPHActual: GetDecimal(reader, 15),
                UPPHInstalled: GetDecimal(reader, 16),
                UPPHActual: GetDecimal(reader, 17),
                FPYTarget: GetDecimal(reader, 18),
                FPYActual: GetDecimal(reader, 19),
                FTYTarget: GetDecimal(reader, 20),
                FTYActual: GetDecimal(reader, 21),
                RTYTarget: GetDecimal(reader, 22),
                RTYActual: GetDecimal(reader, 23),
                OSDReportingDateValue: GetDecimal(reader, 24),
                OSDReportingDatePercent: GetDecimal(reader, 25),
                OSDMTDValue: GetDecimal(reader, 26),
                OSDMTDPercent: GetDecimal(reader, 27),
                ActualOTManhours: GetDecimal(reader, 28),
                PlannedOTManhours: GetDecimal(reader, 29),
                UnplannedOTManhours: GetDecimal(reader, 30),
                OpenWOQty: GetInt(reader, 31),
                Over7DaysWOBalanceQty: GetInt(reader, 32),
                DailyTRCInQty: GetInt(reader, 33),
                DailyTRCOutQty: GetInt(reader, 34),
                TRCOverallFailureInflowPercent: GetDecimal(reader, 35),
                TRCLyingOver3DaysCr: GetDecimal(reader, 36),
                IssueDescription: GetString(reader, 37)));
        }

        var totalPlan = lines.Sum(line => line.ProductionPlan ?? 0);
        var totalActual = lines.Sum(line => line.ProductionActual ?? 0);
        var summary = new ViewerSummary(
            totalPlan,
            totalActual,
            totalPlan > 0 ? decimal.Round(totalActual / totalPlan * 100, 1) : 0,
            lines.GroupBy(line => line.LineId)
                .Count(group => group.All(line => line.Status == "APPROVED")),
            lines.Select(line => line.LineId).Distinct().Count());

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

    private static decimal? GetDecimal(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private static int? GetInt(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static string? GetString(SqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
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
    string? SAPLocation,
    int? ModelId,
    string? ModelName,
    int? DailyReportId,
    string Status,
    decimal? MonthPlan,
    decimal? MonthActual,
    decimal? ProductionPlan,
    decimal? ProductionActual,
    decimal? UPHTarget,
    decimal? UPHActual,
    decimal? UPPHInstalled,
    decimal? UPPHActual,
    decimal? FPYTarget,
    decimal? FPYActual,
    decimal? FTYTarget,
    decimal? FTYActual,
    decimal? RTYTarget,
    decimal? RTYActual,
    decimal? OSDReportingDateValue,
    decimal? OSDReportingDatePercent,
    decimal? OSDMTDValue,
    decimal? OSDMTDPercent,
    decimal? ActualOTManhours,
    decimal? PlannedOTManhours,
    decimal? UnplannedOTManhours,
    int? OpenWOQty,
    int? Over7DaysWOBalanceQty,
    int? DailyTRCInQty,
    int? DailyTRCOutQty,
    decimal? TRCOverallFailureInflowPercent,
    decimal? TRCLyingOver3DaysCr,
    string? IssueDescription)
{
    public decimal Achievement =>
        ProductionPlan is decimal plan && plan > 0
            ? decimal.Round(ProductionActual.GetValueOrDefault() / plan * 100, 1)
            : 0;
}

public sealed record ViewerProgressResponse(
    ViewerSummary Summary,
    IReadOnlyList<ViewerLineProgress> Lines);

public sealed record ViewerTrendPoint(
    string Date,
    decimal ProductionPlan,
    decimal ProductionActual);