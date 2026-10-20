using System.Data;
using System.Security.Claims;
using Dixon.CommandCenter.API.Data;
using Dixon.CommandCenter.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize]
[Route("api/daily-reports")]
public sealed class DailyReportsController(
    SqlConnectionFactory connectionFactory,
    DailyReportExcelImportService excelImportService) : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, (string Column, int Scale)> EditableKpiPlanFields =
        new Dictionary<string, (string Column, int Scale)>(StringComparer.OrdinalIgnoreCase)
        {
            ["productionPlan"] = ("ProductionPlan", 2),
            ["uphTarget"] = ("UPHTarget", 2),
            ["upphTarget"] = ("UPPHTarget", 2),
            ["cphTarget"] = ("CPHTarget", 2),
            ["fpyTarget"] = ("FPYTarget", 4),
            ["ftyTarget"] = ("FTYTarget", 4),
            ["rtyTarget"] = ("RTYTarget", 4),
            ["osdTarget"] = ("OSDTarget", 2),
            ["otTarget"] = ("OTTarget", 2)
        };

    // ============================================================
    // CONFIG
    // ============================================================

    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(
        [FromQuery] int customerId,
        [FromQuery] int categoryId,
        [FromQuery] int lineId,
        [FromQuery] DateTime reportDate,
        [FromQuery] int? modelId,
        CancellationToken cancellationToken)
    {
        if (!await HasCustomerAccessAsync(customerId, cancellationToken))
            return Forbid();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        const string contextSql = """
            SELECT
                c.CustomerId,
                c.CustomerName,
                cat.CategoryId,
                cat.CategoryName,
                pl.LineId,
                pl.LineName,
                pl.SAPLocation
            FROM Customer c
            INNER JOIN Category cat
                ON cat.CustomerId = c.CustomerId
               AND cat.PlantId = @PlantId
               AND cat.IsActive = 1
            INNER JOIN ProductionLine pl
                ON pl.CategoryId = cat.CategoryId
               AND pl.PlantId = cat.PlantId
               AND pl.IsActive = 1
            WHERE c.CustomerId = @CustomerId
              AND cat.CategoryId = @CategoryId
              AND pl.LineId = @LineId;
            """;

        await using var contextCommand =
            new SqlCommand(contextSql, connection);

        Add(contextCommand, "@CustomerId", SqlDbType.Int, customerId);
        Add(contextCommand, "@CategoryId", SqlDbType.Int, categoryId);
        Add(contextCommand, "@LineId", SqlDbType.Int, lineId);
        Add(contextCommand, "@PlantId", SqlDbType.Int, GetPlantId());

        await using var contextReader =
            await contextCommand.ExecuteReaderAsync(cancellationToken);

        if (!await contextReader.ReadAsync(cancellationToken))
        {
            return NotFound(new
            {
                message = "Invalid brand, category or production line."
            });
        }

        var customer = new
        {
            customerId = contextReader.GetInt32(0),
            customerName = contextReader.GetString(1)
        };

        var category = new
        {
            categoryId = contextReader.GetInt32(2),
            categoryName = contextReader.GetString(3)
        };

        var line = new
        {
            lineId = contextReader.GetInt32(4),
            lineName = contextReader.GetString(5),
            sapLocation = contextReader.IsDBNull(6)
                ? null
                : contextReader.GetString(6)
        };

        await contextReader.CloseAsync();

        var models = new List<object>();

        // Models belong to a customer, not to a production line.
        // Every active model for the selected customer is available on each
        // active line inside the selected customer/category context.
        const string modelSql = """
            SELECT
                m.ModelId,
                m.ModelName
            FROM Model m
            WHERE m.CustomerId = @CustomerId
              AND m.IsActive = 1
              AND EXISTS
              (
                  SELECT 1
                  FROM Category cat
                  WHERE cat.CategoryId = @CategoryId
                    AND cat.CustomerId = m.CustomerId
                    AND cat.PlantId = @PlantId
                    AND cat.IsActive = 1
              )
            ORDER BY m.ModelName;
            """;

        await using var modelCommand =
            new SqlCommand(modelSql, connection);

        Add(modelCommand, "@CustomerId", SqlDbType.Int, customerId);
        Add(modelCommand, "@CategoryId", SqlDbType.Int, categoryId);
        Add(modelCommand, "@PlantId", SqlDbType.Int, GetPlantId());

        await using var modelReader =
            await modelCommand.ExecuteReaderAsync(cancellationToken);

        while (await modelReader.ReadAsync(cancellationToken))
        {
            models.Add(new
            {
                modelId = modelReader.GetInt32(0),
                modelName = modelReader.GetString(1)
            });
        }

        await modelReader.CloseAsync();

        return Ok(new
        {
            customer,
            category,
            line,
            models,

            // Monthly/default KPI master is intentionally not used.
            targets = (object?)null
        });
    }

    [HttpGet("kpi-plan")]
    public async Task<IActionResult> GetKpiPlan(
        [FromQuery] int customerId,
        [FromQuery] int categoryId,
        [FromQuery] int lineId,
        [FromQuery] int modelId,
        [FromQuery] DateTime reportDate,
        CancellationToken cancellationToken)
    {
        if (reportDate == default)
        {
            return BadRequest(new { message = "A reporting date is required." });
        }

        if (!await HasCustomerAccessAsync(customerId, cancellationToken))
            return Forbid();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                km.KPI_MasterId,
                km.ProductionPlan,
                km.UPHTarget,
                km.UPPHTarget,
                km.CPHTarget,
                km.FPYTarget,
                km.FTYTarget,
                km.RTYTarget,
                km.OSDTarget,
                km.OTTarget,
                km.UpdatedAt
            FROM Customer c
            INNER JOIN Category cat
                ON cat.CustomerId = c.CustomerId
               AND cat.PlantId = @PlantId
               AND cat.IsActive = 1
            INNER JOIN ProductionLine pl
                ON pl.CategoryId = cat.CategoryId
               AND pl.PlantId = cat.PlantId
               AND pl.IsActive = 1
            INNER JOIN Model m
                ON m.CustomerId = c.CustomerId
               AND m.IsActive = 1
            LEFT JOIN KPI_Master km
                ON km.PlantId = cat.PlantId
               AND km.CustomerId = c.CustomerId
               AND km.CategoryId = cat.CategoryId
               AND km.LineId = pl.LineId
               AND km.ModelId = m.ModelId
               AND km.KPIYEAR = @Year
               AND km.KPIMonth = @Month
               AND km.IsActive = 1
            WHERE c.CustomerId = @CustomerId
              AND cat.CategoryId = @CategoryId
              AND pl.LineId = @LineId
              AND m.ModelId = @ModelId
              AND c.IsActive = 1;
            """;

        await using var command = new SqlCommand(sql, connection);
        Add(command, "@PlantId", SqlDbType.Int, GetPlantId());
        Add(command, "@CustomerId", SqlDbType.Int, customerId);
        Add(command, "@CategoryId", SqlDbType.Int, categoryId);
        Add(command, "@LineId", SqlDbType.Int, lineId);
        Add(command, "@ModelId", SqlDbType.Int, modelId);
        Add(command, "@Year", SqlDbType.Int, reportDate.Year);
        Add(command, "@Month", SqlDbType.Int, reportDate.Month);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return NotFound(new { message = "Invalid brand, category, line or model." });

        if (reader.IsDBNull(0))
            return Ok(new { plan = (object?)null });

        var plan = new
        {
            planId = reader.GetInt32(0),
            productionPlan = GetDecimal(reader, 1),
            uphTarget = GetDecimal(reader, 2),
            upphTarget = GetDecimal(reader, 3),
            cphTarget = GetDecimal(reader, 4),
            fpyTarget = GetDecimal(reader, 5),
            ftyTarget = GetDecimal(reader, 6),
            rtyTarget = GetDecimal(reader, 7),
            osdTarget = GetDecimal(reader, 8),
            otTarget = GetDecimal(reader, 9),
            updatedAt = reader.IsDBNull(10) ? (DateTime?)null : reader.GetDateTime(10)
        };

        if (await reader.ReadAsync(cancellationToken))
            return Conflict(new { message = "More than one monthly KPI plan matches this reporting context." });

        return Ok(new { plan });
    }


    // ============================================================
    // EXCEL IMPORT PREVIEW
    // ============================================================

    [HttpPost("import/preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> PreviewExcelImport(
        [FromForm] DailyReportExcelImportRequest request,
        CancellationToken cancellationToken)
    {
        if (request.File is null || request.File.Length == 0)
        {
            return BadRequest(new
            {
                valid = false,
                message = "Excel import failed: Select an .xlsx file to upload."
            });
        }

        if (!string.Equals(
                Path.GetExtension(request.File.FileName),
                ".xlsx",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                valid = false,
                message = "Excel import failed: Only .xlsx files are supported."
            });
        }

        if (request.CustomerId <= 0
            || request.CategoryId <= 0
            || request.ReportDate == default)
        {
            return BadRequest(new
            {
                valid = false,
                message = "Excel import failed: Brand, category and report date are required."
            });
        }

        if (request.ReportDate.Date > DateTime.Today)
        {
            return BadRequest(new
            {
                valid = false,
                message = "Excel import failed: A future reporting date is not valid."
            });
        }

        if (!await HasCustomerAccessAsync(
                request.CustomerId,
                cancellationToken))
        {
            return Forbid();
        }

        await using var stream = request.File.OpenReadStream();
        var preview = await excelImportService.PreviewAsync(
            stream,
            GetPlantId(),
            request.CustomerId,
            request.CategoryId,
            request.ReportDate,
            cancellationToken);

        return preview.Valid
            ? Ok(preview)
            : BadRequest(new
            {
                valid = false,
                reportDate = preview.ReportDate,
                lines = preview.Lines,
                models = preview.Models,
                errors = preview.Errors,
                message = $"Excel import failed: {string.Join(" ", preview.Errors)}"
            });
    }


    // ============================================================
    // LINE STATUS - GET
    // ============================================================

    [HttpGet("line-status")]
    public async Task<IActionResult> GetLineStatus(
        [FromQuery] int customerId,
        [FromQuery] int categoryId,
        [FromQuery] DateTime reportDate,
        CancellationToken cancellationToken)
    {
        if (!await HasCustomerAccessAsync(customerId, cancellationToken))
            return Forbid();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                pl.LineId,
                pl.LineName,
                pl.SAPLocation,

                CASE
                    WHEN COALESCE(x.ActiveEntryCount, 0) = 0
                         AND COALESCE(x.InactiveEntryCount, 0) > 0
                        THEN 'INACTIVE'

                    WHEN COALESCE(x.ActiveEntryCount, 0) > 0
                         AND COALESCE(x.ApprovedEntryCount, 0)
                             = COALESCE(x.ActiveEntryCount, 0)
                        THEN 'APPROVED'

                    WHEN COALESCE(x.ActiveEntryCount, 0) > 0
                         AND COALESCE(x.SubmittedEntryCount, 0)
                             + COALESCE(x.ApprovedEntryCount, 0)
                             = COALESCE(x.ActiveEntryCount, 0)
                        THEN 'SUBMITTED'

                    WHEN COALESCE(x.ActiveEntryCount, 0) > 0
                        THEN 'IN_PROGRESS'

                    ELSE 'NOT_STARTED'
                END AS ReportStatus,

                x.LatestSubmittedAt,
                x.LatestDailyReportId,

                COALESCE(x.ActiveEntryCount, 0) AS ActiveEntryCount

            FROM ProductionLine pl

            INNER JOIN Category cat
                ON cat.CategoryId = pl.CategoryId

            OUTER APPLY
            (
                SELECT
                    SUM(
                        CASE
                            WHEN dr.IsActive = 1
                                 AND dr.ModelId IS NOT NULL
                            THEN 1
                            ELSE 0
                        END
                    ) AS ActiveEntryCount,

                    SUM(
                        CASE
                            WHEN dr.IsActive = 1
                                 AND dr.ModelId IS NOT NULL
                                 AND dr.Status = 'SUBMITTED'
                            THEN 1
                            ELSE 0
                        END
                    ) AS SubmittedEntryCount,

                    SUM(
                        CASE
                            WHEN dr.IsActive = 1
                                 AND dr.ModelId IS NOT NULL
                                 AND dr.Status = 'APPROVED'
                            THEN 1
                            ELSE 0
                        END
                    ) AS ApprovedEntryCount,

                    SUM(
                        CASE
                            WHEN dr.IsActive = 0
                            THEN 1
                            ELSE 0
                        END
                    ) AS InactiveEntryCount,

                    MAX(
                        CASE
                            WHEN dr.Status IN ('SUBMITTED', 'APPROVED')
                                 AND dr.IsActive = 1
                            THEN dr.SubmittedAt
                        END
                    ) AS LatestSubmittedAt,

                    MAX(dr.DailyReportId) AS LatestDailyReportId

                FROM DailyReport dr

                WHERE dr.LineId = pl.LineId
                  AND dr.PlantId = pl.PlantId
                  AND dr.CustomerId = cat.CustomerId
                  AND dr.CategoryId = cat.CategoryId
                  AND dr.ReportDate = @ReportDate
            ) x

            WHERE cat.CustomerId = @CustomerId
              AND cat.CategoryId = @CategoryId
              AND cat.PlantId = @PlantId
              AND pl.PlantId = cat.PlantId
              AND pl.IsActive = 1
              AND cat.IsActive = 1

            ORDER BY pl.LineName;
            """;

        await using var command =
            new SqlCommand(sql, connection);

        Add(command, "@CustomerId", SqlDbType.Int, customerId);
        Add(command, "@CategoryId", SqlDbType.Int, categoryId);
        Add(command, "@ReportDate", SqlDbType.Date, reportDate.Date);
        Add(command, "@PlantId", SqlDbType.Int, GetPlantId());

        var lines = new List<object>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var status = reader.GetString(3);

            lines.Add(new
            {
                lineId = reader.GetInt32(0),
                lineName = reader.GetString(1),

                sapLocation = reader.IsDBNull(2)
                    ? null
                    : reader.GetString(2),

                status,

                isActive = status != "INACTIVE",

                submittedAt =
                    reader.IsDBNull(4)
                        ? (DateTime?)null
                        : reader.GetDateTime(4),

                dailyReportId =
                    reader.IsDBNull(5)
                        ? (int?)null
                        : reader.GetInt32(5),

                modelCount = reader.GetInt32(6)
            });
        }

        return Ok(lines);
    }


    // ============================================================
    // LINE STATUS - UPDATE
    // ============================================================

    [HttpPost("line-status")]
    public async Task<IActionResult> SetLineStatus(
        [FromBody] LineStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ReportDate.Date > DateTime.Today)
        {
            return BadRequest(new
            {
                message = "A future reporting date cannot be updated."
            });
        }

        var userId = GetUserId();

        if (userId <= 0)
        {
            return Unauthorized(new
            {
                message = "The current user could not be identified."
            });
        }

        if (!await HasCustomerAccessAsync(
                request.CustomerId,
                cancellationToken))
        {
            return Forbid();
        }

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            const string validateSql = """
                SELECT COUNT(1)
                FROM ProductionLine pl
                INNER JOIN Category cat
                    ON cat.CategoryId = pl.CategoryId
                INNER JOIN Customer c
                    ON c.CustomerId = cat.CustomerId
                WHERE c.CustomerId = @CustomerId
                  AND cat.CategoryId = @CategoryId
                  AND cat.PlantId = @PlantId
                  AND pl.LineId = @LineId
                  AND pl.PlantId = cat.PlantId
                  AND c.IsActive = 1
                  AND cat.IsActive = 1
                  AND pl.IsActive = 1;
                """;

            await using var validate =
                new SqlCommand(
                    validateSql,
                    connection,
                    transaction);

            Add(
                validate,
                "@CustomerId",
                SqlDbType.Int,
                request.CustomerId);

            Add(
                validate,
                "@CategoryId",
                SqlDbType.Int,
                request.CategoryId);

            Add(
                validate,
                "@LineId",
                SqlDbType.Int,
                request.LineId);
            Add(validate, "@PlantId", SqlDbType.Int, GetPlantId());

            if (Convert.ToInt32(
                    await validate.ExecuteScalarAsync(
                        cancellationToken)) != 1)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return BadRequest(new
                {
                    message =
                        "Invalid brand, category or production line."
                });
            }

            if (!request.IsActive)
            {
                await using var deactivate =
                    new SqlCommand(
                        """
                        UPDATE DailyReport
                        SET
                            IsActive = 0,
                            Status = 'INACTIVE',
                            UpdatedAt = SYSUTCDATETIME()
                        WHERE ReportDate = @ReportDate
                          AND PlantId = @PlantId
                          AND CustomerId = @CustomerId
                          AND CategoryId = @CategoryId
                          AND LineId = @LineId;
                        """,
                        connection,
                        transaction);

                Add(
                    deactivate,
                    "@ReportDate",
                    SqlDbType.Date,
                    request.ReportDate.Date);
                Add(deactivate, "@PlantId", SqlDbType.Int, GetPlantId());

                Add(
                    deactivate,
                    "@CustomerId",
                    SqlDbType.Int,
                    request.CustomerId);

                Add(
                    deactivate,
                    "@CategoryId",
                    SqlDbType.Int,
                    request.CategoryId);

                Add(
                    deactivate,
                    "@LineId",
                    SqlDbType.Int,
                    request.LineId);

                await deactivate.ExecuteNonQueryAsync(
                    cancellationToken);

                await using var marker =
                    new SqlCommand(
                        """
                        IF NOT EXISTS
                        (
                            SELECT 1
                            FROM DailyReport
                            WHERE PlantId = @PlantId
                              AND ReportDate = @ReportDate
                              AND CustomerId = @CustomerId
                              AND CategoryId = @CategoryId
                              AND LineId = @LineId
                        )
                        BEGIN
                            INSERT INTO DailyReport
                            (
                                ReportDate,
                                CustomerId,
                                CategoryId,
                                LineId,
                                PlantId,
                                ModelId,
                                CreatedBy,
                                Status,
                                IsActive,
                                CreatedAt,
                                UpdatedAt
                            )
                            VALUES
                            (
                                @ReportDate,
                                @CustomerId,
                                @CategoryId,
                                @LineId,
                                @PlantId,
                                NULL,
                                @CreatedBy,
                                'INACTIVE',
                                0,
                                SYSUTCDATETIME(),
                                SYSUTCDATETIME()
                            );
                        END;
                        """,
                        connection,
                        transaction);

                Add(
                    marker,
                    "@ReportDate",
                    SqlDbType.Date,
                    request.ReportDate.Date);
                Add(marker, "@PlantId", SqlDbType.Int, GetPlantId());

                Add(
                    marker,
                    "@CustomerId",
                    SqlDbType.Int,
                    request.CustomerId);

                Add(
                    marker,
                    "@CategoryId",
                    SqlDbType.Int,
                    request.CategoryId);

                Add(
                    marker,
                    "@LineId",
                    SqlDbType.Int,
                    request.LineId);

                Add(
                    marker,
                    "@CreatedBy",
                    SqlDbType.Int,
                    userId);

                await marker.ExecuteNonQueryAsync(
                    cancellationToken);
            }
            else
            {
                await using var activate =
                    new SqlCommand(
                        """
                        DELETE FROM DailyReport
                        WHERE ReportDate = @ReportDate
                          AND PlantId = @PlantId
                          AND CustomerId = @CustomerId
                          AND CategoryId = @CategoryId
                          AND LineId = @LineId
                          AND ModelId IS NULL;

                        UPDATE DailyReport
                        SET
                            IsActive = 1,
                            Status =
                                CASE
                                    WHEN Status = 'INACTIVE'
                                    THEN 'DRAFT'
                                    ELSE Status
                                END,
                            UpdatedAt = SYSUTCDATETIME()
                        WHERE ReportDate = @ReportDate
                          AND PlantId = @PlantId
                          AND CustomerId = @CustomerId
                          AND CategoryId = @CategoryId
                          AND LineId = @LineId;
                        """,
                        connection,
                        transaction);

                Add(
                    activate,
                    "@ReportDate",
                    SqlDbType.Date,
                    request.ReportDate.Date);
                Add(activate, "@PlantId", SqlDbType.Int, GetPlantId());

                Add(
                    activate,
                    "@CustomerId",
                    SqlDbType.Int,
                    request.CustomerId);

                Add(
                    activate,
                    "@CategoryId",
                    SqlDbType.Int,
                    request.CategoryId);

                Add(
                    activate,
                    "@LineId",
                    SqlDbType.Int,
                    request.LineId);

                await activate.ExecuteNonQueryAsync(
                    cancellationToken);
            }

            await transaction.CommitAsync(
                cancellationToken);

            return Ok(new
            {
                message =
                    request.IsActive
                        ? "Line activated."
                        : "Line marked inactive and daily values cleared."
            });
        }
        catch
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw;
        }
    }


    // ============================================================
    // GET ENTRIES
    // ============================================================

    [HttpGet("entries")]
    public async Task<IActionResult> GetEntries(
        [FromQuery] int customerId,
        [FromQuery] int categoryId,
        [FromQuery] int lineId,
        [FromQuery] DateTime reportDate,
        CancellationToken cancellationToken)
    {
        if (!await HasCustomerAccessAsync(
                customerId,
                cancellationToken))
        {
            return Forbid();
        }

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                dr.DailyReportId,
                dr.ModelId,
                m.ModelName,
                dr.Status,
                dr.IsActive,

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
                dr.RejectionReason,

                dr.UPPHTarget,
                dr.OSDTarget,
<<<<<<< HEAD
                dr.OTTarget
=======
                dr.OTTarget,
                dr.wip_lying_greater_3_days,
                dr.wip_lying_greater_7_days,
                dr.FPYIntViTarget,
                dr.FPYIntViActual,
                dr.FPYPTTarget,
                dr.FPYPTActual,
                dr.FPYRiTarget,
                dr.FPYRiActual,
                dr.FPYRiImageTarget,
                dr.FPYRiImageActual,
                dr.FPYPackingViTarget,
                dr.FPYPackingViActual,
                dr.WOCount0To3Days,
                dr.WOCount4To7Days,
                dr.WOCountGreater7Days,
                dr.WOCountTotal,
                dr.WOQty0To3Days,
                dr.WOQty4To7Days,
                dr.WOQtyGreater7Days,
                dr.WOQtyTotal,
                dr.WOClearancePlanGreater7Days,
                dr.WOResponsible,
                dr.WOTimeline
>>>>>>> origin/main

            FROM DailyReport dr

            LEFT JOIN Model m
                ON m.ModelId = dr.ModelId

            WHERE dr.CustomerId = @CustomerId
              AND dr.PlantId = @PlantId
              AND dr.CategoryId = @CategoryId
              AND dr.LineId = @LineId
              AND dr.ReportDate = @ReportDate
              AND dr.ModelId IS NOT NULL

            ORDER BY m.ModelName;
            """;

        await using var command =
            new SqlCommand(sql, connection);

        Add(
            command,
            "@CustomerId",
            SqlDbType.Int,
            customerId);

        Add(
            command,
            "@CategoryId",
            SqlDbType.Int,
            categoryId);

        Add(
            command,
            "@LineId",
            SqlDbType.Int,
            lineId);

        Add(
            command,
            "@PlantId",
            SqlDbType.Int,
            GetPlantId());

        Add(
            command,
            "@ReportDate",
            SqlDbType.Date,
            reportDate.Date);

        var entries = new List<object>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new
            {
                dailyReportId = reader.GetInt32(0),

                modelId = reader.GetInt32(1),

                modelName =
                    reader.IsDBNull(2)
                        ? null
                        : reader.GetString(2),

                status = reader.GetString(3),

                isActive = reader.GetBoolean(4),

                monthPlan = GetDecimal(reader, 5),
                monthActual = GetDecimal(reader, 6),

                productionPlan = GetDecimal(reader, 7),
                productionActual = GetDecimal(reader, 8),

                uphTarget = GetDecimal(reader, 9),
                uphActual = GetDecimal(reader, 10),

                upphInstalled = GetDecimal(reader, 11),
                upphActual = GetDecimal(reader, 12),
                cphTarget = GetDecimal(reader, 13),
                cphActual = GetDecimal(reader, 14),

                fpyTarget = GetDecimal(reader, 15),
                fpyActual = GetDecimal(reader, 16),

                ftyTarget = GetDecimal(reader, 17),
                ftyActual = GetDecimal(reader, 18),

                rtyTarget = GetDecimal(reader, 19),
                rtyActual = GetDecimal(reader, 20),

                osdReportingDateValue =
                    GetDecimal(reader, 21),

                osdReportingDatePercent =
                    GetDecimal(reader, 22),

                osdMtdValue =
                    GetDecimal(reader, 23),

                osdMtdPercent =
                    GetDecimal(reader, 24),

                plannedOTManhours =
                    GetDecimal(reader, 25),

                unplannedOTManhours =
                    GetDecimal(reader, 26),

                actualOTManhours =
                    GetDecimal(reader, 27),

                openWOQty =
                    GetInt(reader, 28),

                over7DaysWOBalanceQty =
                    GetInt(reader, 29),

                dailyTRCInQty =
                    GetInt(reader, 30),

                dailyTRCOutQty =
                    GetInt(reader, 31),

                trcOverallFailureInflowPercent =
                    GetDecimal(reader, 32),

                trcLyingOver3DaysCr =
                    GetDecimal(reader, 33),

                issueDescription =
                    GetString(reader, 34),

                rejectionReason =
                    GetString(reader, 35),

                upphTarget = GetDecimal(reader, 36),
                osdTarget = GetDecimal(reader, 37),
<<<<<<< HEAD
                otTarget = GetDecimal(reader, 38)
=======
                otTarget = GetDecimal(reader, 38),
                wipLyingGreater3Days = GetInt(reader, 39),
                wipLyingGreater7Days = GetInt(reader, 40),
                fpyIntViTarget = GetDecimal(reader, 41),
                fpyIntViActual = GetDecimal(reader, 42),
                fpyPtTarget = GetDecimal(reader, 43),
                fpyPtActual = GetDecimal(reader, 44),
                fpyRiTarget = GetDecimal(reader, 45),
                fpyRiActual = GetDecimal(reader, 46),
                fpyRiImageTarget = GetDecimal(reader, 47),
                fpyRiImageActual = GetDecimal(reader, 48),
                fpyPackingViTarget = GetDecimal(reader, 49),
                fpyPackingViActual = GetDecimal(reader, 50),
                woCount0To3Days = GetInt(reader, 51),
                woCount4To7Days = GetInt(reader, 52),
                woCountGreater7Days = GetInt(reader, 53),
                woCountTotal = GetInt(reader, 54),
                woQty0To3Days = GetInt(reader, 55),
                woQty4To7Days = GetInt(reader, 56),
                woQtyGreater7Days = GetInt(reader, 57),
                woQtyTotal = GetInt(reader, 58),
                woClearancePlanGreater7Days = GetInt(reader, 59),
                woResponsible = GetString(reader, 60),
                woTimeline = GetString(reader, 61)
>>>>>>> origin/main
            });
        }

        return Ok(entries);
    }


    // ============================================================
    // GET DRAFT
    // ============================================================

    [HttpGet("draft")]
    public async Task<IActionResult> GetDraft(
        [FromQuery] int customerId,
        [FromQuery] int categoryId,
        [FromQuery] int lineId,
        [FromQuery] DateTime reportDate,
        CancellationToken cancellationToken)
    {
        if (!await HasCustomerAccessAsync(
                customerId,
                cancellationToken))
        {
            return Forbid();
        }

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

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

                dr.Status,
                dr.DailyReportId,
                dr.RejectionReason,

                dr.UPPHTarget,
                dr.OSDTarget,
<<<<<<< HEAD
                dr.OTTarget
=======
                dr.OTTarget,
                dr.wip_lying_greater_3_days,
                dr.wip_lying_greater_7_days,
                dr.FPYIntViTarget,
                dr.FPYIntViActual,
                dr.FPYPTTarget,
                dr.FPYPTActual,
                dr.FPYRiTarget,
                dr.FPYRiActual,
                dr.FPYRiImageTarget,
                dr.FPYRiImageActual,
                dr.FPYPackingViTarget,
                dr.FPYPackingViActual,
                dr.WOCount0To3Days,
                dr.WOCount4To7Days,
                dr.WOCountGreater7Days,
                dr.WOCountTotal,
                dr.WOQty0To3Days,
                dr.WOQty4To7Days,
                dr.WOQtyGreater7Days,
                dr.WOQtyTotal,
                dr.WOClearancePlanGreater7Days,
                dr.WOResponsible,
                dr.WOTimeline
>>>>>>> origin/main

            FROM DailyReport dr

            WHERE dr.CustomerId = @CustomerId
              AND dr.PlantId = @PlantId
              AND dr.CategoryId = @CategoryId
              AND dr.LineId = @LineId
              AND dr.ReportDate = @ReportDate
              AND dr.ModelId IS NOT NULL
              AND dr.Status IN ('DRAFT', 'REJECTED')
              AND dr.IsActive = 1

            ORDER BY
                dr.UpdatedAt DESC,
                dr.DailyReportId DESC;
            """;

        await using var command =
            new SqlCommand(sql, connection);

        Add(
            command,
            "@CustomerId",
            SqlDbType.Int,
            customerId);

        Add(
            command,
            "@CategoryId",
            SqlDbType.Int,
            categoryId);

        Add(
            command,
            "@LineId",
            SqlDbType.Int,
            lineId);

        Add(
            command,
            "@ReportDate",
            SqlDbType.Date,
            reportDate.Date);
        Add(command, "@PlantId", SqlDbType.Int, GetPlantId());

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return Ok(null);

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
            cphTarget = GetDecimal(reader, 9),
            cphActual = GetDecimal(reader, 10),

            fpyTarget = GetDecimal(reader, 11),
            fpyActual = GetDecimal(reader, 12),

            ftyTarget = GetDecimal(reader, 13),
            ftyActual = GetDecimal(reader, 14),

            rtyTarget = GetDecimal(reader, 15),
            rtyActual = GetDecimal(reader, 16),

            osdReportingDateValue =
                GetDecimal(reader, 17),

            osdReportingDatePercent =
                GetDecimal(reader, 18),

            osdMtdValue =
                GetDecimal(reader, 19),

            osdMtdPercent =
                GetDecimal(reader, 20),

            plannedOTManhours =
                GetDecimal(reader, 21),

            unplannedOTManhours =
                GetDecimal(reader, 22),

            actualOTManhours =
                GetDecimal(reader, 23),

            openWOQty =
                GetInt(reader, 24),

            over7DaysWOBalanceQty =
                GetInt(reader, 25),

            dailyTRCInQty =
                GetInt(reader, 26),

            dailyTRCOutQty =
                GetInt(reader, 27),

            trcOverallFailureInflowPercent =
                GetDecimal(reader, 28),

            trcLyingOver3DaysCr =
                GetDecimal(reader, 29),

            issueDescription =
                GetString(reader, 30),

            status = reader.GetString(31),

            dailyReportId = reader.GetInt32(32),

            rejectionReason =
                GetString(reader, 33),

            upphTarget = GetDecimal(reader, 34),
            osdTarget = GetDecimal(reader, 35),
<<<<<<< HEAD
            otTarget = GetDecimal(reader, 36)
=======
            otTarget = GetDecimal(reader, 36),
            wipLyingGreater3Days = GetInt(reader, 37),
            wipLyingGreater7Days = GetInt(reader, 38),
            fpyIntViTarget = GetDecimal(reader, 39),
            fpyIntViActual = GetDecimal(reader, 40),
            fpyPtTarget = GetDecimal(reader, 41),
            fpyPtActual = GetDecimal(reader, 42),
            fpyRiTarget = GetDecimal(reader, 43),
            fpyRiActual = GetDecimal(reader, 44),
            fpyRiImageTarget = GetDecimal(reader, 45),
            fpyRiImageActual = GetDecimal(reader, 46),
            fpyPackingViTarget = GetDecimal(reader, 47),
            fpyPackingViActual = GetDecimal(reader, 48),
            woCount0To3Days = GetInt(reader, 49),
            woCount4To7Days = GetInt(reader, 50),
            woCountGreater7Days = GetInt(reader, 51),
            woCountTotal = GetInt(reader, 52),
            woQty0To3Days = GetInt(reader, 53),
            woQty4To7Days = GetInt(reader, 54),
            woQtyGreater7Days = GetInt(reader, 55),
            woQtyTotal = GetInt(reader, 56),
            woClearancePlanGreater7Days = GetInt(reader, 57),
            woResponsible = GetString(reader, 58),
            woTimeline = GetString(reader, 59)
>>>>>>> origin/main
        });
    }


    // ============================================================
    // SAVE DRAFT
    // ============================================================

    [HttpPost("draft")]
    public async Task<IActionResult> SaveDraft(
        [FromBody] DailyReportRequest request,
        CancellationToken cancellationToken)
        => await Save(
            request,
            "DRAFT",
            cancellationToken);


    // ============================================================
    // SUBMIT
    // ============================================================

    [HttpPost("submit")]
    public async Task<IActionResult> Submit(
        [FromBody] DailyReportRequest request,
        CancellationToken cancellationToken)
        => await Save(
            request,
            "SUBMITTED",
            cancellationToken);


    // ============================================================
    // SAVE CORE
    // ============================================================

    private async Task<IActionResult> Save(
        DailyReportRequest request,
        string status,
        CancellationToken cancellationToken)
    {
        if (request.ReportDate.Date > DateTime.Today)
        {
            return BadRequest(new
            {
                message =
                    "A future reporting date cannot be submitted."
            });
        }

        if (!await ValidateConfiguration(
                request,
                GetPlantId(),
                cancellationToken))
        {
            return BadRequest(new
            {
                message =
                    "Invalid brand, category, line or model configuration."
            });
        }

        var userId = GetUserId();

        if (userId <= 0)
        {
            return Unauthorized(new
            {
                message =
                    "The current user could not be identified."
            });
        }

        if (!await HasCustomerAccessAsync(
                request.CustomerId,
                cancellationToken))
        {
            return Forbid();
        }

        var planEditsError = ValidateKpiPlanEdits(request.PlanEdits);
        if (planEditsError is not null)
            return BadRequest(new { message = planEditsError });

        if (request.PlanEdits is { Count: > 0 })
        {
            if (!User.IsInRole("Contributor"))
                return Forbid();

            if (request.KpiPlanId is null)
                return BadRequest(new { message = "A matching monthly KPI plan is required to save plan edits." });
        }

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var existing =
                await FindReportAsync(
                    connection,
                    transaction,
                    request,
                    cancellationToken);

            int dailyReportId;

            if (existing.HasValue)
            {
                dailyReportId = existing.Value.Id;

                if (existing.Value.Status is "SUBMITTED" or "APPROVED")
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    return status == "SUBMITTED"
                        ? Ok(new
                        {
                            message =
                                existing.Value.Status == "APPROVED"
                                    ? "Daily report is already approved."
                                    : "Daily report is already submitted.",

                            dailyReportId,

                            status = existing.Value.Status
                        })
                        : Conflict(new
                        {
                            message =
                                "This report is already locked for approval."
                        });
                }

                await using var update =
                    new SqlCommand(
                        """
                        UPDATE DailyReport
                        SET
                            ModelId = @ModelId,
                            IsActive = 1,
                            Status = @Status,

                            SubmittedAt =
                                CASE
                                    WHEN @Status = 'SUBMITTED'
                                    THEN SYSUTCDATETIME()
                                    ELSE NULL
                                END,

                            UpdatedAt = SYSUTCDATETIME()

                        WHERE DailyReportId = @DailyReportId;
                        """,
                        connection,
                        transaction);

                Add(
                    update,
                    "@ModelId",
                    SqlDbType.Int,
                    request.ModelId);

                Add(
                    update,
                    "@Status",
                    SqlDbType.VarChar,
                    status);

                Add(
                    update,
                    "@DailyReportId",
                    SqlDbType.Int,
                    dailyReportId);

                await update.ExecuteNonQueryAsync(
                    cancellationToken);
            }
            else
            {
                await using var insert =
                    new SqlCommand(
                        """
                        INSERT INTO DailyReport
                        (
                            PlantId,
                            ReportDate,
                            CustomerId,
                            CategoryId,
                            LineId,
                            ModelId,
                            CreatedBy,
                            Status,
                            IsActive,
                            SubmittedAt,
                            CreatedAt,
                            UpdatedAt
                        )
                        OUTPUT INSERTED.DailyReportId

                        VALUES
                        (
                            @PlantId,
                            @ReportDate,
                            @CustomerId,
                            @CategoryId,
                            @LineId,
                            @ModelId,
                            @CreatedBy,
                            @Status,
                            1,

                            CASE
                                WHEN @Status = 'SUBMITTED'
                                THEN SYSUTCDATETIME()
                                ELSE NULL
                            END,

                            SYSUTCDATETIME(),
                            SYSUTCDATETIME()
                        );
                        """,
                        connection,
                        transaction);

                Add(
                    insert,
                    "@PlantId",
                    SqlDbType.Int,
                    GetPlantId());

                Add(
                    insert,
                    "@ReportDate",
                    SqlDbType.Date,
                    request.ReportDate.Date);

                Add(
                    insert,
                    "@CustomerId",
                    SqlDbType.Int,
                    request.CustomerId);

                Add(
                    insert,
                    "@CategoryId",
                    SqlDbType.Int,
                    request.CategoryId);

                Add(
                    insert,
                    "@LineId",
                    SqlDbType.Int,
                    request.LineId);

                Add(
                    insert,
                    "@ModelId",
                    SqlDbType.Int,
                    request.ModelId);

                Add(
                    insert,
                    "@CreatedBy",
                    SqlDbType.Int,
                    userId);

                Add(
                    insert,
                    "@Status",
                    SqlDbType.VarChar,
                    status);

                dailyReportId =
                    Convert.ToInt32(
                        await insert.ExecuteScalarAsync(
                            cancellationToken));
            }

            if (request.PlanEdits is { Count: > 0 }
                && !await UpdateKpiPlanAsync(
                    connection,
                    transaction,
                    request,
                    cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return Conflict(new
                {
                    message = "The monthly KPI plan changed or no longer matches this report. Reload the report before saving your plan edits."
                });
            }

            // ====================================================
            // IMPORTANT:
            //
            // All KPI values are manually entered daily.
            // They are stored in DailyReport while:
            //
            // DRAFT / SUBMITTED / REJECTED
            //
            // KPI_Daily is created only after approval.
            // ====================================================

            await SaveDailyReportValuesAsync(
                connection,
                transaction,
                request,
                dailyReportId,
                cancellationToken);

            if (status == "SUBMITTED")
            {
                await UpsertApprovalQueueAsync(
                    connection,
                    transaction,
                    dailyReportId,
                    userId,
                    cancellationToken);
            }

            await transaction.CommitAsync(
                cancellationToken);

            return Ok(new
            {
                message =
                    status == "SUBMITTED"
                        ? "Daily report submitted successfully."
                        : "Daily report draft saved.",

                dailyReportId,

                status
            });
        }
        catch (SqlException ex)
            when (ex.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            return Conflict(new
            {
                message =
                    "A report already exists for this line, model and date."
            });
        }
        catch
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw;
        }
    }


    // ============================================================
    // APPROVAL QUEUE
    // ============================================================

    private static async Task UpsertApprovalQueueAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int dailyReportId,
        int submittedBy,
        CancellationToken cancellationToken)
    {
        await using var command =
            new SqlCommand(
                """
                IF EXISTS
                (
                    SELECT 1
                    FROM ApprovalQueue
                    WHERE DailyReportId = @DailyReportId
                )
                BEGIN

                    UPDATE ApprovalQueue
                    SET
                        Status = 'PENDING',
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
                    (
                        DailyReportId,
                        Status,
                        SubmittedBy,
                        SubmittedAt,
                        CreatedAt,
                        UpdatedAt
                    )
                    VALUES
                    (
                        @DailyReportId,
                        'PENDING',
                        @SubmittedBy,
                        SYSUTCDATETIME(),
                        SYSUTCDATETIME(),
                        SYSUTCDATETIME()
                    );

                END
                """,
                connection,
                transaction);

        Add(
            command,
            "@DailyReportId",
            SqlDbType.Int,
            dailyReportId);

        Add(
            command,
            "@SubmittedBy",
            SqlDbType.Int,
            submittedBy);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    // ============================================================
    // FIND EXISTING REPORT
    // ============================================================

    private async Task<(int Id, string Status)?> FindReportAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DailyReportRequest request,
        CancellationToken cancellationToken)
    {
        await using var command =
            new SqlCommand(
                """
                SELECT TOP (1)
                    DailyReportId,
                    Status

                FROM DailyReport WITH (UPDLOCK, HOLDLOCK)

                WHERE PlantId = @PlantId
                  AND ReportDate = @ReportDate
                  AND CustomerId = @CustomerId
                  AND CategoryId = @CategoryId
                  AND LineId = @LineId
                  AND ModelId = @ModelId;
                """,
                connection,
                transaction);

        Add(
            command,
            "@PlantId",
            SqlDbType.Int,
            GetPlantId());

        Add(
            command,
            "@ReportDate",
            SqlDbType.Date,
            request.ReportDate.Date);

        Add(
            command,
            "@CustomerId",
            SqlDbType.Int,
            request.CustomerId);

        Add(
            command,
            "@CategoryId",
            SqlDbType.Int,
            request.CategoryId);

        Add(
            command,
            "@LineId",
            SqlDbType.Int,
            request.LineId);

        Add(
            command,
            "@ModelId",
            SqlDbType.Int,
            request.ModelId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
                cancellationToken))
        {
            return null;
        }

        return (
            reader.GetInt32(0),
            reader.GetString(1)
        );
    }

    private static string? ValidateKpiPlanEdits(
        IReadOnlyList<DailyReportKpiPlanFieldEdit?>? edits)
    {
        if (edits is null || edits.Count == 0)
            return null;

        if (edits.Count > EditableKpiPlanFields.Count)
            return "Too many KPI plan fields were supplied.";

        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edit in edits)
        {
            if (edit is null
                || string.IsNullOrWhiteSpace(edit.Field)
                || !EditableKpiPlanFields.TryGetValue(edit.Field, out var definition))
            {
                return "One or more KPI plan fields are not editable.";
            }

            if (!fields.Add(edit.Field))
                return "A KPI plan field cannot be supplied more than once.";

            if (edit.Value is decimal value)
            {
                var maximum = definition.Scale == 2
                    ? 9999999999999999.99m
                    : 999999.9999m;
                if (value < 0
                    || value > maximum
                    || decimal.Round(value, definition.Scale) != value)
                {
                    return $"The {edit.Field} value is outside the allowed range or precision.";
                }
            }
        }

        return null;
    }

    private async Task<bool> UpdateKpiPlanAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DailyReportRequest request,
        CancellationToken cancellationToken)
    {
        var assignments = new List<string>();
        await using var command = new SqlCommand
        {
            Connection = connection,
            Transaction = transaction
        };

        for (var index = 0; index < request.PlanEdits!.Count; index++)
        {
            var edit = request.PlanEdits[index]!;
            if (!EditableKpiPlanFields.TryGetValue(edit.Field!, out var definition))
                return false;

            var parameterName = $"@PlanValue{index}";
            assignments.Add($"[{definition.Column}] = {parameterName}");
            Add(command, parameterName, SqlDbType.Decimal, edit.Value);
        }

        command.CommandText = $"""
            UPDATE KPI_Master WITH (UPDLOCK, HOLDLOCK)
            SET {string.Join(", ", assignments)},
                UpdatedAt = SYSUTCDATETIME()
            WHERE KPI_MasterId = @PlanId
              AND PlantId = @PlantId
              AND CustomerId = @CustomerId
              AND CategoryId = @CategoryId
              AND LineId = @LineId
              AND ModelId = @ModelId
              AND KPIYEAR = @Year
              AND KPIMonth = @Month
              AND IsActive = 1
              AND 1 =
              (
                  SELECT COUNT(1)
                  FROM KPI_Master matchingPlan WITH (UPDLOCK, HOLDLOCK)
                  WHERE matchingPlan.PlantId = @PlantId
                    AND matchingPlan.CustomerId = @CustomerId
                    AND matchingPlan.CategoryId = @CategoryId
                    AND matchingPlan.LineId = @LineId
                    AND matchingPlan.ModelId = @ModelId
                    AND matchingPlan.KPIYEAR = @Year
                    AND matchingPlan.KPIMonth = @Month
                    AND matchingPlan.IsActive = 1
              )
              AND
              (
                  (UpdatedAt = @ExpectedUpdatedAt)
                  OR (UpdatedAt IS NULL AND @ExpectedUpdatedAt IS NULL)
              );
            """;

        Add(command, "@PlanId", SqlDbType.Int, request.KpiPlanId);
        Add(command, "@PlantId", SqlDbType.Int, GetPlantId());
        Add(command, "@CustomerId", SqlDbType.Int, request.CustomerId);
        Add(command, "@CategoryId", SqlDbType.Int, request.CategoryId);
        Add(command, "@LineId", SqlDbType.Int, request.LineId);
        Add(command, "@ModelId", SqlDbType.Int, request.ModelId);
        Add(command, "@Year", SqlDbType.Int, request.ReportDate.Year);
        Add(command, "@Month", SqlDbType.Int, request.ReportDate.Month);
        Add(command, "@ExpectedUpdatedAt", SqlDbType.DateTime2, request.KpiPlanUpdatedAt);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }


    // ============================================================
    // SAVE ALL DAILY REPORT VALUES
    // ============================================================

    private static async Task SaveDailyReportValuesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DailyReportRequest request,
        int dailyReportId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE DailyReport

            SET
                ModelId = @ModelId,

                MonthPlan = @MonthPlan,
                MonthActual = @MonthActual,

                ProductionPlan = @ProductionPlan,
                ProductionActual = @ProductionActual,

                UPHTarget = @UPHTarget,
                UPHActual = @UPHActual,

                UPPHTarget = @UPPHTarget,
                UPPHInstalled = @UPPHInstalled,
                UPPHActual = @UPPHActual,
                CPHTarget = @CPHTarget,
                CPHActual = @CPHActual,

                FPYTarget = @FPYTarget,
                FPYActual = @FPYActual,

                FTYTarget = @FTYTarget,
                FTYActual = @FTYActual,

                RTYTarget = @RTYTarget,
                RTYActual = @RTYActual,

                OSDReportingDateValue =
                    @OSDReportingDateValue,

                OSDReportingDatePercent =
                    @OSDReportingDatePercent,

                OSDMTDValue =
                    @OSDMTDValue,

                OSDMTDPercent =
                    @OSDMTDPercent,

                OSDTarget = @OSDTarget,

                PlannedOTManhours =
                    @PlannedOTManhours,

                UnplannedOTManhours =
                    @UnplannedOTManhours,

                OTTarget = @OTTarget,

                ActualOTManhours =
                    @ActualOTManhours,

                OpenWOQty =
                    @OpenWOQty,

                Over7DaysWOBalanceQty =
                    @Over7DaysWOBalanceQty,

