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
public sealed class ApprovalController(SqlConnectionFactory connectionFactory) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var userId=GetUserId(); if(userId<=0)return Unauthorized();
        await using var connection=await connectionFactory.OpenAsync(cancellationToken);
        const string sql="""
            SELECT aq.Status,COUNT(1) AS ItemCount
            FROM ApprovalQueue aq
            INNER JOIN DailyReport dr ON dr.DailyReportId=aq.DailyReportId
            INNER JOIN UserCustomer uc ON uc.CustomerId=dr.CustomerId AND uc.UserId=@UserId
            GROUP BY aq.Status;
            """;
        await using var cmd=new SqlCommand(sql,connection);Add(cmd,"@UserId",SqlDbType.Int,userId);
        var counts=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);await using var r=await cmd.ExecuteReaderAsync(cancellationToken);while(await r.ReadAsync(cancellationToken))counts[r.GetString(0)]=r.GetInt32(1);
        return Ok(new { pending=counts.GetValueOrDefault("PENDING"), approved=counts.GetValueOrDefault("APPROVED"), rejected=counts.GetValueOrDefault("REJECTED"), reviewed=counts.GetValueOrDefault("APPROVED")+counts.GetValueOrDefault("REJECTED") });
    }

    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        const string sql = """
            SELECT aq.ApprovalQueueId, aq.DailyReportId, aq.Status, aq.SubmittedAt,
                   c.CustomerName, cat.CategoryName, pl.LineName,
                   m.ModelName, u.FullName AS SubmittedByName
            FROM ApprovalQueue aq
            INNER JOIN DailyReport dr ON dr.DailyReportId = aq.DailyReportId
            INNER JOIN Customer c ON c.CustomerId = dr.CustomerId
            INNER JOIN Category cat ON cat.CategoryId = dr.CategoryId
            INNER JOIN ProductionLine pl ON pl.LineId = dr.LineId
            LEFT JOIN Model m ON m.ModelId = dr.ModelId
            INNER JOIN [User] u ON u.UserId = aq.SubmittedBy
            INNER JOIN UserCustomer uc ON uc.CustomerId = dr.CustomerId AND uc.UserId = @UserId
            WHERE aq.Status = 'PENDING'
              AND dr.Status = 'SUBMITTED'
            ORDER BY aq.SubmittedAt ASC;
            """;
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@UserId", SqlDbType.Int, userId);
        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                approvalQueueId = reader.GetInt32(0), dailyReportId = reader.GetInt32(1),
                status = reader.GetString(2), submittedAt = reader.GetDateTime(3),
                customerName = reader.GetString(4), categoryName = reader.GetString(5),
                lineName = reader.GetString(6), modelName = reader.IsDBNull(7) ? null : reader.GetString(7),
                submittedBy = reader.GetString(8)
            });
        }
        return Ok(rows);
    }

    [HttpGet("history")]
    public async Task<IActionResult> History(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        const string sql = """
            SELECT TOP (100) aq.ApprovalQueueId, aq.DailyReportId, aq.Status, aq.SubmittedAt,
                   aq.ReviewedAt, aq.Comments, c.CustomerName, cat.CategoryName, pl.LineName,
                   m.ModelName, reviewer.FullName
            FROM ApprovalQueue aq
            INNER JOIN DailyReport dr ON dr.DailyReportId = aq.DailyReportId
            INNER JOIN Customer c ON c.CustomerId = dr.CustomerId
            INNER JOIN Category cat ON cat.CategoryId = dr.CategoryId
            INNER JOIN ProductionLine pl ON pl.LineId = dr.LineId
            LEFT JOIN Model m ON m.ModelId = dr.ModelId
            INNER JOIN UserCustomer uc ON uc.CustomerId = dr.CustomerId AND uc.UserId = @UserId
            LEFT JOIN [User] reviewer ON reviewer.UserId = aq.ReviewedBy
            WHERE aq.Status <> 'PENDING'
            ORDER BY COALESCE(aq.ReviewedAt, aq.SubmittedAt) DESC;
            """;
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@UserId", SqlDbType.Int, userId);
        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                approvalQueueId = reader.GetInt32(0), dailyReportId = reader.GetInt32(1), status = reader.GetString(2),
                submittedAt = reader.GetDateTime(3), reviewedAt = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4),
                comments = reader.IsDBNull(5) ? null : reader.GetString(5), customerName = reader.GetString(6),
                categoryName = reader.GetString(7), lineName = reader.GetString(8), modelName = reader.IsDBNull(9) ? null : reader.GetString(9),
                reviewer = reader.IsDBNull(10) ? null : reader.GetString(10)
            });
        }
        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken cancellationToken)
    {
        var userId = GetUserId(); if (userId <= 0) return Unauthorized();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        const string sql = """
            SELECT aq.ApprovalQueueId,aq.DailyReportId,aq.Status,aq.SubmittedAt,aq.Comments,
                   c.CustomerName,cat.CategoryName,pl.LineName,pl.SAPLocation,m.ModelName,
                   dr.ReportDate,creator.FullName,
                   kd.MonthPlan,kd.MonthActual,kd.ProductionPlan,kd.ProductionActual,kd.UPHActual,kd.UPPHInstalled,kd.UPPHActual,
                   kd.FPYActual,kd.FTYActual,kd.RTYActual,kd.OSDReportingDateValue,kd.OSDMTDValue,
                   kd.OSDReportingDatePercent,kd.OSDMTDPercent,kd.PlannedOTManhours,kd.UnplannedOTManhours,kd.ActualOTManhours,kd.OpenWOQty,kd.Over7DaysWOBalanceQty,kd.DailyTRCInQty,kd.DailyTRCOutQty,kd.TRCOverallFailureInflowPercent,kd.TRCLyingOver3DaysCr,kd.IssueDescription,
                   km.ProductionPlan AS MasterProductionPlan,km.UPHTarget,km.UPPHTarget,km.CPHTarget,km.FPYTarget,km.FTYTarget,km.RTYTarget,km.OSDTarget,km.OTTarget
            FROM ApprovalQueue aq
            INNER JOIN DailyReport dr ON dr.DailyReportId=aq.DailyReportId
            INNER JOIN Customer c ON c.CustomerId=dr.CustomerId
            INNER JOIN Category cat ON cat.CategoryId=dr.CategoryId
            INNER JOIN ProductionLine pl ON pl.LineId=dr.LineId
            LEFT JOIN Model m ON m.ModelId=dr.ModelId
            INNER JOIN [User] creator ON creator.UserId=dr.CreatedBy
            INNER JOIN UserCustomer uc ON uc.CustomerId=dr.CustomerId AND uc.UserId=@UserId
            LEFT JOIN KPI_Daily kd ON kd.DailyReportId=dr.DailyReportId
            LEFT JOIN KPI_Master km ON km.CustomerId=dr.CustomerId AND km.CategoryId=dr.CategoryId AND km.LineId=dr.LineId AND km.ModelId=dr.ModelId AND km.KPIYEAR=YEAR(dr.ReportDate) AND km.KPIMonth=MONTH(dr.ReportDate) AND km.IsActive=1
            WHERE aq.ApprovalQueueId=@Id;
            """;
        await using var command=new SqlCommand(sql,connection);Add(command,"@Id",SqlDbType.Int,id);Add(command,"@UserId",SqlDbType.Int,userId);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        if(!await reader.ReadAsync(cancellationToken)) return NotFound(new{message="Approval item not found or not assigned to you."});
        return Ok(new {
            approvalQueueId=reader.GetInt32(0),dailyReportId=reader.GetInt32(1),status=reader.GetString(2),submittedAt=reader.GetDateTime(3),comments=reader.IsDBNull(4)?null:reader.GetString(4),
            customerName=reader.GetString(5),categoryName=reader.GetString(6),lineName=reader.GetString(7),sapLocation=reader.IsDBNull(8)?null:reader.GetString(8),modelName=reader.IsDBNull(9)?null:reader.GetString(9),reportDate=reader.GetDateTime(10).ToString("yyyy-MM-dd"),submittedBy=reader.GetString(11),
            metrics=new {
                monthPlan=GetDecimal(reader,12),monthActual=GetDecimal(reader,13),productionPlan=GetDecimal(reader,14),productionActual=GetDecimal(reader,15),uphActual=GetDecimal(reader,16),upphInstalled=GetDecimal(reader,17),upphActual=GetDecimal(reader,18),fpyActual=GetDecimal(reader,19),ftyActual=GetDecimal(reader,20),rtyActual=GetDecimal(reader,21),osdReportingDateValue=GetDecimal(reader,22),osdMtdValue=GetDecimal(reader,23),osdReportingDatePercent=GetDecimal(reader,24),osdMtdPercent=GetDecimal(reader,25),plannedOTManhours=GetDecimal(reader,26),unplannedOTManhours=GetDecimal(reader,27),actualOTManhours=GetDecimal(reader,28),openWOQty=GetInt(reader,29),over7DaysWOBalanceQty=GetInt(reader,30),dailyTRCInQty=GetInt(reader,31),dailyTRCOutQty=GetInt(reader,32),trcOverallFailureInflowPercent=GetDecimal(reader,33),trcLyingOver3DaysCr=GetDecimal(reader,34),issueDescription=GetString(reader,35),
                plan=new { productionPlan=GetDecimal(reader,36),uphTarget=GetDecimal(reader,37),upphTarget=GetDecimal(reader,38),cphTarget=GetDecimal(reader,39),fpyTarget=GetDecimal(reader,40),ftyTarget=GetDecimal(reader,41),rtyTarget=GetDecimal(reader,42),osdTarget=GetDecimal(reader,43),otTarget=GetDecimal(reader,44) }
            }
        });
    }

    [HttpPost("{id:int}/approve")]
    public Task<IActionResult> Approve(int id, CancellationToken cancellationToken) => Review(id, true, null, cancellationToken);

    [HttpPost("{id:int}/reject")]
    public Task<IActionResult> Reject(int id, [FromBody] ReviewRequest request, CancellationToken cancellationToken) => Review(id, false, request.Comment, cancellationToken);

    private async Task<IActionResult> Review(int id, bool approve, string? comment, CancellationToken cancellationToken)
    {
        if (!approve && string.IsNullOrWhiteSpace(comment)) return BadRequest(new { message = "A rejection comment is required." });
        var userId = GetUserId(); if (userId <= 0) return Unauthorized();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            const string check = """
                SELECT aq.DailyReportId, aq.Status, dr.CustomerId
                FROM ApprovalQueue aq
                INNER JOIN DailyReport dr ON dr.DailyReportId = aq.DailyReportId
                INNER JOIN UserCustomer uc ON uc.CustomerId = dr.CustomerId AND uc.UserId = @UserId
                WHERE aq.ApprovalQueueId = @Id;
                """;
            await using var cmd = new SqlCommand(check, connection, tx);
            Add(cmd,"@Id",SqlDbType.Int,id); Add(cmd,"@UserId",SqlDbType.Int,userId);
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken)) { await tx.RollbackAsync(cancellationToken); return NotFound(new { message="Approval item not found or not assigned to you." }); }
            var dailyReportId = r.GetInt32(0); var status = r.GetString(1); await r.CloseAsync();
            if (status != "PENDING") { await tx.RollbackAsync(cancellationToken); return Conflict(new { message="This approval item has already been reviewed." }); }

            var queueStatus = approve ? "APPROVED" : "REJECTED";
            await using var updateQueue = new SqlCommand("""
                UPDATE ApprovalQueue SET Status=@Status, ReviewedBy=@UserId, ReviewedAt=SYSUTCDATETIME(), Comments=@Comments, UpdatedAt=SYSUTCDATETIME() WHERE ApprovalQueueId=@Id;
                UPDATE DailyReport SET Status=@ReportStatus, ApprovedBy=CASE WHEN @Approve=1 THEN @UserId ELSE NULL END, ApprovedAt=CASE WHEN @Approve=1 THEN SYSUTCDATETIME() ELSE NULL END, RejectionReason=CASE WHEN @Approve=1 THEN NULL ELSE @Comments END, UpdatedAt=SYSUTCDATETIME() WHERE DailyReportId=@DailyReportId;
                INSERT INTO ReportApproval(DailyReportId, ActionBy, ActionType, Comments, ActionAt) VALUES(@DailyReportId,@UserId,@ActionType,@Comments,SYSUTCDATETIME());
                """, connection, tx);
            Add(updateQueue,"@Status",SqlDbType.VarChar,queueStatus); Add(updateQueue,"@UserId",SqlDbType.Int,userId); Add(updateQueue,"@Comments",SqlDbType.VarChar,string.IsNullOrWhiteSpace(comment)?null:comment.Trim()); Add(updateQueue,"@Id",SqlDbType.Int,id); Add(updateQueue,"@Approve",SqlDbType.Bit,approve); Add(updateQueue,"@ReportStatus",SqlDbType.VarChar,approve?"APPROVED":"REJECTED"); Add(updateQueue,"@DailyReportId",SqlDbType.Int,dailyReportId); Add(updateQueue,"@ActionType",SqlDbType.VarChar,approve?"APPROVED":"REJECTED");
            await updateQueue.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Ok(new { message = approve ? "Report approved and locked." : "Report rejected and returned to contributor.", status=queueStatus });
        }
        catch { await tx.RollbackAsync(cancellationToken); throw; }
    }

    private int GetUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    private static void Add(SqlCommand c,string n,SqlDbType t,object? v){var p=c.Parameters.Add(n,t);p.Value=v??DBNull.Value;}
    private static decimal? GetDecimal(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToDecimal(r.GetValue(i));
    private static int? GetInt(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToInt32(r.GetValue(i));
    private static string? GetString(SqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
}

public sealed record ReviewRequest(string? Comment);
