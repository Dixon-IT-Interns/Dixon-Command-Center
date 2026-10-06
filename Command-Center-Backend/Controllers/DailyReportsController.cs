using System.Data;
using System.Security.Claims;
using Dixon.CommandCenter.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize]
[Route("api/daily-reports")]
public sealed class DailyReportsController(SqlConnectionFactory connectionFactory) : ControllerBase
{
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(
        [FromQuery] int customerId,
        [FromQuery] int categoryId,
        [FromQuery] int lineId,
        [FromQuery] DateTime reportDate,
        [FromQuery] int? modelId,
        CancellationToken cancellationToken)
    {
        if (!await HasCustomerAccessAsync(customerId, cancellationToken)) return Forbid();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        const string contextSql = """
            SELECT c.CustomerId,c.CustomerName,cat.CategoryId,cat.CategoryName,
                   pl.LineId,pl.LineName,pl.SAPLocation
            FROM Customer c
            INNER JOIN Category cat ON cat.CustomerId=c.CustomerId AND cat.IsActive=1
            INNER JOIN ProductionLine pl ON pl.CategoryId=cat.CategoryId AND pl.IsActive=1
            WHERE c.CustomerId=@CustomerId AND cat.CategoryId=@CategoryId AND pl.LineId=@LineId;
            """;
        await using var contextCommand=new SqlCommand(contextSql,connection);
        Add(contextCommand,"@CustomerId",SqlDbType.Int,customerId);Add(contextCommand,"@CategoryId",SqlDbType.Int,categoryId);Add(contextCommand,"@LineId",SqlDbType.Int,lineId);
        await using var contextReader=await contextCommand.ExecuteReaderAsync(cancellationToken);
        if(!await contextReader.ReadAsync(cancellationToken))return NotFound(new{message="Invalid brand, category or production line."});
        var customer=new{customerId=contextReader.GetInt32(0),customerName=contextReader.GetString(1)};
        var category=new{categoryId=contextReader.GetInt32(2),categoryName=contextReader.GetString(3)};
        var line=new{lineId=contextReader.GetInt32(4),lineName=contextReader.GetString(5),sapLocation=contextReader.IsDBNull(6)?null:contextReader.GetString(6)};
        await contextReader.CloseAsync();

        var models=new List<object>();
        const string modelSql="""
            SELECT DISTINCT m.ModelId,m.ModelName
            FROM LineModel lm
            INNER JOIN ProductionLine pl ON pl.LineId=lm.LineId
            INNER JOIN Category cat ON cat.CategoryId=pl.CategoryId
            INNER JOIN Model m ON m.ModelId=lm.ModelId
            WHERE lm.LineId=@LineId AND cat.CategoryId=@CategoryId AND cat.CustomerId=@CustomerId
              AND lm.IsActive=1 AND m.IsActive=1
            ORDER BY m.ModelName;
            """;
        await using var modelCommand=new SqlCommand(modelSql,connection);
        Add(modelCommand,"@CustomerId",SqlDbType.Int,customerId);Add(modelCommand,"@CategoryId",SqlDbType.Int,categoryId);Add(modelCommand,"@LineId",SqlDbType.Int,lineId);
        await using var modelReader=await modelCommand.ExecuteReaderAsync(cancellationToken);
        while(await modelReader.ReadAsync(cancellationToken))models.Add(new{modelId=modelReader.GetInt32(0),modelName=modelReader.GetString(1)});
        await modelReader.CloseAsync();

        return Ok(new{customer,category,line,models,targets=(object?)null});
    }