<<<<<<< HEAD
=======
                wip_lying_greater_3_days =
                    @WipLyingGreater3Days,

                wip_lying_greater_7_days =
                    @WipLyingGreater7Days,

                FPYIntViTarget = @FPYIntViTarget,
                FPYIntViActual = @FPYIntViActual,
                FPYPTTarget = @FPYPTTarget,
                FPYPTActual = @FPYPTActual,
                FPYRiTarget = @FPYRiTarget,
                FPYRiActual = @FPYRiActual,
                FPYRiImageTarget = @FPYRiImageTarget,
                FPYRiImageActual = @FPYRiImageActual,
                FPYPackingViTarget = @FPYPackingViTarget,
                FPYPackingViActual = @FPYPackingViActual,
                WOCount0To3Days = @WOCount0To3Days,
                WOCount4To7Days = @WOCount4To7Days,
                WOCountGreater7Days = @WOCountGreater7Days,
                WOCountTotal = @WOCountTotal,
                WOQty0To3Days = @WOQty0To3Days,
                WOQty4To7Days = @WOQty4To7Days,
                WOQtyGreater7Days = @WOQtyGreater7Days,
                WOQtyTotal = @WOQtyTotal,
                WOClearancePlanGreater7Days = @WOClearancePlanGreater7Days,
                WOResponsible = @WOResponsible,
                WOTimeline = @WOTimeline,

