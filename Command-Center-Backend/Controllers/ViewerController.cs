using System.Security.Claims;
using ClosedXML.Excel;
using Dixon.CommandCenter.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize(Roles = "Viewer")]
[Route("api/viewer")]
public sealed class ViewerController(ViewerRepository viewer) : ControllerBase
{
    [HttpGet("progress")]
    public async Task<IActionResult> GetProgress(
        [FromQuery] DateTime? reportDate,
        [FromQuery] int? customerId,
        [FromQuery] string process = "ALL",
        CancellationToken cancellationToken = default)
    {
        if (reportDate is null)
        {
            return BadRequest(new { message = "A reporting date is required." });
        }

        if (!TryGetScope(out var userId, out var plantId))
        {
            return Unauthorized();
        }

        if (!TryNormalizeProcess(process, out process))
        {
            return BadRequest(new { message = "Process must be ALL, FATP or SMT." });
        }

        if (customerId is not null
            && !await viewer.HasCustomerAccessAsync(userId, plantId, customerId.Value, cancellationToken))
        {
            return Forbid();
        }

        return Ok(await viewer.GetProgressAsync(
            userId, plantId, customerId, process, reportDate.Value, cancellationToken));
    }

    [HttpGet("trend")]
    public async Task<IActionResult> GetTrend(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int? customerId,
        [FromQuery] string process = "ALL",
        CancellationToken cancellationToken = default)
    {
        if (startDate is null || endDate is null || endDate.Value.Date < startDate.Value.Date)
        {
            return BadRequest(new { message = "A valid start and end date range is required." });
        }

        if ((endDate.Value.Date - startDate.Value.Date).TotalDays > 365)
        {
            return BadRequest(new { message = "The trend range cannot exceed 366 days." });
        }

        if (!TryGetScope(out var userId, out var plantId))
        {
            return Unauthorized();
        }

        if (!TryNormalizeProcess(process, out process))
        {
            return BadRequest(new { message = "Process must be ALL, FATP or SMT." });
        }

        if (customerId is not null
            && !await viewer.HasCustomerAccessAsync(userId, plantId, customerId.Value, cancellationToken))
        {
            return Forbid();
        }

        return Ok(await viewer.GetTrendAsync(
            userId, plantId, customerId, process, startDate.Value, endDate.Value, cancellationToken));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] DateTime? reportDate,
        [FromQuery] int? customerId,
        [FromQuery] string process = "ALL",
        CancellationToken cancellationToken = default)
    {
        if (reportDate is null)
        {
            return BadRequest(new { message = "A reporting date is required." });
        }

        if (!TryGetScope(out var userId, out var plantId))
        {
            return Unauthorized();
        }

        if (!TryNormalizeProcess(process, out process))
        {
            return BadRequest(new { message = "Process must be ALL, FATP or SMT." });
        }

        if (customerId is not null
            && !await viewer.HasCustomerAccessAsync(userId, plantId, customerId.Value, cancellationToken))
        {
            return Forbid();
        }

        var progress = await viewer.GetProgressAsync(
            userId, plantId, customerId, process, reportDate.Value, cancellationToken);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Line Work Progress");
        var headers = new[]
        {
            "Customer", "Process", "Line No.", "SAP Location", "Model", "Status",
            "Month Plan", "Month Actual", "Production Plan", "Production Actual",
            "UPH Target", "UPH Actual", "UPPH Installed", "UPPH Actual",
            "FPY Target", "FPY Actual", "FTY Target", "FTY Actual", "RTY Target", "RTY Actual",
            "OS&D Reporting Date Value", "OS&D Reporting Date %",
            "OS&D MTD Value", "OS&D MTD %",
            "Actual OT", "Planned OT", "Unplanned OT", "Open WO Qty", ">7 Days WO",
            "Daily TRC In", "Daily TRC Out", "TRC Overall Failure %",
            "TRC Lying >3 Days", "Issue Description"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        for (var row = 0; row < progress.Lines.Count; row++)
        {
            var line = progress.Lines[row];
            var excelRow = row + 2;
            var values = new object?[]
            {
                line.CustomerName, line.Process, line.LineName, line.SAPLocation, line.ModelName, line.Status,
                line.MonthPlan, line.MonthActual, line.ProductionPlan, line.ProductionActual,
                line.UPHTarget, line.UPHActual, line.UPPHInstalled, line.UPPHActual,
                line.FPYTarget, line.FPYActual, line.FTYTarget, line.FTYActual, line.RTYTarget, line.RTYActual,
                line.OSDReportingDateValue, line.OSDReportingDatePercent,
                line.OSDMTDValue, line.OSDMTDPercent,
                line.ActualOTManhours, line.PlannedOTManhours, line.UnplannedOTManhours,
                line.OpenWOQty, line.Over7DaysWOBalanceQty,
                line.DailyTRCInQty, line.DailyTRCOutQty, line.TRCOverallFailureInflowPercent,
                line.TRCLyingOver3DaysCr, line.IssueDescription
            };

            for (var column = 0; column < values.Length; column++)
            {
                SetCellValue(sheet.Cell(excelRow, column + 1), values[column]);
            }
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Dixon_Viewer_{reportDate.Value:yyyyMMdd}.xlsx");
    }

    private bool TryGetScope(out int userId, out int plantId)
    {
        userId = 0;
        plantId = 0;
        return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId)
            && userId > 0
            && int.TryParse(User.FindFirstValue("plantId"), out plantId)
            && plantId > 0;
    }

    private static bool TryNormalizeProcess(string? value, out string process)
    {
        process = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return process is "ALL" or "FATP" or "SMT";
    }

    private static void SetCellValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                cell.Clear(XLClearOptions.Contents);
                break;
            case string text:
                cell.Value = text;
                break;
            case int number:
                cell.Value = number;
                break;
            case decimal number:
                cell.Value = number;
                break;
            default:
                throw new InvalidOperationException($"Unsupported Excel cell value type: {value.GetType().Name}.");
        }
    }
}