    [HttpGet("line-status")]
    public async Task<IActionResult> GetLineStatus(
        [FromQuery] int customerId,
        [FromQuery] int categoryId,
        [FromQuery] DateTime reportDate,
        CancellationToken cancellationToken)
    {
        if (!await HasCustomerAccessAsync(customerId, cancellationToken)) return Forbid();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        const string sql = """
            SELECT
                pl.LineId,
                pl.LineName,
                pl.SAPLocation,
                CASE
                    WHEN COALESCE(x.ActiveEntryCount, 0) = 0 AND COALESCE(x.InactiveEntryCount, 0) > 0 THEN 'INACTIVE'
                    WHEN COALESCE(x.ActiveEntryCount, 0) > 0
                         AND COALESCE(x.ApprovedEntryCount, 0) = COALESCE(x.ActiveEntryCount, 0) THEN 'APPROVED'
                    WHEN COALESCE(x.ActiveEntryCount, 0) > 0
                         AND COALESCE(x.SubmittedEntryCount, 0) + COALESCE(x.ApprovedEntryCount, 0) = COALESCE(x.ActiveEntryCount, 0) THEN 'SUBMITTED'
                    WHEN COALESCE(x.ActiveEntryCount, 0) > 0 THEN 'IN_PROGRESS'
                    ELSE 'NOT_STARTED'
                END AS ReportStatus,
                x.LatestSubmittedAt,
                x.LatestDailyReportId,
                COALESCE(x.ActiveEntryCount, 0) AS ActiveEntryCount
            FROM ProductionLine pl
            INNER JOIN Category cat ON cat.CategoryId = pl.CategoryId
            OUTER APPLY (
                SELECT
                    SUM(CASE WHEN dr.IsActive = 1 AND dr.ModelId IS NOT NULL THEN 1 ELSE 0 END) AS ActiveEntryCount,
                    SUM(CASE WHEN dr.IsActive = 1 AND dr.ModelId IS NOT NULL AND dr.Status = 'SUBMITTED' THEN 1 ELSE 0 END) AS SubmittedEntryCount,
                    SUM(CASE WHEN dr.IsActive = 1 AND dr.ModelId IS NOT NULL AND dr.Status = 'APPROVED' THEN 1 ELSE 0 END) AS ApprovedEntryCount,
                    SUM(CASE WHEN dr.IsActive = 0 THEN 1 ELSE 0 END) AS InactiveEntryCount,
                    MAX(CASE WHEN dr.Status IN ('SUBMITTED','APPROVED') AND dr.IsActive = 1 THEN dr.SubmittedAt END) AS LatestSubmittedAt,
                    MAX(dr.DailyReportId) AS LatestDailyReportId
                FROM DailyReport dr
                WHERE dr.LineId = pl.LineId
                  AND dr.CustomerId = cat.CustomerId
                  AND dr.CategoryId = cat.CategoryId
                  AND dr.ReportDate = @ReportDate
            ) x
            WHERE cat.CustomerId = @CustomerId
              AND cat.CategoryId = @CategoryId
              AND pl.IsActive = 1
              AND cat.IsActive = 1
            ORDER BY pl.LineName;
            """;

        await using var command = new SqlCommand(sql, connection);
        Add(command, "@CustomerId", SqlDbType.Int, customerId);
        Add(command, "@CategoryId", SqlDbType.Int, categoryId);
        Add(command, "@ReportDate", SqlDbType.Date, reportDate.Date);

        var lines = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = reader.GetString(3);
            lines.Add(new
            {
                lineId = reader.GetInt32(0),
                lineName = reader.GetString(1),
                sapLocation = reader.IsDBNull(2) ? null : reader.GetString(2),
                status,
                isActive = status != "INACTIVE",
                submittedAt = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4),
                dailyReportId = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                modelCount = reader.GetInt32(6)
            });
        }

        return Ok(lines);
    }

    [HttpPost("line-status")]
    public async Task<IActionResult> SetLineStatus([FromBody] LineStatusRequest request, CancellationToken cancellationToken)
    {
        if (request.ReportDate.Date > DateTime.Today)
            return BadRequest(new { message = "A future reporting date cannot be updated." });

        var userId = GetUserId();
        if (userId <= 0)
            return Unauthorized(new { message = "The current user could not be identified." });
        if (!await HasCustomerAccessAsync(request.CustomerId, cancellationToken)) return Forbid();

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            const string validateSql = """
                SELECT COUNT(1)
                FROM ProductionLine pl
                INNER JOIN Category cat ON cat.CategoryId = pl.CategoryId
                INNER JOIN Customer c ON c.CustomerId = cat.CustomerId
                WHERE c.CustomerId = @CustomerId
                  AND cat.CategoryId = @CategoryId
                  AND pl.LineId = @LineId
                  AND c.IsActive = 1
                  AND cat.IsActive = 1
                  AND pl.IsActive = 1;
                """;
            await using var validate = new SqlCommand(validateSql, connection, transaction);
            Add(validate, "@CustomerId", SqlDbType.Int, request.CustomerId);
            Add(validate, "@CategoryId", SqlDbType.Int, request.CategoryId);
            Add(validate, "@LineId", SqlDbType.Int, request.LineId);
            if (Convert.ToInt32(await validate.ExecuteScalarAsync(cancellationToken)) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return BadRequest(new { message = "Invalid brand, category or production line." });
            }

            if (!request.IsActive)
            {
                // Keep the model rows so the user can reactivate the line later,
                // but clear every daily actual value for this line/date.
                await using var deactivate = new SqlCommand("""
                    UPDATE DailyReport
                    SET IsActive = 0,
                        Status = 'INACTIVE',
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE ReportDate = @ReportDate
                      AND CustomerId = @CustomerId
                      AND CategoryId = @CategoryId
                      AND LineId = @LineId;
                    """, connection, transaction);
                Add(deactivate, "@ReportDate", SqlDbType.Date, request.ReportDate.Date);
                Add(deactivate, "@CustomerId", SqlDbType.Int, request.CustomerId);
                Add(deactivate, "@CategoryId", SqlDbType.Int, request.CategoryId);
                Add(deactivate, "@LineId", SqlDbType.Int, request.LineId);
                await deactivate.ExecuteNonQueryAsync(cancellationToken);

                // If there were no model rows, create a single line-level marker.
                await using var marker = new SqlCommand("""
                    IF NOT EXISTS (
                        SELECT 1 FROM DailyReport
                        WHERE ReportDate = @ReportDate
                          AND CustomerId = @CustomerId
                          AND CategoryId = @CategoryId
                          AND LineId = @LineId
                    )
                    BEGIN
                        INSERT INTO DailyReport
                        (ReportDate, CustomerId, CategoryId, LineId, ModelId, CreatedBy, Status, IsActive, CreatedAt, UpdatedAt)
                        VALUES
                        (@ReportDate, @CustomerId, @CategoryId, @LineId, NULL, @CreatedBy, 'INACTIVE', 0, SYSUTCDATETIME(), SYSUTCDATETIME());
                    END;
                    """, connection, transaction);
                Add(marker, "@ReportDate", SqlDbType.Date, request.ReportDate.Date);
                Add(marker, "@CustomerId", SqlDbType.Int, request.CustomerId);
                Add(marker, "@CategoryId", SqlDbType.Int, request.CategoryId);
                Add(marker, "@LineId", SqlDbType.Int, request.LineId);
                Add(marker, "@CreatedBy", SqlDbType.Int, userId);
                await marker.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                // Reactivating restores existing model rows as editable drafts.
                await using var activate = new SqlCommand("""
                    DELETE FROM DailyReport
                    WHERE ReportDate = @ReportDate
                      AND CustomerId = @CustomerId
                      AND CategoryId = @CategoryId
                      AND LineId = @LineId
                      AND ModelId IS NULL;

                    UPDATE DailyReport
                    SET IsActive = 1,
                        Status = CASE WHEN Status = 'INACTIVE' THEN 'DRAFT' ELSE Status END,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE ReportDate = @ReportDate
                      AND CustomerId = @CustomerId
                      AND CategoryId = @CategoryId
                      AND LineId = @LineId;
                    """, connection, transaction);
                Add(activate, "@ReportDate", SqlDbType.Date, request.ReportDate.Date);
                Add(activate, "@CustomerId", SqlDbType.Int, request.CustomerId);
                Add(activate, "@CategoryId", SqlDbType.Int, request.CategoryId);
                Add(activate, "@LineId", SqlDbType.Int, request.LineId);
                await activate.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return Ok(new { message = request.IsActive ? "Line activated." : "Line marked inactive and daily values cleared." });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [HttpGet("entries")]
    public async Task<IActionResult> GetEntries(
        [FromQuery] int customerId,
        [FromQuery] int categoryId,
        [FromQuery] int lineId,
        [FromQuery] DateTime reportDate,
        CancellationToken cancellationToken)
    {
        if (!await HasCustomerAccessAsync(customerId, cancellationToken)) return Forbid();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        const string sql = """
            SELECT
                dr.DailyReportId, dr.ModelId, m.ModelName, dr.Status, dr.IsActive,
                dr.MonthPlan, dr.MonthActual, dr.ProductionPlan, dr.ProductionActual,
                dr.UPHTarget, dr.UPHActual, dr.UPPHInstalled, dr.UPPHActual,
                dr.FPYTarget, dr.FPYActual, dr.FTYTarget, dr.FTYActual, dr.RTYTarget, dr.RTYActual,
                dr.OSDReportingDateValue, dr.OSDMTDValue, dr.ActualOTManhours, dr.OpenWOQty,
                dr.Over7DaysWOBalanceQty, dr.DailyTRCInQty, dr.DailyTRCOutQty,
                dr.TRCOverallFailureInflowPercent, dr.TRCLyingOver3DaysCr, dr.IssueDescription,
                dr.RejectionReason
            FROM DailyReport dr
            LEFT JOIN Model m ON m.ModelId = dr.ModelId
            WHERE dr.CustomerId = @CustomerId
              AND dr.CategoryId = @CategoryId
              AND dr.LineId = @LineId
              AND dr.ReportDate = @ReportDate
              AND dr.ModelId IS NOT NULL
            ORDER BY m.ModelName;
            """;
        await using var command=new SqlCommand(sql,connection);
        Add(command,"@CustomerId",SqlDbType.Int,customerId);Add(command,"@CategoryId",SqlDbType.Int,categoryId);Add(command,"@LineId",SqlDbType.Int,lineId);Add(command,"@ReportDate",SqlDbType.Date,reportDate.Date);
        var entries=new List<object>();
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new{
                dailyReportId=reader.GetInt32(0),modelId=reader.GetInt32(1),modelName=reader.IsDBNull(2)?null:reader.GetString(2),
                status=reader.GetString(3),isActive=reader.GetBoolean(4),
                monthPlan=GetDecimal(reader,5),monthActual=GetDecimal(reader,6),productionPlan=GetDecimal(reader,7),productionActual=GetDecimal(reader,8),
                uphTarget=GetDecimal(reader,9),uphActual=GetDecimal(reader,10),upphInstalled=GetDecimal(reader,11),upphActual=GetDecimal(reader,12),
                fpyTarget=GetDecimal(reader,13),fpyActual=GetDecimal(reader,14),ftyTarget=GetDecimal(reader,15),ftyActual=GetDecimal(reader,16),
                rtyTarget=GetDecimal(reader,17),rtyActual=GetDecimal(reader,18),osdReportingDateValue=GetDecimal(reader,19),osdMtdValue=GetDecimal(reader,20),
                actualOTManhours=GetDecimal(reader,21),openWOQty=GetInt(reader,22),over7DaysWOBalanceQty=GetInt(reader,23),dailyTRCInQty=GetInt(reader,24),dailyTRCOutQty=GetInt(reader,25),
                trcOverallFailureInflowPercent=GetDecimal(reader,26),trcLyingOver3DaysCr=GetDecimal(reader,27),issueDescription=GetString(reader,28),rejectionReason=GetString(reader,29)
            });
        }
        return Ok(entries);
    }

    [HttpGet("draft")]
    public async Task<IActionResult> GetDraft(
        [FromQuery] int customerId,
        [FromQuery] int categoryId,
        [FromQuery] int lineId,
        [FromQuery] DateTime reportDate,
        CancellationToken cancellationToken)
    {
        if (!await HasCustomerAccessAsync(customerId, cancellationToken)) return Forbid();

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT TOP (1)
                dr.ModelId,
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
                dr.OSDMTDValue,
                dr.ActualOTManhours,
                dr.OpenWOQty,
                dr.Over7DaysWOBalanceQty,
                dr.DailyTRCInQty,
                dr.DailyTRCOutQty,
                dr.TRCOverallFailureInflowPercent,
                dr.TRCLyingOver3DaysCr,
                dr.IssueDescription,
                dr.Status,
                dr.DailyReportId,
                dr.RejectionReason
            FROM DailyReport dr
            WHERE dr.CustomerId = @CustomerId
              AND dr.CategoryId = @CategoryId
              AND dr.LineId = @LineId
              AND dr.ReportDate = @ReportDate
              AND dr.ModelId IS NOT NULL
              AND dr.Status IN ('DRAFT', 'REJECTED')
              AND dr.IsActive = 1
            ORDER BY dr.UpdatedAt DESC, dr.DailyReportId DESC;
            """;

        await using var command = new SqlCommand(sql, connection);
        Add(command, "@CustomerId", SqlDbType.Int, customerId);
        Add(command, "@CategoryId", SqlDbType.Int, categoryId);
        Add(command, "@LineId", SqlDbType.Int, lineId);
        Add(command, "@ReportDate", SqlDbType.Date, reportDate.Date);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return Ok(null);

        return Ok(new
        {
            modelId = GetInt(reader, 0),
            monthPlan = GetDecimal(reader, 1),
            monthActual = GetDecimal(reader, 2),
            productionPlan = GetDecimal(reader, 3),
            productionActual = GetDecimal(reader, 4),
            uphTarget = GetDecimal(reader, 5),
            uphActual = GetDecimal(reader, 6),
            upphInstalled = GetDecimal(reader, 7),
            upphActual = GetDecimal(reader, 8),
            fpyTarget = GetDecimal(reader, 9),
            fpyActual = GetDecimal(reader, 10),
            ftyTarget = GetDecimal(reader, 11),
            ftyActual = GetDecimal(reader, 12),
            rtyTarget = GetDecimal(reader, 13),
            rtyActual = GetDecimal(reader, 14),
            osdReportingDateValue = GetDecimal(reader, 15),
            osdMtdValue = GetDecimal(reader, 16),
            actualOTManhours = GetDecimal(reader, 17),
            openWOQty = GetInt(reader, 18),
            over7DaysWOBalanceQty = GetInt(reader, 19),
            dailyTRCInQty = GetInt(reader, 20),
            dailyTRCOutQty = GetInt(reader, 21),
            trcOverallFailureInflowPercent = GetDecimal(reader, 22),
            trcLyingOver3DaysCr = GetDecimal(reader, 23),
            issueDescription = GetString(reader, 24),
            status = reader.GetString(25),
            dailyReportId = reader.GetInt32(26),
            rejectionReason = GetString(reader, 27)
        });
    }

    [HttpPost("draft")]
    public async Task<IActionResult> SaveDraft([FromBody] DailyReportRequest request, CancellationToken cancellationToken)
        => await Save(request, "DRAFT", cancellationToken);

    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] DailyReportRequest request, CancellationToken cancellationToken)
        => await Save(request, "SUBMITTED", cancellationToken);

    private async Task<IActionResult> Save(DailyReportRequest request, string status, CancellationToken cancellationToken)
    {
        if (request.ReportDate.Date > DateTime.Today)
            return BadRequest(new { message = "A future reporting date cannot be submitted." });

        if (!await ValidateConfiguration(request, cancellationToken))
            return BadRequest(new { message = "Invalid brand, category, line or model configuration." });

        var userId = GetUserId();
        if (userId <= 0)
            return Unauthorized(new { message = "The current user could not be identified." });
        if (!await HasCustomerAccessAsync(request.CustomerId, cancellationToken)) return Forbid();

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var existing = await FindReportAsync(connection, transaction, request, cancellationToken);
            int dailyReportId;

            if (existing.HasValue)
            {
                dailyReportId = existing.Value.Id;
                if (existing.Value.Status is "SUBMITTED" or "APPROVED")
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return status == "SUBMITTED"
                        ? Ok(new { message = existing.Value.Status == "APPROVED" ? "Daily report is already approved." : "Daily report is already submitted.", dailyReportId, status = existing.Value.Status })
                        : Conflict(new { message = "This report is already locked for approval." });
                }

                await using var update = new SqlCommand("""
                    UPDATE DailyReport
                    SET ModelId = @ModelId,
                        IsActive = 1,
                        Status = @Status,
                        SubmittedAt = CASE WHEN @Status = 'SUBMITTED' THEN SYSUTCDATETIME() ELSE NULL END,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE DailyReportId = @DailyReportId;
                    """, connection, transaction);
                Add(update, "@ModelId", SqlDbType.Int, request.ModelId);
                Add(update, "@Status", SqlDbType.VarChar, status);
                Add(update, "@DailyReportId", SqlDbType.Int, dailyReportId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await using var insert = new SqlCommand("""
                    INSERT INTO DailyReport
                    (ReportDate, CustomerId, CategoryId, LineId, ModelId, CreatedBy, Status, IsActive, SubmittedAt, CreatedAt, UpdatedAt)
                    OUTPUT INSERTED.DailyReportId
                    VALUES
                    (@ReportDate, @CustomerId, @CategoryId, @LineId, @ModelId, @CreatedBy, @Status, 1,
                     CASE WHEN @Status = 'SUBMITTED' THEN SYSUTCDATETIME() ELSE NULL END,
                     SYSUTCDATETIME(), SYSUTCDATETIME());
                    """, connection, transaction);
                Add(insert, "@ReportDate", SqlDbType.Date, request.ReportDate.Date);
                Add(insert, "@CustomerId", SqlDbType.Int, request.CustomerId);
                Add(insert, "@CategoryId", SqlDbType.Int, request.CategoryId);
                Add(insert, "@LineId", SqlDbType.Int, request.LineId);
                Add(insert, "@ModelId", SqlDbType.Int, request.ModelId);
                Add(insert, "@CreatedBy", SqlDbType.Int, userId);
                Add(insert, "@Status", SqlDbType.VarChar, status);
                dailyReportId = Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
            }

            // ALL KPI values are entered manually and live in DailyReport while the
            // report is DRAFT/SUBMITTED/REJECTED. KPI_Daily is created only when
            // an approver approves the report.
            await SaveDailyReportValuesAsync(connection, transaction, request, dailyReportId, cancellationToken);

            if (status == "SUBMITTED")
            {
                await UpsertApprovalQueueAsync(connection, transaction, dailyReportId, userId, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            return Ok(new
            {
                message = status == "SUBMITTED" ? "Daily report submitted successfully." : "Daily report draft saved.",
                dailyReportId,
                status
            });
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new { message = "A report already exists for this line, model and date." });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task UpsertApprovalQueueAsync(SqlConnection connection, SqlTransaction transaction, int dailyReportId, int submittedBy, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            IF EXISTS (SELECT 1 FROM ApprovalQueue WHERE DailyReportId = @DailyReportId)
            BEGIN
                UPDATE ApprovalQueue
                SET Status = 'PENDING',
                    SubmittedBy = @SubmittedBy,
                    SubmittedAt = SYSUTCDATETIME(),
                    AssignedTo = NULL,
                    AssignedAt = NULL,
                    ReviewedBy = NULL,
                    ReviewedAt = NULL,
                    Comments = NULL,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE DailyReportId = @DailyReportId;
            END
            ELSE
            BEGIN
                INSERT INTO ApprovalQueue
                (DailyReportId, Status, SubmittedBy, SubmittedAt, CreatedAt, UpdatedAt)
                VALUES
                (@DailyReportId, 'PENDING', @SubmittedBy, SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME());
            END
            """, connection, transaction);
        Add(command, "@DailyReportId", SqlDbType.Int, dailyReportId);
        Add(command, "@SubmittedBy", SqlDbType.Int, submittedBy);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<(int Id, string Status)?> FindReportAsync(SqlConnection connection, SqlTransaction transaction, DailyReportRequest request, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT TOP (1) DailyReportId, Status
            FROM DailyReport WITH (UPDLOCK, HOLDLOCK)
            WHERE ReportDate = @ReportDate
              AND CustomerId = @CustomerId
              AND CategoryId = @CategoryId
              AND LineId = @LineId
              AND ModelId = @ModelId;
            """, connection, transaction);
        Add(command, "@ReportDate", SqlDbType.Date, request.ReportDate.Date);
        Add(command, "@CustomerId", SqlDbType.Int, request.CustomerId);
        Add(command, "@CategoryId", SqlDbType.Int, request.CategoryId);
        Add(command, "@LineId", SqlDbType.Int, request.LineId);
        Add(command, "@ModelId", SqlDbType.Int, request.ModelId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return (reader.GetInt32(0), reader.GetString(1));
    }

    private static async Task SaveDailyReportValuesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DailyReportRequest request,
        int dailyReportId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE DailyReport
            SET ModelId = @ModelId,
                MonthPlan = @MonthPlan,
                MonthActual = @MonthActual,
                ProductionPlan = @ProductionPlan,
                ProductionActual = @ProductionActual,
                UPHTarget = @UPHTarget,
                UPHActual = @UPHActual,
                UPPHInstalled = @UPPHInstalled,
                UPPHActual = @UPPHActual,
                FPYTarget = @FPYTarget,
                FPYActual = @FPYActual,
                FTYTarget = @FTYTarget,
                FTYActual = @FTYActual,
                RTYTarget = @RTYTarget,
                RTYActual = @RTYActual,
                OSDReportingDateValue = @OSDReportingDateValue,
                OSDMTDValue = @OSDMTDValue,
                ActualOTManhours = @ActualOTManhours,
                OpenWOQty = @OpenWOQty,
                Over7DaysWOBalanceQty = @Over7DaysWOBalanceQty,
                DailyTRCInQty = @DailyTRCInQty,
                DailyTRCOutQty = @DailyTRCOutQty,
                TRCOverallFailureInflowPercent = @TRCOverallFailureInflowPercent,
                TRCLyingOver3DaysCr = @TRCLyingOver3DaysCr,
                IssueDescription = @IssueDescription,
                UpdatedAt = SYSUTCDATETIME()
            WHERE DailyReportId = @DailyReportId;
            """;

        await using var command = new SqlCommand(sql, connection, transaction);
        Add(command, "@ModelId", SqlDbType.Int, request.ModelId);
        Add(command, "@MonthPlan", SqlDbType.Decimal, request.MonthPlan);
        Add(command, "@MonthActual", SqlDbType.Decimal, request.MonthActual);
        Add(command, "@ProductionPlan", SqlDbType.Decimal, request.ProductionPlan);
        Add(command, "@ProductionActual", SqlDbType.Decimal, request.ProductionActual);
        Add(command, "@UPHTarget", SqlDbType.Decimal, request.UphTarget);
        Add(command, "@UPHActual", SqlDbType.Decimal, request.UphActual);
        Add(command, "@UPPHInstalled", SqlDbType.Decimal, request.UpphInstalled);
        Add(command, "@UPPHActual", SqlDbType.Decimal, request.UpphActual);
        Add(command, "@FPYTarget", SqlDbType.Decimal, request.FpyTarget);
        Add(command, "@FPYActual", SqlDbType.Decimal, request.FpyActual);
        Add(command, "@FTYTarget", SqlDbType.Decimal, request.FtyTarget);
        Add(command, "@FTYActual", SqlDbType.Decimal, request.FtyActual);
        Add(command, "@RTYTarget", SqlDbType.Decimal, request.RtyTarget);
        Add(command, "@RTYActual", SqlDbType.Decimal, request.RtyActual);
        Add(command, "@OSDReportingDateValue", SqlDbType.Decimal, request.OsdReportingDateValue);
        Add(command, "@OSDMTDValue", SqlDbType.Decimal, request.OsdMtdValue);
        Add(command, "@ActualOTManhours", SqlDbType.Decimal, request.ActualOTManhours);
        Add(command, "@OpenWOQty", SqlDbType.Int, request.OpenWOQty);
        Add(command, "@Over7DaysWOBalanceQty", SqlDbType.Int, request.Over7DaysWOBalanceQty);
        Add(command, "@DailyTRCInQty", SqlDbType.Int, request.DailyTRCInQty);
        Add(command, "@DailyTRCOutQty", SqlDbType.Int, request.DailyTRCOutQty);
        Add(command, "@TRCOverallFailureInflowPercent", SqlDbType.Decimal, request.TRCOverallFailureInflowPercent);
        Add(command, "@TRCLyingOver3DaysCr", SqlDbType.Decimal, request.TRCLyingOver3DaysCr);
        Add(command, "@IssueDescription", SqlDbType.VarChar, request.IssueDescription?.Trim());
        Add(command, "@DailyReportId", SqlDbType.Int, dailyReportId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> ValidateConfiguration(DailyReportRequest request, CancellationToken cancellationToken)
    {
        await using var connection=await connectionFactory.OpenAsync(cancellationToken);
        const string sql="""
            SELECT COUNT(1)
            FROM LineModel lm
            INNER JOIN ProductionLine pl ON pl.LineId=lm.LineId
            INNER JOIN Category cat ON cat.CategoryId=pl.CategoryId
            INNER JOIN Customer c ON c.CustomerId=cat.CustomerId
            INNER JOIN Model m ON m.ModelId=lm.ModelId
            WHERE c.CustomerId=@CustomerId AND cat.CategoryId=@CategoryId AND pl.LineId=@LineId
              AND lm.ModelId=@ModelId AND lm.IsActive=1 AND c.IsActive=1 AND cat.IsActive=1
              AND pl.IsActive=1 AND m.IsActive=1;
            """;
        await using var command=new SqlCommand(sql,connection);
        Add(command,"@CustomerId",SqlDbType.Int,request.CustomerId);Add(command,"@CategoryId",SqlDbType.Int,request.CategoryId);Add(command,"@LineId",SqlDbType.Int,request.LineId);Add(command,"@ModelId",SqlDbType.Int,request.ModelId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken))==1;
    }

    private async Task<bool> HasCustomerAccessAsync(int customerId, CancellationToken cancellationToken)
    {
        if (User.IsInRole("Admin")) return true;
        var userId = GetUserId();
        if (userId <= 0) return false;
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT COUNT(1) FROM UserCustomer WHERE UserId=@UserId AND CustomerId=@CustomerId", connection);
        Add(command,"@UserId",SqlDbType.Int,userId); Add(command,"@CustomerId",SqlDbType.Int,customerId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    private int GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : 0;
    }

    private static void Add(SqlCommand command, string name, SqlDbType type, object? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value ?? DBNull.Value;
        if (type == SqlDbType.Decimal)
        {
            parameter.Precision = 18;
            parameter.Scale = 4;
        }
    }

    private static decimal? GetDecimal(SqlDataReader reader, int index) => reader.IsDBNull(index) ? null : Convert.ToDecimal(reader.GetValue(index));
    private static int? GetInt(SqlDataReader reader, int index) => reader.IsDBNull(index) ? null : Convert.ToInt32(reader.GetValue(index));
    private static string? GetString(SqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
}

public sealed class LineStatusRequest
{
    public DateTime ReportDate { get; set; }
    public int CustomerId { get; set; }
    public int CategoryId { get; set; }
    public int LineId { get; set; }
    public bool IsActive { get; set; }
}

public sealed class DailyReportRequest
{
    public DateTime ReportDate { get; set; }
    public int CustomerId { get; set; }
    public int CategoryId { get; set; }
    public int LineId { get; set; }
    public int? ModelId { get; set; }
    public decimal? MonthPlan { get; set; }
    public decimal? MonthActual { get; set; }
    public decimal? ProductionPlan { get; set; }
    public decimal? ProductionActual { get; set; }
    public decimal? UphTarget { get; set; }
    public decimal? UphActual { get; set; }
    public decimal? UpphInstalled { get; set; }
    public decimal? UpphActual { get; set; }
    public decimal? FpyTarget { get; set; }
    public decimal? FpyActual { get; set; }
    public decimal? FtyTarget { get; set; }
    public decimal? FtyActual { get; set; }
    public decimal? RtyTarget { get; set; }
    public decimal? RtyActual { get; set; }
    public decimal? OsdReportingDateValue { get; set; }
    public decimal? OsdMtdValue { get; set; }
    public decimal? ActualOTManhours { get; set; }
    public int? OpenWOQty { get; set; }
    public int? Over7DaysWOBalanceQty { get; set; }
    public int? DailyTRCInQty { get; set; }
    public int? DailyTRCOutQty { get; set; }
    public decimal? TRCOverallFailureInflowPercent { get; set; }
    public decimal? TRCLyingOver3DaysCr { get; set; }
    public string? IssueDescription { get; set; }
}