>>>>>>> origin/main
                DailyTRCInQty =
                    @DailyTRCInQty,

                DailyTRCOutQty =
                    @DailyTRCOutQty,

                TRCOverallFailureInflowPercent =
                    @TRCOverallFailureInflowPercent,

                TRCLyingOver3DaysCr =
                    @TRCLyingOver3DaysCr,

                IssueDescription =
                    @IssueDescription,

                UpdatedAt =
                    SYSUTCDATETIME()

            WHERE DailyReportId =
                @DailyReportId;
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        Add(
            command,
            "@ModelId",
            SqlDbType.Int,
            request.ModelId);

        Add(
            command,
            "@MonthPlan",
            SqlDbType.Decimal,
            request.MonthPlan);

        Add(
            command,
            "@MonthActual",
            SqlDbType.Decimal,
            request.MonthActual);

        Add(
            command,
            "@ProductionPlan",
            SqlDbType.Decimal,
            request.ProductionPlan);

        Add(
            command,
            "@ProductionActual",
            SqlDbType.Decimal,
            request.ProductionActual);

        Add(
            command,
            "@UPHTarget",
            SqlDbType.Decimal,
            request.UphTarget);

        Add(
            command,
            "@UPHActual",
            SqlDbType.Decimal,
            request.UphActual);

        Add(
            command,
            "@UPPHTarget",
            SqlDbType.Decimal,
            request.UpphTarget);

        Add(
            command,
            "@UPPHInstalled",
            SqlDbType.Decimal,
            request.UpphInstalled);

        Add(
            command,
            "@UPPHActual",
            SqlDbType.Decimal,
            request.UpphActual);

        Add(
            command,
            "@CPHTarget",
            SqlDbType.Decimal,
            request.CPHTarget);

        Add(
            command,
            "@CPHActual",
            SqlDbType.Decimal,
            request.CPHActual);

        Add(
            command,
            "@FPYTarget",
            SqlDbType.Decimal,
            request.FpyTarget);

        Add(
            command,
            "@FPYActual",
            SqlDbType.Decimal,
            request.FpyActual);

        Add(
            command,
            "@FTYTarget",
            SqlDbType.Decimal,
            request.FtyTarget);

        Add(
            command,
            "@FTYActual",
            SqlDbType.Decimal,
            request.FtyActual);

        Add(
            command,
            "@RTYTarget",
            SqlDbType.Decimal,
            request.RtyTarget);

        Add(
            command,
            "@RTYActual",
            SqlDbType.Decimal,
            request.RtyActual);

        // ========================================================
        // OSD
        // ========================================================

        Add(
            command,
            "@OSDReportingDateValue",
            SqlDbType.Decimal,
            request.OsdReportingDateValue);

        Add(
            command,
            "@OSDReportingDatePercent",
            SqlDbType.Decimal,
            request.OsdReportingDatePercent);

        Add(
            command,
            "@OSDMTDValue",
            SqlDbType.Decimal,
            request.OsdMtdValue);

        Add(
            command,
            "@OSDMTDPercent",
            SqlDbType.Decimal,
            request.OsdMtdPercent);

        Add(
            command,
            "@OSDTarget",
            SqlDbType.Decimal,
            request.OsdTarget);

        // ========================================================
        // OT
        // ========================================================

        Add(
            command,
            "@PlannedOTManhours",
            SqlDbType.Decimal,
            request.PlannedOTManhours);

        Add(
            command,
            "@UnplannedOTManhours",
            SqlDbType.Decimal,
            request.UnplannedOTManhours);

        Add(
            command,
            "@OTTarget",
            SqlDbType.Decimal,
            request.OtTarget);

        Add(
            command,
            "@ActualOTManhours",
            SqlDbType.Decimal,
            request.ActualOTManhours);

        // ========================================================
        // WORK ORDERS
        // ========================================================

        Add(
            command,
            "@OpenWOQty",
            SqlDbType.Int,
            request.OpenWOQty);

        Add(
            command,
            "@Over7DaysWOBalanceQty",
            SqlDbType.Int,
            request.Over7DaysWOBalanceQty);

