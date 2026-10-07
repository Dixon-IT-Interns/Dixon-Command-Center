using System.Data;
using System.Security.Claims;
using Dixon.CommandCenter.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize(Roles = "Approver")]
[Route("api/approvals")]
public sealed class ApprovalController(
    SqlConnectionFactory connectionFactory,
    ILogger<ApprovalController> logger,
    IWebHostEnvironment environment) : ControllerBase
{
    // =========================================================
    // SUMMARY
    // =========================================================

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId <= 0)
            return Unauthorized();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                aq.Status,
                COUNT(1) AS ItemCount
            FROM ApprovalQueue aq
            INNER JOIN DailyReport dr
                ON dr.DailyReportId = aq.DailyReportId
            INNER JOIN UserCustomer uc
                ON uc.CustomerId = dr.CustomerId
               AND uc.UserId = @UserId
            GROUP BY aq.Status;
            """;

        await using var command = new SqlCommand(sql, connection);

        Add(command, "@UserId", SqlDbType.Int, userId);

        var counts = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return Ok(new
        {
            pending = counts.GetValueOrDefault("PENDING"),
            approved = counts.GetValueOrDefault("APPROVED"),
            rejected = counts.GetValueOrDefault("REJECTED"),
            reviewed =
                counts.GetValueOrDefault("APPROVED") +
                counts.GetValueOrDefault("REJECTED")
        });
    }


    // =========================================================
    // PENDING APPROVALS
    // =========================================================

    [HttpGet("pending")]
    public async Task<IActionResult> Pending(
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId <= 0)
            return Unauthorized();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                aq.ApprovalQueueId,
                aq.DailyReportId,
                aq.Status,
                aq.SubmittedAt,
                c.CustomerName,
                cat.CategoryName,
                pl.LineName,
                m.ModelName,
                u.FullName AS SubmittedByName
            FROM ApprovalQueue aq
            INNER JOIN DailyReport dr
                ON dr.DailyReportId = aq.DailyReportId
            INNER JOIN Customer c
                ON c.CustomerId = dr.CustomerId
            INNER JOIN Category cat
                ON cat.CategoryId = dr.CategoryId
            INNER JOIN ProductionLine pl
                ON pl.LineId = dr.LineId
            LEFT JOIN Model m
                ON m.ModelId = dr.ModelId
            INNER JOIN [User] u
                ON u.UserId = aq.SubmittedBy
            INNER JOIN UserCustomer uc
                ON uc.CustomerId = dr.CustomerId
               AND uc.UserId = @UserId
            WHERE aq.Status = 'PENDING'
              AND dr.Status = 'SUBMITTED'
            ORDER BY aq.SubmittedAt ASC;
            """;

        await using var command = new SqlCommand(sql, connection);

        Add(command, "@UserId", SqlDbType.Int, userId);

        var rows = new List<object>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                approvalQueueId = reader.GetInt32(0),
                dailyReportId = reader.GetInt32(1),
                status = reader.GetString(2),
                submittedAt = reader.GetDateTime(3),

                customerName = reader.GetString(4),
                categoryName = reader.GetString(5),
                lineName = reader.GetString(6),

                modelName =
                    reader.IsDBNull(7)
                        ? null
                        : reader.GetString(7),

                submittedBy = reader.GetString(8)
            });
        }

        return Ok(rows);
    }


    // =========================================================
    // APPROVAL HISTORY
    // =========================================================

    [HttpGet("history")]
    public async Task<IActionResult> History(
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId <= 0)
            return Unauthorized();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT TOP (100)
                aq.ApprovalQueueId,
                aq.DailyReportId,
                aq.Status,
                aq.SubmittedAt,
                aq.ReviewedAt,
                aq.Comments,
                c.CustomerName,
                cat.CategoryName,
                pl.LineName,
                m.ModelName,
                reviewer.FullName
            FROM ApprovalQueue aq
            INNER JOIN DailyReport dr
                ON dr.DailyReportId = aq.DailyReportId
            INNER JOIN Customer c
                ON c.CustomerId = dr.CustomerId
            INNER JOIN Category cat
                ON cat.CategoryId = dr.CategoryId
            INNER JOIN ProductionLine pl
                ON pl.LineId = dr.LineId
            LEFT JOIN Model m
                ON m.ModelId = dr.ModelId
            INNER JOIN UserCustomer uc
                ON uc.CustomerId = dr.CustomerId
               AND uc.UserId = @UserId
            LEFT JOIN [User] reviewer
                ON reviewer.UserId = aq.ReviewedBy
            WHERE aq.Status <> 'PENDING'
            ORDER BY COALESCE(
                aq.ReviewedAt,
                aq.SubmittedAt
            ) DESC;
            """;

        await using var command = new SqlCommand(sql, connection);

        Add(command, "@UserId", SqlDbType.Int, userId);

        var rows = new List<object>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                approvalQueueId = reader.GetInt32(0),
                dailyReportId = reader.GetInt32(1),
                status = reader.GetString(2),

                submittedAt = reader.GetDateTime(3),

                reviewedAt =
                    reader.IsDBNull(4)
                        ? (DateTime?)null
                        : reader.GetDateTime(4),

                comments =
                    reader.IsDBNull(5)
                        ? null
                        : reader.GetString(5),

                customerName = reader.GetString(6),
                categoryName = reader.GetString(7),
                lineName = reader.GetString(8),

                modelName =
                    reader.IsDBNull(9)
                        ? null
                        : reader.GetString(9),

                reviewer =
                    reader.IsDBNull(10)
                        ? null
                        : reader.GetString(10)
            });
        }

        return Ok(rows);
    }


    // =========================================================
    // APPROVAL DETAIL
    // =========================================================

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(
        int id,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId <= 0)
            return Unauthorized();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        /*
         * IMPORTANT:
         *
         * Approval Detail is now completely based on DailyReport.
         *
         * No KPI_Master.
         * No monthly default/target lookup.
         *
         * Contributor enters all values manually every day.
         */

        const string sql = """
            SELECT
                aq.ApprovalQueueId,
                aq.DailyReportId,
                aq.Status,
                aq.SubmittedAt,
                aq.Comments,

                c.CustomerName,
                cat.CategoryName,
                pl.LineName,
                pl.SAPLocation,
                m.ModelName,

                dr.ReportDate,
                creator.FullName,

                dr.MonthPlan,
                dr.MonthActual,

                dr.ProductionPlan,
                dr.ProductionActual,

                dr.UPHTarget,
                dr.UPHActual,

                dr.UPPHInstalled,
                dr.UPPHActual,

                dr.CPHTarget,
                dr.CPHActual,

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

                dr.PlannedOTManhours,
                dr.UnplannedOTManhours,
                dr.ActualOTManhours,

                dr.OpenWOQty,
                dr.Over7DaysWOBalanceQty,

                dr.DailyTRCInQty,
                dr.DailyTRCOutQty,

                dr.TRCOverallFailureInflowPercent,
                dr.TRCLyingOver3DaysCr,

                dr.IssueDescription

            FROM ApprovalQueue aq

            INNER JOIN DailyReport dr
                ON dr.DailyReportId = aq.DailyReportId

            INNER JOIN Customer c
                ON c.CustomerId = dr.CustomerId

            INNER JOIN Category cat
                ON cat.CategoryId = dr.CategoryId

            INNER JOIN ProductionLine pl
                ON pl.LineId = dr.LineId

            LEFT JOIN Model m
                ON m.ModelId = dr.ModelId

            INNER JOIN [User] creator
                ON creator.UserId = dr.CreatedBy

            INNER JOIN UserCustomer uc
                ON uc.CustomerId = dr.CustomerId
               AND uc.UserId = @UserId

            WHERE aq.ApprovalQueueId = @Id;
            """;

        await using var command =
            new SqlCommand(sql, connection);

        Add(command, "@Id", SqlDbType.Int, id);
        Add(command, "@UserId", SqlDbType.Int, userId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return NotFound(new
            {
                message =
                    "Approval item not found or not assigned to you."
            });
        }

        return Ok(new
        {
            approvalQueueId = reader.GetInt32(0),
            dailyReportId = reader.GetInt32(1),
            status = reader.GetString(2),
            submittedAt = reader.GetDateTime(3),

            comments =
                reader.IsDBNull(4)
                    ? null
                    : reader.GetString(4),

            customerName = reader.GetString(5),
            categoryName = reader.GetString(6),
            lineName = reader.GetString(7),

            sapLocation =
                reader.IsDBNull(8)
                    ? null
                    : reader.GetString(8),

            modelName =
                reader.IsDBNull(9)
                    ? null
                    : reader.GetString(9),

            reportDate =
                reader.GetDateTime(10)
                    .ToString("yyyy-MM-dd"),

            submittedBy = reader.GetString(11),

            metrics = new
            {
                monthPlan = GetDecimal(reader, 12),
                monthActual = GetDecimal(reader, 13),

                productionPlan = GetDecimal(reader, 14),
                productionActual = GetDecimal(reader, 15),

                uphTarget = GetDecimal(reader, 16),
                uphActual = GetDecimal(reader, 17),

                upphInstalled = GetDecimal(reader, 18),
                upphActual = GetDecimal(reader, 19),

                cphTarget = GetDecimal(reader, 20),
                cphActual = GetDecimal(reader, 21),

                fpyTarget = GetDecimal(reader, 22),
                fpyActual = GetDecimal(reader, 23),

                ftyTarget = GetDecimal(reader, 24),
                ftyActual = GetDecimal(reader, 25),

                rtyTarget = GetDecimal(reader, 26),
                rtyActual = GetDecimal(reader, 27),

                osdReportingDateValue =
                    GetDecimal(reader, 28),

                osdReportingDatePercent =
                    GetDecimal(reader, 29),

                osdMtdValue =
                    GetDecimal(reader, 30),

                osdMtdPercent =
                    GetDecimal(reader, 31),

                plannedOTManhours =
                    GetDecimal(reader, 32),

                unplannedOTManhours =
                    GetDecimal(reader, 33),

                actualOTManhours =
                    GetDecimal(reader, 34),

                openWOQty =
                    GetInt(reader, 35),

                over7DaysWOBalanceQty =
                    GetInt(reader, 36),

                dailyTRCInQty =
                    GetInt(reader, 37),

                dailyTRCOutQty =
                    GetInt(reader, 38),

                trcOverallFailureInflowPercent =
                    GetDecimal(reader, 39),

                trcLyingOver3DaysCr =
                    GetDecimal(reader, 40),

                issueDescription =
                    GetString(reader, 41)
            }
        });
    }


    // =========================================================
    // APPROVE
    // =========================================================

    [HttpPost("{id:int}/approve")]
    public Task<IActionResult> Approve(
        int id,
        CancellationToken cancellationToken)
    {
        return Review(
            id,
            approve: true,
            comment: null,
            cancellationToken);
    }


    // =========================================================
    // REJECT
    // =========================================================

    [HttpPost("{id:int}/reject")]
    public Task<IActionResult> Reject(
        int id,
        [FromBody] ReviewRequest request,
        CancellationToken cancellationToken)
    {
        return Review(
            id,
            approve: false,
            comment: request.Comment,
            cancellationToken);
    }


    // =========================================================
    // REVIEW
    // =========================================================

    private async Task<IActionResult> Review(
        int id,
        bool approve,
        string? comment,
        CancellationToken cancellationToken)
    {
        if (!approve && string.IsNullOrWhiteSpace(comment))
        {
            return BadRequest(new
            {
                message = "A rejection comment is required."
            });
        }

        var userId = GetUserId();

        if (userId <= 0)
            return Unauthorized();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var tx =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        int? dailyReportIdForLog = null;

        try
        {
            // -------------------------------------------------
            // Get approval + DailyReport
            // -------------------------------------------------

            const string checkSql = """
                SELECT
                    aq.DailyReportId,
                    aq.Status,
                    dr.CustomerId
                FROM ApprovalQueue aq

                INNER JOIN DailyReport dr
                    ON dr.DailyReportId = aq.DailyReportId

                INNER JOIN UserCustomer uc
                    ON uc.CustomerId = dr.CustomerId
                   AND uc.UserId = @UserId

                WHERE aq.ApprovalQueueId = @Id;
                """;

            await using var checkCommand =
                new SqlCommand(
                    checkSql,
                    connection,
                    tx);

            Add(
                checkCommand,
                "@Id",
                SqlDbType.Int,
                id);

            Add(
                checkCommand,
                "@UserId",
                SqlDbType.Int,
                userId);

            await using var reader =
                await checkCommand.ExecuteReaderAsync(
                    cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);

                return NotFound(new
                {
                    message =
                        "Approval item not found or not assigned to you."
                });
            }

            var dailyReportId = reader.GetInt32(0);
            dailyReportIdForLog = dailyReportId;
            var status = reader.GetString(1);

            await reader.CloseAsync();

            if (!string.Equals(
                    status,
                    "PENDING",
                    StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync(cancellationToken);

                return Conflict(new
                {
                    message =
                        "This approval item has already been reviewed."
                });
            }


            // -------------------------------------------------
            // REJECT
            // -------------------------------------------------

            if (!approve)
            {
                const string rejectSql = """
                    UPDATE ApprovalQueue
                    SET
                        Status = 'REJECTED',
                        ReviewedBy = @UserId,
                        ReviewedAt = SYSUTCDATETIME(),
                        Comments = @Comments,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE ApprovalQueueId = @Id;

                    UPDATE DailyReport
                    SET
                        Status = 'REJECTED',
                        ApprovedBy = NULL,
                        ApprovedAt = NULL,
                        RejectionReason = @Comments,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE DailyReportId = @DailyReportId;

                    INSERT INTO ReportApproval
                    (
                        DailyReportId,
                        ActionBy,
                        ActionType,
                        Comments,
                        ActionAt
                    )
                    VALUES
                    (
                        @DailyReportId,
                        @UserId,
                        'REJECTED',
                        @Comments,
                        SYSUTCDATETIME()
                    );
                    """;

                await using var rejectCommand =
                    new SqlCommand(
                        rejectSql,
                        connection,
                        tx);

                Add(
                    rejectCommand,
                    "@Status",
                    SqlDbType.VarChar,
                    "REJECTED");

                Add(
                    rejectCommand,
                    "@UserId",
                    SqlDbType.Int,
                    userId);

                Add(
                    rejectCommand,
                    "@Comments",
                    SqlDbType.VarChar,
                    comment?.Trim());

                Add(
                    rejectCommand,
                    "@Id",
                    SqlDbType.Int,
                    id);

                Add(
                    rejectCommand,
                    "@DailyReportId",
                    SqlDbType.Int,
                    dailyReportId);

                await rejectCommand.ExecuteNonQueryAsync(
                    cancellationToken);

                await tx.CommitAsync(cancellationToken);

                return Ok(new
                {
                    message =
                        "Report rejected and returned to contributor.",

                    status = "REJECTED"
                });
            }


            // -------------------------------------------------
            // APPROVE
            // -------------------------------------------------
            //
            // First create/update final KPI_Daily snapshot.
            // Then mark DailyReport + ApprovalQueue approved.
            //

            await CreateFinalKpiSnapshotAsync(
                connection,
                tx,
                dailyReportId,
                cancellationToken);


            // -------------------------------------------------
            // Mark approval as APPROVED
            // -------------------------------------------------

            const string approveSql = """
                UPDATE ApprovalQueue
                SET
                    Status = 'APPROVED',
                    ReviewedBy = @UserId,
                    ReviewedAt = SYSUTCDATETIME(),
                    Comments = NULL,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE ApprovalQueueId = @Id;

                UPDATE DailyReport
                SET
                    Status = 'APPROVED',
                    ApprovedBy = @UserId,
                    ApprovedAt = SYSUTCDATETIME(),
                    RejectionReason = NULL,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE DailyReportId = @DailyReportId;

                INSERT INTO ReportApproval
                (
                    DailyReportId,
                    ActionBy,
                    ActionType,
                    Comments,
                    ActionAt
                )
                VALUES
                (
                    @DailyReportId,
                    @UserId,
                    'APPROVED',
                    NULL,
                    SYSUTCDATETIME()
                );
                """;

            await using var approveCommand =
                new SqlCommand(
                    approveSql,
                    connection,
                    tx);

            Add(
                approveCommand,
                "@UserId",
                SqlDbType.Int,
                userId);

            Add(
                approveCommand,
                "@Id",
                SqlDbType.Int,
                id);

            Add(
                approveCommand,
                "@DailyReportId",
                SqlDbType.Int,
                dailyReportId);

            await approveCommand.ExecuteNonQueryAsync(
                cancellationToken);


            await tx.CommitAsync(cancellationToken);

            return Ok(new
            {
                message =
                    "Report approved and final KPI snapshot created.",

                status = "APPROVED"
            });
        }
        catch (SqlException ex)
        {
            await tx.RollbackAsync(cancellationToken);

            if (environment.IsDevelopment())
            {
                logger.LogError(
                    ex,
                    "Approval review failed. ApprovalQueueId={ApprovalQueueId}, DailyReportId={DailyReportId}, Approve={Approve}, UserId={UserId}, SqlNumber={SqlNumber}, SqlState={SqlState}, SqlProcedure={SqlProcedure}, SqlLineNumber={SqlLineNumber}, SqlMessage={SqlMessage}",
                    id,
                    dailyReportIdForLog,
                    approve,
                    userId,
                    ex.Number,
                    ex.State,
                    ex.Procedure,
                    ex.LineNumber,
                    ex.Message);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        message = "Approval review failed.",
                        sqlError = new
                        {
                            ex.Message,
                            ex.Number,
                            ex.State,
                            ex.Procedure,
                            ex.LineNumber
                        }
                    });
            }

            throw;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }


    // =========================================================
    // CREATE FINAL KPI SNAPSHOT
    // =========================================================

    private static async Task CreateFinalKpiSnapshotAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int dailyReportId,
        CancellationToken cancellationToken)
    {
        /*
         * KPI_Daily is the FINAL approved snapshot.
         *
         * DailyReport = editable/submitted source
         * KPI_Daily    = approved/final data
         *
         * KPI_Master is intentionally NOT used.
         */

        const string sql = """
            IF EXISTS
            (
                SELECT 1
                FROM KPI_Daily
                WHERE DailyReportId = @DailyReportId
            )
            BEGIN
                UPDATE kd
                SET
                    ModelId = dr.ModelId,
                    ReportDate = dr.ReportDate,

                    MonthPlan = dr.MonthPlan,
                    MonthActual = dr.MonthActual,

                    ProductionPlan = dr.ProductionPlan,
                    ProductionActual = dr.ProductionActual,

                    UPHTarget = dr.UPHTarget,
                    UPHActual = dr.UPHActual,

                    UPPHInstalled = dr.UPPHInstalled,
                    UPPHActual = dr.UPPHActual,
                    CPHTarget = dr.CPHTarget,
                    CPHActual = dr.CPHActual,

                    FPYTarget = dr.FPYTarget,
                    FPYActual = dr.FPYActual,

                    FTYTarget = dr.FTYTarget,
                    FTYActual = dr.FTYActual,

                    RTYTarget = dr.RTYTarget,
                    RTYActual = dr.RTYActual,

                    OSDReportingDateValue = dr.OSDReportingDateValue,
                    OSDReportingDatePercent = dr.OSDReportingDatePercent,

                    OSDMTDValue = dr.OSDMTDValue,
                    OSDMTDPercent = dr.OSDMTDPercent,

                    PlannedOTManhours = dr.PlannedOTManhours,
                    UnplannedOTManhours = dr.UnplannedOTManhours,
                    ActualOTManhours = dr.ActualOTManhours,

                    OpenWOQty = dr.OpenWOQty,
                    Over7DaysWOBalanceQty = dr.Over7DaysWOBalanceQty,

                    DailyTRCInQty = dr.DailyTRCInQty,
                    DailyTRCOutQty = dr.DailyTRCOutQty,

                    TRCOverallFailureInflowPercent = dr.TRCOverallFailureInflowPercent,
                    TRCLyingOver3DaysCr = dr.TRCLyingOver3DaysCr,

                    IssueDescription = dr.IssueDescription,
                    UpdatedAt = SYSUTCDATETIME()

                FROM KPI_Daily kd
                INNER JOIN DailyReport dr
                    ON dr.DailyReportId = kd.DailyReportId
                WHERE kd.DailyReportId = @DailyReportId;
            END
            ELSE
            BEGIN
                INSERT INTO KPI_Daily
                (
                    DailyReportId,
                    ModelId,
                    ReportDate,

                    MonthPlan,
                    MonthActual,

                    ProductionPlan,
                    ProductionActual,

                    UPHTarget,
                    UPHActual,

                    UPPHInstalled,
                    UPPHActual,

                    CPHTarget,
                    CPHActual,

                    FPYTarget,
                    FPYActual,

                    FTYTarget,
                    FTYActual,

                    RTYTarget,
                    RTYActual,

                    OSDReportingDateValue,
                    OSDReportingDatePercent,

                    OSDMTDValue,
                    OSDMTDPercent,

                    PlannedOTManhours,
                    UnplannedOTManhours,
                    ActualOTManhours,

                    OpenWOQty,
                    Over7DaysWOBalanceQty,

                    DailyTRCInQty,
                    DailyTRCOutQty,

                    TRCOverallFailureInflowPercent,
                    TRCLyingOver3DaysCr,

                    IssueDescription,
                    CreatedAt,
                    UpdatedAt
                )
                SELECT
                    dr.DailyReportId,
                    dr.ModelId,
                    dr.ReportDate,

                    dr.MonthPlan,
                    dr.MonthActual,

                    dr.ProductionPlan,
                    dr.ProductionActual,

                    dr.UPHTarget,
                    dr.UPHActual,

                    dr.UPPHInstalled,
                    dr.UPPHActual,

                    dr.CPHTarget,
                    dr.CPHActual,

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

                    dr.PlannedOTManhours,
                    dr.UnplannedOTManhours,
                    dr.ActualOTManhours,

                    dr.OpenWOQty,
                    dr.Over7DaysWOBalanceQty,

                    dr.DailyTRCInQty,
                    dr.DailyTRCOutQty,

                    dr.TRCOverallFailureInflowPercent,
                    dr.TRCLyingOver3DaysCr,

                    dr.IssueDescription,
                    SYSUTCDATETIME(),
                    SYSUTCDATETIME()

                FROM DailyReport dr
                WHERE dr.DailyReportId = @DailyReportId;
            END
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        Add(
            command,
            "@DailyReportId",
            SqlDbType.Int,
            dailyReportId);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private int GetUserId()
    {
        return int.TryParse(
            User.FindFirstValue(
                ClaimTypes.NameIdentifier),
            out var id)
            ? id
            : 0;
    }


    private static void Add(
        SqlCommand command,
        string name,
        SqlDbType type,
        object? value)
    {
        var parameter =
            command.Parameters.Add(name, type);

        parameter.Value =
            value ?? DBNull.Value;
    }


    private static decimal? GetDecimal(
        SqlDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : Convert.ToDecimal(
                reader.GetValue(index));
    }


    private static int? GetInt(
        SqlDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : Convert.ToInt32(
                reader.GetValue(index));
    }


    private static string? GetString(
        SqlDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : reader.GetString(index);
    }
}


// =============================================================
// REQUEST MODEL
// =============================================================

public sealed record ReviewRequest(string? Comment);