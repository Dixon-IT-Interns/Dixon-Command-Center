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
            "Customer", "Process", "Line", "Status", "Plan", "Actual",
            "Achievement (%)", "Open WO", "7+ Days WO"
        };

        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

        for (var row = 0; row < progress.Lines.Count; row++)
        {
            var line = progress.Lines[row];
            var excelRow = row + 2;
            sheet.Cell(excelRow, 1).Value = line.CustomerName;
            sheet.Cell(excelRow, 2).Value = line.Process;
            sheet.Cell(excelRow, 3).Value = line.LineName;
            sheet.Cell(excelRow, 4).Value = line.Status;
            sheet.Cell(excelRow, 5).Value = line.ProductionPlan;
            sheet.Cell(excelRow, 6).Value = line.ProductionActual;
            sheet.Cell(excelRow, 7).Value = line.Achievement;
            sheet.Cell(excelRow, 8).Value = line.OpenWOQty;
            sheet.Cell(excelRow, 9).Value = line.Over7DaysWOBalanceQty;
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
}