<<<<<<< HEAD
=======
        Add(
            command,
            "@WipLyingGreater3Days",
            SqlDbType.Int,
            request.WipLyingGreater3Days);

        Add(
            command,
            "@WipLyingGreater7Days",
            SqlDbType.Int,
            request.WipLyingGreater7Days);

        Add(command, "@FPYIntViTarget", SqlDbType.Decimal, request.FPYIntViTarget);
        Add(command, "@FPYIntViActual", SqlDbType.Decimal, request.FPYIntViActual);
        Add(command, "@FPYPTTarget", SqlDbType.Decimal, request.FPYPTTarget);
        Add(command, "@FPYPTActual", SqlDbType.Decimal, request.FPYPTActual);
        Add(command, "@FPYRiTarget", SqlDbType.Decimal, request.FPYRiTarget);
        Add(command, "@FPYRiActual", SqlDbType.Decimal, request.FPYRiActual);
        Add(command, "@FPYRiImageTarget", SqlDbType.Decimal, request.FPYRiImageTarget);
        Add(command, "@FPYRiImageActual", SqlDbType.Decimal, request.FPYRiImageActual);
        Add(command, "@FPYPackingViTarget", SqlDbType.Decimal, request.FPYPackingViTarget);
        Add(command, "@FPYPackingViActual", SqlDbType.Decimal, request.FPYPackingViActual);
        Add(command, "@WOCount0To3Days", SqlDbType.Int, request.WOCount0To3Days);
        Add(command, "@WOCount4To7Days", SqlDbType.Int, request.WOCount4To7Days);
        Add(command, "@WOCountGreater7Days", SqlDbType.Int, request.WOCountGreater7Days);
        Add(command, "@WOCountTotal", SqlDbType.Int, request.WOCountTotal);
        Add(command, "@WOQty0To3Days", SqlDbType.Int, request.WOQty0To3Days);
        Add(command, "@WOQty4To7Days", SqlDbType.Int, request.WOQty4To7Days);
        Add(command, "@WOQtyGreater7Days", SqlDbType.Int, request.WOQtyGreater7Days);
        Add(command, "@WOQtyTotal", SqlDbType.Int, request.WOQtyTotal);
        Add(command, "@WOClearancePlanGreater7Days", SqlDbType.Int, request.WOClearancePlanGreater7Days);
        Add(command, "@WOResponsible", SqlDbType.NVarChar, request.WOResponsible?.Trim());
        Add(command, "@WOTimeline", SqlDbType.NVarChar, request.WOTimeline?.Trim());

>>>>>>> origin/main
        // ========================================================
        // TRC
        // ========================================================

        Add(
            command,
            "@DailyTRCInQty",
            SqlDbType.Int,
            request.DailyTRCInQty);

        Add(
            command,
            "@DailyTRCOutQty",
            SqlDbType.Int,
            request.DailyTRCOutQty);

        Add(
            command,
            "@TRCOverallFailureInflowPercent",
            SqlDbType.Decimal,
            request.TRCOverallFailureInflowPercent);

        Add(
            command,
            "@TRCLyingOver3DaysCr",
            SqlDbType.Decimal,
            request.TRCLyingOver3DaysCr);

        // ========================================================
        // ISSUE
        // ========================================================

        Add(
            command,
            "@IssueDescription",
            SqlDbType.VarChar,
            request.IssueDescription?.Trim());

        Add(
            command,
            "@DailyReportId",
            SqlDbType.Int,
            dailyReportId);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    // ============================================================
    // VALIDATE CONFIGURATION
    // ============================================================

    private async Task<bool> ValidateConfiguration(
        DailyReportRequest request,
        int plantId,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenAsync(
                cancellationToken);

        // Validate each relationship directly. LineModel is intentionally not
        // required: an active model belongs to a customer, and any active line
        // under that customer's selected category can report that model.
        const string sql = """
            SELECT COUNT(1)
            FROM Customer c
            INNER JOIN Category cat
                ON cat.CustomerId = c.CustomerId
               AND cat.PlantId = @PlantId
            INNER JOIN ProductionLine pl
                ON pl.CategoryId = cat.CategoryId
               AND pl.PlantId = cat.PlantId
            INNER JOIN Model m
                ON m.CustomerId = c.CustomerId
            WHERE c.CustomerId = @CustomerId
              AND cat.CategoryId = @CategoryId
              AND pl.LineId = @LineId
              AND m.ModelId = @ModelId
              AND c.IsActive = 1
              AND cat.IsActive = 1
              AND pl.IsActive = 1
              AND m.IsActive = 1;
            """;

        await using var command =
            new SqlCommand(sql, connection);

        Add(
            command,
            "@CustomerId",
            SqlDbType.Int,
            request.CustomerId);

        Add(
            command,
            "@CategoryId",
            SqlDbType.Int,
            request.CategoryId);

        Add(
            command,
            "@LineId",
            SqlDbType.Int,
            request.LineId);

        Add(
            command,
            "@ModelId",
            SqlDbType.Int,
            request.ModelId);

        Add(command, "@PlantId", SqlDbType.Int, plantId);

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(
                cancellationToken)) == 1;
    }


    // ============================================================
    // CUSTOMER ACCESS
    // ============================================================

    private async Task<bool> HasCustomerAccessAsync(
        int customerId,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var plantId = GetPlantId();

        if (userId <= 0 || plantId <= 0)
            return false;

        await using var connection =
            await connectionFactory.OpenAsync(
                cancellationToken);

        await using var command =
            new SqlCommand(
                """
                SELECT COUNT(1)
                FROM PlantCustomer pc
                INNER JOIN Customer c ON c.CustomerId = pc.CustomerId
                INNER JOIN Plant p ON p.PlantId = pc.PlantId
                WHERE pc.PlantId = @PlantId
                  AND pc.CustomerId = @CustomerId
                  AND c.IsActive = 1
                  AND p.IsActive = 1
                  AND
                  (
                      @IsAdmin = 1
                      OR EXISTS
                      (
                          SELECT 1
                          FROM UserCustomer uc
                          WHERE uc.UserId = @UserId
                            AND uc.CustomerId = pc.CustomerId
                      )
                  )
                """,
                connection);

        Add(
            command,
            "@PlantId",
            SqlDbType.Int,
            plantId);

        Add(
            command,
            "@UserId",
            SqlDbType.Int,
            userId);

        Add(
            command,
            "@CustomerId",
            SqlDbType.Int,
            customerId);

        Add(
            command,
            "@IsAdmin",
            SqlDbType.Bit,
            User.IsInRole("Admin"));

        return Convert.ToInt32(
            await command.ExecuteScalarAsync(
                cancellationToken)) == 1;
    }


    // ============================================================
    // USER ID
    // ============================================================

    private int GetUserId()
    {
        var value =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier);

        return int.TryParse(
            value,
            out var id)
            ? id
            : 0;
    }

    private int GetPlantId()
    {
        return int.TryParse(User.FindFirstValue("plantId"), out var plantId)
            ? plantId
            : 0;
    }


    // ============================================================
    // SQL PARAMETER HELPER
    // ============================================================

    private static void Add(
        SqlCommand command,
        string name,
        SqlDbType type,
        object? value)
    {
        var parameter =
            command.Parameters.Add(
                name,
                type);

        parameter.Value =
            value ?? DBNull.Value;

        if (type == SqlDbType.Decimal)
        {
            parameter.Precision = 18;
            parameter.Scale = 4;
        }
    }


    // ============================================================
    // DATA READER HELPERS
    // ============================================================

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

public sealed class DailyReportExcelImportRequest
{
    public IFormFile? File { get; set; }

    public int CustomerId { get; set; }

    public int CategoryId { get; set; }

    public DateTime ReportDate { get; set; }
}


// ==================================================================
// LINE STATUS REQUEST
// ==================================================================

public sealed class LineStatusRequest
{
    public DateTime ReportDate { get; set; }

    public int CustomerId { get; set; }

    public int CategoryId { get; set; }

    public int LineId { get; set; }

    public bool IsActive { get; set; }
}


// ==================================================================
// DAILY REPORT REQUEST
// ==================================================================

public sealed class DailyReportRequest
{
    public DateTime ReportDate { get; set; }

    public int CustomerId { get; set; }

    public int CategoryId { get; set; }

    public int LineId { get; set; }

    public int? ModelId { get; set; }

    public int? KpiPlanId { get; set; }

    public DateTime? KpiPlanUpdatedAt { get; set; }

    public List<DailyReportKpiPlanFieldEdit?>? PlanEdits { get; set; }


    // ==============================================================
    // MONTH
    // ==============================================================

    public decimal? MonthPlan { get; set; }

    public decimal? MonthActual { get; set; }


    // ==============================================================
    // PRODUCTION
    // ==============================================================

    public decimal? ProductionPlan { get; set; }

    public decimal? ProductionActual { get; set; }


    // ==============================================================
    // UPH
    // ==============================================================

    public decimal? UphTarget { get; set; }

    public decimal? UphActual { get; set; }


    // ==============================================================
    // UPPH
    // ==============================================================

    public decimal? UpphInstalled { get; set; }

    public decimal? UpphActual { get; set; }

    public decimal? UpphTarget { get; set; }

    public decimal? CPHTarget { get; set; }

    public decimal? CPHActual { get; set; }


    // ==============================================================
    // FPY
    // ==============================================================

    public decimal? FpyTarget { get; set; }

    public decimal? FpyActual { get; set; }


    // ==============================================================
    // FTY
    // ==============================================================

    public decimal? FtyTarget { get; set; }

    public decimal? FtyActual { get; set; }


    // ==============================================================
    // RTY
    // ==============================================================

    public decimal? RtyTarget { get; set; }

    public decimal? RtyActual { get; set; }


    // ==============================================================
    // OS&D
    // ==============================================================

    public decimal? OsdReportingDateValue { get; set; }

    public decimal? OsdReportingDatePercent { get; set; }

    public decimal? OsdMtdValue { get; set; }

    public decimal? OsdMtdPercent { get; set; }

    public decimal? OsdTarget { get; set; }


    // ==============================================================
    // OT
    // ==============================================================

    public decimal? PlannedOTManhours { get; set; }

    public decimal? UnplannedOTManhours { get; set; }

    public decimal? OtTarget { get; set; }

    public decimal? ActualOTManhours { get; set; }


    // ==============================================================
    // WORK ORDER
    // ==============================================================

    public int? OpenWOQty { get; set; }

    public int? Over7DaysWOBalanceQty { get; set; }

<<<<<<< HEAD
=======
    public int WipLyingGreater3Days { get; set; }

    public int WipLyingGreater7Days { get; set; }

    public decimal? FPYIntViTarget { get; set; }
    public decimal? FPYIntViActual { get; set; }
    public decimal? FPYPTTarget { get; set; }
    public decimal? FPYPTActual { get; set; }
    public decimal? FPYRiTarget { get; set; }
    public decimal? FPYRiActual { get; set; }
    public decimal? FPYRiImageTarget { get; set; }
    public decimal? FPYRiImageActual { get; set; }
    public decimal? FPYPackingViTarget { get; set; }
    public decimal? FPYPackingViActual { get; set; }
    public int? WOCount0To3Days { get; set; }
    public int? WOCount4To7Days { get; set; }
    public int? WOCountGreater7Days { get; set; }
    public int? WOCountTotal { get; set; }
    public int? WOQty0To3Days { get; set; }
    public int? WOQty4To7Days { get; set; }
    public int? WOQtyGreater7Days { get; set; }
    public int? WOQtyTotal { get; set; }
    public int? WOClearancePlanGreater7Days { get; set; }
    public string? WOResponsible { get; set; }
    public string? WOTimeline { get; set; }

>>>>>>> origin/main

    // ==============================================================
    // TRC
    // ==============================================================

    public int? DailyTRCInQty { get; set; }

    public int? DailyTRCOutQty { get; set; }

    public decimal? TRCOverallFailureInflowPercent { get; set; }

    public decimal? TRCLyingOver3DaysCr { get; set; }


    // ==============================================================
    // ISSUE
    // ==============================================================

    public string? IssueDescription { get; set; }
}

public sealed record DailyReportKpiPlanFieldEdit(string? Field, decimal? Value);