using System.Globalization;
using System.Xml;
using ClosedXML.Excel;
using Dixon.CommandCenter.API.Data;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Services;

public sealed class DailyReportExcelImportService(
    SqlConnectionFactory connectionFactory)
{
    private static readonly HashSet<string> EmptyNumericValues =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "",
            "-",
            "–",
            "—",
            "−",
            "NA",
            "N/A",
            "NP",
            "NO PLAN"
        };

    public async Task<DailyReportExcelImportPreview> PreviewAsync(
        Stream workbookStream,
        int plantId,
        int customerId,
        int categoryId,
        DateTime selectedReportDate,
        CancellationToken cancellationToken,
        string importType = "production")
    {
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        const string contextSql = """
            SELECT c.CustomerName, cat.CategoryName
            FROM Customer c
            INNER JOIN Category cat
                ON cat.CustomerId = c.CustomerId
               AND cat.PlantId = @PlantId
            WHERE c.CustomerId = @CustomerId
              AND cat.CategoryId = @CategoryId
              AND c.IsActive = 1
              AND cat.IsActive = 1;
            """;

        await using var contextCommand =
            new SqlCommand(contextSql, connection);
        contextCommand.Parameters.AddWithValue("@CustomerId", customerId);
        contextCommand.Parameters.AddWithValue("@CategoryId", categoryId);
        contextCommand.Parameters.AddWithValue("@PlantId", plantId);

        string? customerName = null;
        string? categoryName = null;

        await using (var reader =
                     await contextCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                customerName = reader.GetString(0);
                categoryName = reader.GetString(1);
            }
        }

        if (customerName is null || categoryName is null)
        {
            return Failure(
                selectedReportDate,
                "The selected brand or category is not active or does not exist.");
        }

        var normalizedImportType = importType.Trim().ToUpperInvariant();
        var expectedMetric = categoryName.Trim().ToUpperInvariant() switch
        {
            "FATP" => "UPPH",
            "SMT" => "CPH",
            _ => null
        };
        if (normalizedImportType == "PRODUCTION" && expectedMetric is null)
            return Failure(selectedReportDate, $"Production Excel import supports FATP and SMT categories, not '{categoryName}'.");
        if (normalizedImportType is not ("PRODUCTION" or "QUALITY" or "WO_CLOSURE"))
            return Failure(selectedReportDate, $"Unsupported Excel import type '{importType}'.");

        var catalog = await LoadCatalogAsync(
            connection,
            plantId,
            customerId,
            categoryId,
            cancellationToken);

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(workbookStream);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or IOException
                or ArgumentException
                or XmlException
                or FormatException)
        {
            return Failure(
                selectedReportDate,
                "The uploaded file is not a valid .xlsx workbook.");
        }

        using (workbook)
        {
            var worksheets = workbook.Worksheets
                .ToArray();

            if (normalizedImportType == "QUALITY")
                return PreviewQuality(worksheets, selectedReportDate, customerName, catalog);
            if (normalizedImportType == "WO_CLOSURE")
                return PreviewWoClosure(worksheets, selectedReportDate, customerName, categoryName);

            worksheets = worksheets
                .Where(sheet => !string.Equals(
                    sheet.Name.Trim(),
                    "D-1 KPI Matrix loop Closer",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var dailySheets = worksheets
                .SelectMany(sheet => FindSections(sheet))
                .ToArray();

            if (dailySheets.Length == 0)
            {
                return Failure(
                    selectedReportDate,
                    "The workbook does not contain the expected daily operations report sheet.");
            }

            var selectedSections = dailySheets
                .Where(section => string.Equals(
                    section.Metric,
                    expectedMetric,
                    StringComparison.OrdinalIgnoreCase))
                .Where(section => !string.Equals(categoryName, "FATP", StringComparison.OrdinalIgnoreCase)
                    || section.Columns.CapacityTarget > 0
                    && Normalize(section.Worksheet.Cell(
                        section.HeaderRow + 1,
                        section.Columns.CapacityTarget).GetString()) == "INSTALLED")
                .ToArray();

            if (string.Equals(categoryName, "SMT", StringComparison.OrdinalIgnoreCase)
                && selectedSections.Length == 0)
            {
                selectedSections = dailySheets
                    .Where(section => string.Equals(
                        section.Metric,
                        "UPPH",
                        StringComparison.OrdinalIgnoreCase))
                    .Select(section => section with { Metric = "CPH" })
                    .ToArray();
            }

            if (selectedSections.Length == 0)
            {
                if (string.Equals(categoryName, "FATP", StringComparison.OrdinalIgnoreCase)
                    && dailySheets.Any(section => section.Metric == "UPPH"))
                {
                    return Failure(
                        selectedReportDate,
                        "The workbook has a UPPH section, but its FATP production table must use the 'Installed' subheading. The available UPPH section does not match the FATP layout.");
                }

                var otherMetric = expectedMetric == "CPH" ? "UPPH" : "CPH";
                var mismatchCategory = expectedMetric == "CPH" ? "FATP" : "SMT";
                return Failure(
                    selectedReportDate,
                    $"Selected category is {categoryName}, but the workbook contains no {expectedMetric} section for it. The available {otherMetric} section is for {mismatchCategory}.");
            }

            var sectionDates = selectedSections
                .Select(section => ReadReportDate(section.Worksheet))
                .ToArray();
            if (sectionDates.Any(date => date is null)
                || sectionDates.Select(date => date!.Value.Date).Distinct().Count() != 1)
            {
                return Failure(
                    selectedReportDate,
                    $"The {categoryName} sections must each contain the same valid report date.");
            }

            var reportDate = sectionDates[0]!.Value.Date;
            if (string.Equals(categoryName, "FATP", StringComparison.OrdinalIgnoreCase))
            {
                return PreviewFATP(
                    selectedSections,
                    reportDate,
                    selectedReportDate,
                    customerName,
                    catalog);
            }

            var errors = new List<string>();
            var warnings = new List<string>();
            var imported = new List<DailyReportExcelImportEntry>();
            var availableCustomers = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var section in selectedSections)
            {
                var importSection = section;
                if (string.Equals(categoryName, "SMT", StringComparison.OrdinalIgnoreCase)
                    && section.Metric == "CPH")
                {
                    importSection = section with
                    {
                        Columns = section.Columns with
                        {
                            CapacityTarget = section.Columns.CapacityTarget > 0
                                ? section.Columns.CapacityTarget
                                : section.OtherCapacityTarget,
                            CapacityActual = section.Columns.CapacityActual > 0
                                ? section.Columns.CapacityActual
                                : section.OtherCapacityActual
                        }
                    };
                }

                for (var rowNumber = importSection.HeaderRow + 2;
                     rowNumber <= importSection.LastDataRow;
                     rowNumber++)
                {
                    var lineNo = ReadText(importSection.Worksheet, rowNumber, importSection.Columns.LineNo);
                    if (string.IsNullOrWhiteSpace(lineNo))
                        continue;

                    if (string.Equals(lineNo, "Total", StringComparison.OrdinalIgnoreCase))
                        break;

                    var excelCustomer = ReadText(importSection.Worksheet, rowNumber, importSection.Columns.Customer);
                    if (string.IsNullOrWhiteSpace(excelCustomer))
                    {
                        warnings.Add($"Excel row {rowNumber} ({lineNo}) was skipped because Customer is blank.");
                        continue;
                    }

                    availableCustomers.Add(excelCustomer);
                    if (!string.Equals(
                            excelCustomer.Trim(),
                            customerName.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var sapLocation = ReadText(importSection.Worksheet, rowNumber, importSection.Columns.SapLocation);
                    var modelName = ReadText(importSection.Worksheet, rowNumber, importSection.Columns.Model);
                    var rowLabel = $"Excel row {rowNumber} ({lineNo}, {modelName ?? "unknown model"})";

                    if (string.IsNullOrWhiteSpace(sapLocation))
                        warnings.Add($"{rowLabel}: row skipped because SAP Location is blank.");
                    if (string.IsNullOrWhiteSpace(modelName))
                        warnings.Add($"{rowLabel}: row skipped because Model is blank.");
                    if (string.IsNullOrWhiteSpace(sapLocation)
                        || string.IsNullOrWhiteSpace(modelName))
                        continue;

                    var matchingOtherCategoryLine = catalog.OtherCategoryLines.Any(item =>
                        NormalizeMatchName(item.LineName) == NormalizeMatchName(lineNo)
                        && NormalizeMatchName(item.SapLocation ?? "") == NormalizeMatchName(sapLocation));
                    var matchingSelectedLine = catalog.Lines.Any(item =>
                        NormalizeMatchName(item.LineName) == NormalizeMatchName(lineNo)
                        && NormalizeMatchName(item.SapLocation ?? "") == NormalizeMatchName(sapLocation));
                    if (!matchingSelectedLine && matchingOtherCategoryLine)
                        continue;

                    var rowSection = importSection;
                    if (string.Equals(categoryName, "SMT", StringComparison.OrdinalIgnoreCase)
                        && section.Metric == "CPH")
                    {
                        rowSection = importSection with
                        {
                            Columns = importSection.Columns with
                            {
                                CapacityTarget = !HasNumericValue(
                                    importSection.Worksheet,
                                    rowNumber,
                                    importSection.Columns.CapacityTarget)
                                    && HasNumericValue(importSection.Worksheet, rowNumber, section.OtherCapacityTarget)
                                    ? section.OtherCapacityTarget
                                    : importSection.Columns.CapacityTarget,
                                CapacityActual = !HasNumericValue(
                                    importSection.Worksheet,
                                    rowNumber,
                                    importSection.Columns.CapacityActual)
                                    && HasNumericValue(importSection.Worksheet, rowNumber, section.OtherCapacityActual)
                                    ? section.OtherCapacityActual
                                    : importSection.Columns.CapacityActual
                            }
                        };
                    }

                    if (!string.Equals(categoryName, "SMT", StringComparison.OrdinalIgnoreCase)
                        && !HasCapacityValue(section, rowNumber)
                        && HasOtherCapacityValue(section, rowNumber))
                        continue;

                    var lineNameMatches = catalog.Lines
                        .Where(item => NormalizeMatchName(item.LineName) == NormalizeMatchName(lineNo))
                        .ToArray();
                    var lineMatches = lineNameMatches
                        .Where(item => NormalizeMatchName(item.SapLocation ?? "") == NormalizeMatchName(sapLocation))
                        .ToArray();

                    if (lineMatches.Length == 0)
                    {
                        warnings.Add(lineNameMatches.Length == 0
                            ? $"{rowLabel}: Line {lineNo} does not exist for {categoryName}."
                            : $"{rowLabel}: SAP Location '{sapLocation}' is not valid for line {lineNo}.");
                        continue;
                    }

                    if (lineMatches.Length > 1)
                    {
                        warnings.Add($"{rowLabel}: row skipped because line {lineNo} and SAP Location '{sapLocation}' match more than one active {categoryName} line.");
                        continue;
                    }

                    var line = lineMatches[0];
                    var modelMatches = catalog.Models
                        .Where(item => Normalize(item.Name) == Normalize(modelName))
                        .ToArray();

                    if (modelMatches.Length == 0)
                    {
                        warnings.Add($"{rowLabel}: row skipped because model '{modelName}' does not exist for brand '{customerName}'.");
                        continue;
                    }

                    if (modelMatches.Length > 1)
                    {
                        warnings.Add($"{rowLabel}: row skipped because model '{modelName}' is ambiguous in the selected brand.");
                        continue;
                    }

                    var model = modelMatches[0];

                    // Models are selected by customer, not by LineModel mapping.
                    // A model may be reported on any active line in the selected category.
                    var values = ReadValues(rowSection, rowNumber, rowLabel, warnings);
                    if (values is null)
                        continue;

                    imported.Add(new DailyReportExcelImportEntry(
                        line.Id,
                        line.LineName,
                        line.SapLocation,
                        model.Id,
                        model.Name,
                        values));
                }
            }

            if (imported.Count == 0 && errors.Count == 0)
            {
                var customers = string.Join(", ", availableCustomers.OrderBy(name => name));
                errors.Add(
                    $"No {categoryName} rows matched selected brand '{customerName}'."
                    + (customers.Length == 0 ? "" : $" Brands in the workbook: {customers}."));
                errors.AddRange(warnings);
            }

            var duplicates = imported
                .GroupBy(entry => (entry.LineId, entry.ModelId))
                .Where(group => group.Count() > 1);
            foreach (var duplicate in duplicates)
            {
                var entry = duplicate.First();
                errors.Add(
                    $"The workbook contains duplicate rows for model '{entry.ModelName}' on line {entry.LineNo}.");
            }

            if (errors.Count > 0)
            {
                return new DailyReportExcelImportPreview(
                    false,
                    reportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    0,
                    0,
                    errors,
                    []);
            }

            return new DailyReportExcelImportPreview(
                true,
                reportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                imported.Select(entry => entry.LineId).Distinct().Count(),
                imported.Count,
                warnings,
                imported);
        }
    }

    private static async Task<ImportCatalog> LoadCatalogAsync(
        SqlConnection connection,
        int plantId,
        int customerId,
        int categoryId,
        CancellationToken cancellationToken)
    {
        const string lineSql = """
            SELECT LineId, LineName, SAPLocation
            FROM ProductionLine
            WHERE CategoryId = @CategoryId
              AND PlantId = @PlantId
              AND IsActive = 1;
            """;

        await using var lineCommand = new SqlCommand(lineSql, connection);
        lineCommand.Parameters.AddWithValue("@CategoryId", categoryId);
        lineCommand.Parameters.AddWithValue("@PlantId", plantId);

        var lines = new List<CatalogLine>();
        await using (var lineReader =
                     await lineCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await lineReader.ReadAsync(cancellationToken))
            {
                lines.Add(new CatalogLine(
                    lineReader.GetInt32(0),
                    lineReader.GetString(1),
                    lineReader.IsDBNull(2) ? null : lineReader.GetString(2)));
            }
        }

        // All active models belonging to the selected customer are valid choices.
        // Do not require a LineModel row for Daily Report or Excel import.
        const string modelSql = """
            SELECT ModelId, ModelName
            FROM Model
            WHERE CustomerId = @CustomerId
              AND IsActive = 1;
            """;

        await using var modelCommand = new SqlCommand(modelSql, connection);
        modelCommand.Parameters.AddWithValue("@CustomerId", customerId);

        var models = new List<CatalogModel>();
        await using (var modelReader =
                     await modelCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await modelReader.ReadAsync(cancellationToken))
            {
                models.Add(new CatalogModel(
                    modelReader.GetInt32(0),
                    modelReader.GetString(1)));
            }
        }

        const string otherCategoryLineSql = """
            SELECT pl.LineName, pl.SAPLocation, cat.CategoryName
            FROM ProductionLine pl
            INNER JOIN Category cat
                ON cat.CategoryId = pl.CategoryId
               AND cat.PlantId = pl.PlantId
               AND cat.IsActive = 1
            WHERE pl.PlantId = @PlantId
              AND cat.CustomerId = @CustomerId
              AND cat.CategoryId <> @CategoryId
              AND pl.IsActive = 1;
            """;

        await using var otherLineCommand = new SqlCommand(otherCategoryLineSql, connection);
        otherLineCommand.Parameters.AddWithValue("@PlantId", plantId);
        otherLineCommand.Parameters.AddWithValue("@CustomerId", customerId);
        otherLineCommand.Parameters.AddWithValue("@CategoryId", categoryId);

        var otherCategoryLines = new List<CatalogOtherLine>();
        await using (var otherLineReader =
                     await otherLineCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await otherLineReader.ReadAsync(cancellationToken))
            {
                otherCategoryLines.Add(new CatalogOtherLine(
                    otherLineReader.GetString(0),
                    otherLineReader.IsDBNull(1) ? null : otherLineReader.GetString(1),
                    otherLineReader.GetString(2)));
            }
        }

        return new ImportCatalog(lines, models, otherCategoryLines);
    }

    private static void ValidateFATPColumns(
        DailySection section,
        List<string> errors)
    {
        var columns = section.Columns;
        var required = new (string Name, int Column)[]
        {
            ("Line No.", columns.LineNo),
            ("SAP Location", columns.SapLocation),
            ("Customer", columns.Customer),
            ("Model", columns.Model),
            ("Month Plan / Plan", columns.MonthPlan),
            ("Month Plan / Actual", columns.MonthActual),
            ("Production / Plan", columns.ProductionPlan),
            ("Production / Actual", columns.ProductionActual),
            ("UPH / Target", columns.UphTarget),
            ("UPH / Actual", columns.UphActual),
            ("UPPH / Installed", columns.CapacityTarget),
            ("UPPH / Actual", columns.CapacityActual),
            ("OS&D Reporting Date / Value (INR)", columns.OsdReportingDateValue),
            ("OS&D Reporting Date / %", columns.OsdReportingDatePercent),
            ("OS&D MTD / Value (INR)", columns.OsdMtdValue),
            ("OS&D MTD / %", columns.OsdMtdPercent),
            ("Open WO (Qty)", columns.OpenWoQty),
            (">7 Days WO (Qty)", columns.Over7DaysWoQty),
            ("Issue description", columns.IssueDescription)
        };

        var missing = required.Where(item => item.Column == 0)
            .Select(item => item.Name)
            .ToArray();
        if (missing.Length > 0)
            errors.Add($"The FATP production table is missing expected columns: {string.Join(", ", missing)}.");
    }

    private static DailyReportExcelImportPreview PreviewFATP(
        IReadOnlyList<DailySection> sections,
        DateTime workbookDate,
        DateTime selectedReportDate,
        string customerName,
        ImportCatalog catalog)
    {
        var errors = new List<string>();
        foreach (var section in sections)
            ValidateFATPColumns(section, errors);

        if (errors.Count > 0)
            return Failure(workbookDate, string.Join(" ", errors));

        var rows = new List<DailyReportExcelImportRow>();
        foreach (var section in sections)
        {
            for (var rowNumber = section.HeaderRow + 2;
                 rowNumber <= section.LastDataRow;
                 rowNumber++)
            {
                var lineNo = ReadSourceText(section.Worksheet, rowNumber, section.Columns.LineNo);
                var sapLocation = ReadSourceText(section.Worksheet, rowNumber, section.Columns.SapLocation);
                var rowCustomer = ReadSourceText(section.Worksheet, rowNumber, section.Columns.Customer);
                var modelName = ReadSourceText(section.Worksheet, rowNumber, section.Columns.Model);
                if (string.IsNullOrWhiteSpace(lineNo)
                    && string.IsNullOrWhiteSpace(sapLocation)
                    && string.IsNullOrWhiteSpace(rowCustomer)
                    && string.IsNullOrWhiteSpace(modelName)
                    && !section.Worksheet.Row(rowNumber).CellsUsed().Any(cell => !cell.IsEmpty()))
                    continue;
                if (string.Equals(lineNo?.Trim(), "Total", StringComparison.OrdinalIgnoreCase))
                    break;

                if (!string.IsNullOrWhiteSpace(rowCustomer)
                    && NormalizeMatchName(rowCustomer!) != NormalizeMatchName(customerName))
                {
                    continue;
                }

                var selectedLineMatches = catalog.Lines.Any(line =>
                    NormalizeMatchName(line.LineName) == NormalizeMatchName(lineNo ?? "")
                    && NormalizeMatchName(line.SapLocation ?? "") == NormalizeMatchName(sapLocation ?? ""));
                var otherCategoryLineMatches = catalog.OtherCategoryLines.Any(line =>
                    !string.Equals(line.CategoryName, "FATP", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(sapLocation)
                    && !string.IsNullOrWhiteSpace(line.SapLocation)
                    && NormalizeMatchName(line.LineName) == NormalizeMatchName(lineNo ?? "")
                    && NormalizeMatchName(line.SapLocation ?? "") == NormalizeMatchName(sapLocation!));

                if (!selectedLineMatches && otherCategoryLineMatches)
                {
                    continue;
                }

                if (!HasCapacityValue(section, rowNumber)
                    && HasOtherCapacityValue(section, rowNumber))
                    continue;

                var reasonParts = new List<string>();
                int? lineId = null;
                string? mappedLineName = null;
                if (string.IsNullOrWhiteSpace(lineNo))
                {
                    reasonParts.Add("Line number is blank.");
                }
                else
                {
                    var lineMatches = catalog.Lines
                        .Where(line => NormalizeMatchName(line.LineName) == NormalizeMatchName(lineNo ?? ""))
                        .ToArray();
                    if (lineMatches.Length == 0)
                        reasonParts.Add($"Line '{lineNo}' is not an active line in this plant and FATP category.");
                    else if (lineMatches.Length > 1)
                        reasonParts.Add($"Line '{lineNo}' matches more than one active FATP line.");
                    else
                    {
                        lineId = lineMatches[0].Id;
                        mappedLineName = lineMatches[0].LineName;
                        if (string.IsNullOrWhiteSpace(sapLocation)
                            || NormalizeMatchName(lineMatches[0].SapLocation ?? "") != NormalizeMatchName(sapLocation!))
                        {
                            reasonParts.Add($"SAP location '{sapLocation ?? "(blank)"}' does not match line '{lineMatches[0].LineName}'.");
                        }
                    }
                }

                int? modelId = null;
                string? mappedModelName = null;
                if (string.IsNullOrWhiteSpace(modelName))
                {
                    reasonParts.Add("Model is blank.");
                }
                else
                {
                    var modelMatches = catalog.Models
                        .Where(model => NormalizeMatchName(model.Name) == NormalizeMatchName(modelName))
                        .ToArray();
                    if (modelMatches.Length == 0)
                        reasonParts.Add($"Model '{modelName}' is not active for the selected brand.");
                    else if (modelMatches.Length > 1)
                        reasonParts.Add($"Model '{modelName}' matches more than one active model for the selected brand.");
                    else
                    {
                        modelId = modelMatches[0].Id;
                        mappedModelName = modelMatches[0].Name;
                    }
                }

                if (string.IsNullOrWhiteSpace(rowCustomer))
                    reasonParts.Add("Customer is blank; confirm this row belongs to the selected brand before mapping.");

                var valueErrors = new List<string>();
                var values = ReadFATPValues(section, rowNumber, valueErrors);
                reasonParts.AddRange(valueErrors);

                var status = valueErrors.Count > 0
                    ? "invalid"
                    : reasonParts.Count > 0
                        ? "unmatched"
                        : "matched";

                rows.Add(new DailyReportExcelImportRow(
                    rowNumber,
                    lineNo ?? "",
                    sapLocation,
                    rowCustomer,
                    modelName,
                    status,
                    reasonParts.Count == 0 ? null : string.Join(" ", reasonParts),
                    lineId,
                    mappedLineName,
                    modelId,
                    mappedModelName,
                    values));
            }
        }

        var duplicateGroups = rows
            .Where(row => row.Status != "invalid")
            .Where(row => row.LineId.HasValue && row.ModelId.HasValue
                || !string.IsNullOrWhiteSpace(row.LineNo)
                && !string.IsNullOrWhiteSpace(row.ModelName))
            .GroupBy(row => row.LineId.HasValue && row.ModelId.HasValue
                ? $"ID:{row.LineId}:{row.ModelId}"
                : $"NAME:{NormalizeMatchName(row.LineNo)}:{NormalizeMatchName(row.ModelName ?? "")}")
            .Where(group => group.Count() > 1);
        foreach (var duplicateGroup in duplicateGroups)
        {
            var duplicateRows = duplicateGroup.Select(row => row.RowNumber).ToHashSet();
            for (var index = 0; index < rows.Count; index++)
            {
                if (duplicateRows.Contains(rows[index].RowNumber))
                {
                    rows[index] = rows[index] with
                    {
                        Status = "duplicate",
                        Reason = "More than one Excel row refers to the same line and model. Include at most one of these rows."
                    };
                }
            }
        }

        var mismatch = workbookDate.Date != selectedReportDate.Date;
        if (workbookDate.Date > DateTime.Today)
            return Failure(workbookDate, "The workbook report date is in the future and cannot be applied.");

        var matched = rows.Where(row => row.Status == "matched").ToArray();
        return new DailyReportExcelImportPreview(
            true,
            workbookDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            matched.Select(row => row.LineId!.Value).Distinct().Count(),
            matched.Length,
            mismatch
                ? [$"The workbook date is {workbookDate:yyyy-MM-dd}; the selected report date is {selectedReportDate:yyyy-MM-dd}. Confirm the workbook date before applying."]
                : [],
            matched.Select(row => new DailyReportExcelImportEntry(
                row.LineId!.Value,
                row.LineNo,
                row.SapLocation,
                row.ModelId!.Value,
                row.MappedModelName!,
                row.Form!)).ToArray(),
            rows);
    }

    private static DailyReportExcelImportValues? ReadFATPValues(
        DailySection section,
        int row,
        List<string> errors)
    {
        var columns = section.Columns;
        var decimals = new Dictionary<string, decimal?>();
        var decimalFields = new (string Name, int Column)[]
        {
            ("Month Plan", columns.MonthPlan),
            ("Month Actual", columns.MonthActual),
            ("Production Plan", columns.ProductionPlan),
            ("Production Actual", columns.ProductionActual),
            ("UPH Target", columns.UphTarget),
            ("UPH Actual", columns.UphActual),
            ("UPPH Installed", columns.CapacityTarget),
            ("UPPH Actual", columns.CapacityActual),
            ("OS&D reporting-date value", columns.OsdReportingDateValue),
            ("OS&D reporting-date percent", columns.OsdReportingDatePercent),
            ("OS&D MTD value", columns.OsdMtdValue),
            ("OS&D MTD percent", columns.OsdMtdPercent)
        };

        foreach (var (name, column) in decimalFields)
        {
            if (!TryReadFATPNumber(GetValueCell(section.Worksheet, row, column), out var value))
            {
                errors.Add($"'{name}' is not a valid number (Excel errors are not imported).");
                decimals[name] = null;
            }
            else
                decimals[name] = value;
        }

        var integers = new Dictionary<string, int?>();
        var integerFields = new (string Name, int Column)[]
        {
            ("Open WO quantity", columns.OpenWoQty),
            (">7 Days WO quantity", columns.Over7DaysWoQty),
            ("WIP Lying >3 Days", columns.WipLyingGreater3Days),
            ("WIP Lying >7 Days", columns.WipLyingGreater7Days)
        };
        foreach (var (name, column) in integerFields)
        {
            if (column <= 0)
                continue;

            if (!TryReadFATPNumber(GetValueCell(section.Worksheet, row, column), out var value)
                || value.HasValue
                && (decimal.Truncate(value.Value) != value.Value
                    || value.Value < int.MinValue
                    || value.Value > int.MaxValue))
            {
                errors.Add($"'{name}' is not a valid whole number (Excel errors are not imported).");
                integers[name] = null;
            }
            else
            {
                integers[name] = value.HasValue ? decimal.ToInt32(value.Value) : null;
            }
        }

        decimal? Get(string name) => decimals[name];
        int? GetInt(string name) => integers.GetValueOrDefault(name);
        return new DailyReportExcelImportValues(
            Get("Month Plan"),
            Get("Month Actual"),
            Get("Production Plan"),
            Get("Production Actual"),
            Get("UPH Target"),
            Get("UPH Actual"),
            Get("UPPH Installed"),
            Get("UPPH Actual"),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Get("OS&D reporting-date value"),
            Get("OS&D reporting-date percent"),
            Get("OS&D MTD value"),
            Get("OS&D MTD percent"),
            null,
            null,
            null,
            GetInt("Open WO quantity"),
            GetInt(">7 Days WO quantity"),
            GetInt("WIP Lying >3 Days"),
            GetInt("WIP Lying >7 Days"),
            null,
            null,
            null,
            null,
            ReadText(section.Worksheet, row, columns.IssueDescription),
            GetPresentFields(columns, section.Metric));
    }

    private static DailyReportExcelImportPreview PreviewQuality(
        IReadOnlyList<IXLWorksheet> worksheets,
        DateTime selectedReportDate,
        string customerName,
        ImportCatalog catalog)
    {
        var rows = new List<DailyReportExcelImportRow>();
        var errors = new List<string>();
        DateTime? workbookDate = null;

        foreach (var worksheet in worksheets)
        {
            workbookDate ??= ReadReportDate(worksheet);
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (var headerRow = 1; headerRow <= lastRow; headerRow++)
            {
                var lineColumn = FindColumn(worksheet, headerRow, lastColumn, "Line No.");
                var sapColumn = FindColumn(worksheet, headerRow, lastColumn, "SAP Location");
                var customerColumn = FindColumn(worksheet, headerRow, lastColumn, "Customer");
                var modelColumn = FindColumn(worksheet, headerRow, lastColumn, "Model");
                if (lineColumn == 0 || sapColumn == 0 || customerColumn == 0 || modelColumn == 0)
                    continue;

                var qualityColumns = BuildQualityColumns(
                    worksheet,
                    headerRow,
                    lastColumn);
                var missing = qualityColumns.Metrics
                    .Where(metric => metric.Target == 0 || metric.Actual == 0)
                    .Select(metric => $"{metric.Label} Target/Actual")
                    .ToArray();
                if (missing.Length > 0)
                {
                    errors.Add($"{worksheet.Name}: missing quality columns {string.Join(", ", missing)}.");
                    continue;
                }

                var dataStart = headerRow + 1;
                while (dataStart <= lastRow
                    && string.IsNullOrWhiteSpace(ReadText(worksheet, dataStart, lineColumn)))
                    dataStart++;
                var nextHeader = Enumerable.Range(dataStart, Math.Max(0, lastRow - dataStart + 1))
                    .FirstOrDefault(candidate =>
                        candidate > dataStart
                        && FindColumn(worksheet, candidate, lastColumn, "Line No.") > 0);
                var lastDataRow = nextHeader > 0 ? nextHeader - 1 : lastRow;

                for (var rowNumber = dataStart; rowNumber <= lastDataRow; rowNumber++)
                {
                    var lineNo = ReadText(worksheet, rowNumber, lineColumn);
                    if (string.IsNullOrWhiteSpace(lineNo))
                        continue;
                    if (string.Equals(lineNo, "Total", StringComparison.OrdinalIgnoreCase))
                        break;

                    var sourceCustomer = ReadText(worksheet, rowNumber, customerColumn);
                    if (!string.IsNullOrWhiteSpace(sourceCustomer)
                        && NormalizeMatchName(sourceCustomer) != NormalizeMatchName(customerName))
                        continue;

                    var sapLocation = ReadText(worksheet, rowNumber, sapColumn);
                    var modelName = ReadText(worksheet, rowNumber, modelColumn);
                    var reasonParts = new List<string>();
                    var lineMatches = catalog.Lines
                        .Where(line => NormalizeMatchName(line.LineName) == NormalizeMatchName(lineNo))
                        .Where(line => NormalizeMatchName(line.SapLocation ?? "") == NormalizeMatchName(sapLocation ?? ""))
                        .ToArray();
                    var modelMatches = catalog.Models
                        .Where(model => NormalizeMatchName(model.Name) == NormalizeMatchName(modelName ?? ""))
                        .ToArray();

                    int? lineId = lineMatches.Length == 1 ? lineMatches[0].Id : null;
                    string? mappedLine = lineMatches.Length == 1 ? lineMatches[0].LineName : null;
                    int? modelId = modelMatches.Length == 1 ? modelMatches[0].Id : null;
                    string? mappedModel = modelMatches.Length == 1 ? modelMatches[0].Name : null;

                    if (lineId is null)
                        reasonParts.Add(lineMatches.Length == 0
                            ? $"Line '{lineNo}' and SAP location '{sapLocation ?? "(blank)"}' do not match an active line."
                            : $"Line '{lineNo}' and SAP location '{sapLocation}' are ambiguous.");
                    if (modelId is null)
                        reasonParts.Add(modelMatches.Length == 0
                            ? $"Model '{modelName ?? "(blank)"}' is not active for this brand."
                            : $"Model '{modelName}' is ambiguous for this brand.");
                    if (string.IsNullOrWhiteSpace(sourceCustomer))
                        reasonParts.Add("Customer is blank.");

                    var valueErrors = new List<string>();
                    var form = ReadQualityValues(worksheet, rowNumber, qualityColumns, valueErrors);
                    reasonParts.AddRange(valueErrors);
                    var status = valueErrors.Count > 0
                        ? "invalid"
                        : reasonParts.Count > 0
                            ? "unmatched"
                            : "matched";
                    rows.Add(new DailyReportExcelImportRow(
                        rowNumber,
                        lineNo,
                        sapLocation,
                        sourceCustomer,
                        modelName,
                        status,
                        reasonParts.Count == 0 ? null : string.Join(" ", reasonParts),
                        lineId,
                        mappedLine,
                        modelId,
                        mappedModel,
                        form));
                }

                headerRow = lastDataRow;
            }
        }

        if (rows.Count == 0)
            return Failure(selectedReportDate, errors.Count > 0
                ? string.Join(" ", errors)
                : "No matching Quality rows were found in the workbook.");

        var duplicateKeys = rows
            .Where(row => row.LineId.HasValue && row.ModelId.HasValue)
            .GroupBy(row => (row.LineId, row.ModelId))
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(row => row.RowNumber))
            .ToHashSet();
        for (var index = 0; index < rows.Count; index++)
        {
            if (duplicateKeys.Contains(rows[index].RowNumber))
                rows[index] = rows[index] with { Status = "duplicate", Reason = "Multiple Quality rows match the same line and model." };
        }

        var date = workbookDate ?? selectedReportDate;
        return new DailyReportExcelImportPreview(
            true,
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            rows.Where(row => row.LineId.HasValue).Select(row => row.LineId!.Value).Distinct().Count(),
            rows.Where(row => row.ModelId.HasValue).Select(row => row.ModelId!.Value).Distinct().Count(),
            errors,
            [],
            rows,
            ImportType: "quality");
    }

    private static QualityExcelColumns BuildQualityColumns(
        IXLWorksheet worksheet,
        int headerRow,
        int lastColumn)
    {
        var metrics = new[]
        {
            new QualityExcelMetric("Int-VI", "fpyIntVi", ["INTVI", "INTERNALVI"]),
            new QualityExcelMetric("PT", "fpyPt", ["PT"]),
            new QualityExcelMetric("RI", "fpyRi", ["RI"]),
            new QualityExcelMetric("RI Image", "fpyRiImage", ["RIIMAGE"]),
            new QualityExcelMetric("Packing VI", "fpyPackingVi", ["PACKINGVI"]),
            new QualityExcelMetric("RTY (%)", "rty", ["RTY"])
        };

        int Resolve(string field, string subcolumn)
        {
            for (var column = 1; column <= lastColumn; column++)
            {
                var labels = Enumerable.Range(headerRow, Math.Min(3, lastRow(worksheet) - headerRow + 1))
                    .Select(row => Normalize(GetValueCell(worksheet, row, column).GetString()))
                    .Where(value => value.Length > 0)
                    .ToArray();
                var labelPath = string.Join("|", labels);
                var fieldMatch = field == "rty"
                    ? labelPath.Contains("RTY", StringComparison.Ordinal)
                    : labelPath.Contains("FPY", StringComparison.Ordinal)
                        && metrics.First(metric => metric.Field == field).Aliases
                            .Any(alias => labelPath.Contains(alias, StringComparison.Ordinal));
                if (fieldMatch && labels.Contains(Normalize(subcolumn)))
                    return column;
            }

            return 0;
        }

        return new QualityExcelColumns(metrics.Select(metric => metric with
        {
            Target = Resolve(metric.Field, "Target"),
            Actual = Resolve(metric.Field, "Actual")
        }).ToArray());

        static int lastRow(IXLWorksheet sheet) => sheet.LastRowUsed()?.RowNumber() ?? 0;
    }

    private static DailyReportExcelImportValues ReadQualityValues(
        IXLWorksheet worksheet,
        int row,
        QualityExcelColumns columns,
        List<string> errors)
    {
        var values = new Dictionary<string, decimal?>(StringComparer.Ordinal);
        foreach (var metric in columns.Metrics)
        {
            foreach (var (suffix, column) in new[] { ("Target", metric.Target), ("Actual", metric.Actual) })
            {
                var field = $"{metric.Field}{suffix}";
                if (!TryReadNumber(GetValueCell(worksheet, row, column), out var value))
                {
                    errors.Add($"{metric.Label} {suffix} is not a valid number.");
                    continue;
                }
                values[field] = value;
            }
        }

        decimal? Value(string field) => values.GetValueOrDefault(field);
        var fields = columns.Metrics
            .SelectMany(metric => new[] { $"{metric.Field}Target", $"{metric.Field}Actual" })
            .ToArray();
        return new DailyReportExcelImportValues(
            null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, Value("rtyTarget"), Value("rtyActual"), null, null, null, null,
            null, null, null, null, null, null, null, null, null, null,
            null, null, fields,
            FpyIntViTarget: Value("fpyIntViTarget"),
            FpyIntViActual: Value("fpyIntViActual"),
            FpyPtTarget: Value("fpyPtTarget"),
            FpyPtActual: Value("fpyPtActual"),
            FpyRiTarget: Value("fpyRiTarget"),
            FpyRiActual: Value("fpyRiActual"),
            FpyRiImageTarget: Value("fpyRiImageTarget"),
            FpyRiImageActual: Value("fpyRiImageActual"),
            FpyPackingViTarget: Value("fpyPackingViTarget"),
            FpyPackingViActual: Value("fpyPackingViActual"));
    }

    private static DailyReportExcelImportPreview PreviewWoClosure(
        IReadOnlyList<IXLWorksheet> worksheets,
        DateTime selectedReportDate,
        string customerName,
        string categoryName)
    {
        var worksheet = worksheets.FirstOrDefault(sheet =>
            string.Equals(sheet.Name.Trim(), "Summary", StringComparison.OrdinalIgnoreCase));
        if (worksheet is null)
            return Failure(selectedReportDate, "WO Closure workbook is missing the Summary sheet.");

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var headerRow = Enumerable.Range(1, lastRow)
            .FirstOrDefault(row =>
                FindAnyColumn(worksheet, row, lastColumn, "Customer / Process", "Customer/Process") > 0
                && FindAnyColumn(worksheet, row, lastColumn, "Metric") > 0);
        if (headerRow == 0)
            return Failure(selectedReportDate, "WO Closure Summary is missing Customer / Process and Metric headers.");

        var processColumn = FindAnyColumn(worksheet, headerRow, lastColumn, "Customer / Process", "Customer/Process");
        var metricColumn = FindAnyColumn(worksheet, headerRow, lastColumn, "Metric");
        var columns = new WoClosureColumns(
            FindAnyColumn(worksheet, headerRow, lastColumn, "0-3 days", "0–3 days", "0 to 3 days"),
            FindAnyColumn(worksheet, headerRow, lastColumn, "4-7 days", "4–7 days", "4 to 7 days"),
            FindAnyColumn(worksheet, headerRow, lastColumn, ">7 days", "greater than 7 days", "over 7 days"),
            FindAnyColumn(worksheet, headerRow, lastColumn, "TTL", "Total"),
            FindAnyColumn(worksheet, headerRow, lastColumn, "Clearance Plan of >7 Days", "Clearance Plan"),
            FindAnyColumn(worksheet, headerRow, lastColumn, "Resp.", "Responsible"),
            FindAnyColumn(worksheet, headerRow, lastColumn, "Timeline"));
        if (columns.Bucket0To3 == 0 || columns.Bucket4To7 == 0 || columns.BucketGreater7 == 0 || columns.Total == 0)
            return Failure(selectedReportDate, "WO Closure Summary is missing one or more aging bucket columns.");

        var countRow = 0;
        var qtyRow = 0;
        for (var row = headerRow + 1; row <= lastRow; row++)
        {
            var process = ReadText(worksheet, row, processColumn);
            if (!MatchesCustomerProcess(process, customerName, categoryName))
                continue;
            var metric = Normalize(ReadText(worksheet, row, metricColumn) ?? "");
            if (metric == "COUNT")
                countRow = row;
            else if (metric == "QTY" || metric == "QUANTITY")
                qtyRow = row;
        }

        if (countRow == 0 && qtyRow == 0)
            return Failure(selectedReportDate, $"WO Closure Summary has no section for {customerName} - {categoryName}.");

        var errors = new List<string>();
        int? ReadInteger(int row, int column, string label)
        {
            if (row == 0 || column == 0)
                return null;
            if (!TryReadNumber(GetValueCell(worksheet, row, column), out var value)
                || value.HasValue && (decimal.Truncate(value.Value) != value.Value
                    || value.Value < int.MinValue || value.Value > int.MaxValue))
            {
                errors.Add($"WO Closure {label} is not a valid whole number.");
                return null;
            }
            return value.HasValue ? decimal.ToInt32(value.Value) : null;
        }

        var form = new DailyReportExcelImportValues(
            null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null, null,
            null, null, [],
            WoCount0To3Days: ReadInteger(countRow, columns.Bucket0To3, "Count 0-3"),
            WoCount4To7Days: ReadInteger(countRow, columns.Bucket4To7, "Count 4-7"),
            WoCountGreater7Days: ReadInteger(countRow, columns.BucketGreater7, "Count >7"),
            WoCountTotal: ReadInteger(countRow, columns.Total, "Count total"),
            WoQty0To3Days: ReadInteger(qtyRow, columns.Bucket0To3, "Qty 0-3"),
            WoQty4To7Days: ReadInteger(qtyRow, columns.Bucket4To7, "Qty 4-7"),
            WoQtyGreater7Days: ReadInteger(qtyRow, columns.BucketGreater7, "Qty >7"),
            WoQtyTotal: ReadInteger(qtyRow, columns.Total, "Qty total"),
            WoClearancePlanGreater7Days: ReadInteger(qtyRow > 0 ? qtyRow : countRow, columns.ClearancePlan, "clearance plan"),
            WoResponsible: ReadText(worksheet, qtyRow > 0 ? qtyRow : countRow, columns.Responsible),
            WoTimeline: ReadText(worksheet, qtyRow > 0 ? qtyRow : countRow, columns.Timeline));

        return new DailyReportExcelImportPreview(
            true,
            (ReadReportDate(worksheet) ?? selectedReportDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            0,
            0,
            errors,
            [],
            [],
            ImportType: "woClosure",
            SharedForm: form);
    }

    private static bool MatchesCustomerProcess(
        string? value,
        string customerName,
        string categoryName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var parts = value.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            && NormalizeMatchName(parts[0]) == NormalizeMatchName(customerName)
            && NormalizeMatchName(parts[^1]) == NormalizeMatchName(categoryName);
    }

    private static int FindAnyColumn(
        IXLWorksheet worksheet,
        int row,
        int lastColumn,
        params string[] labels)
    {
        foreach (var label in labels)
        {
            var column = FindColumn(worksheet, row, lastColumn, label);
            if (column > 0)
                return column;
        }
        return 0;
    }

    private static IReadOnlyList<DailySection> FindSections(IXLWorksheet worksheet)
    {
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var sections = new List<DailySection>();

        for (var row = 1; row <= lastRow; row++)
        {
            var lineNoColumn = FindColumn(worksheet, row, lastColumn, "Line No.");
            var sapColumn = FindColumn(worksheet, row, lastColumn, "SAP Location");
            var customerColumn = FindColumn(worksheet, row, lastColumn, "Customer");
            var modelColumn = FindColumn(worksheet, row, lastColumn, "Model");
            var metrics = new[] { "UPPH", "CPH" }
                .Where(metric => FindColumn(worksheet, row, lastColumn, metric) > 0)
                .ToArray();

            if (lineNoColumn == 0
                || sapColumn == 0
                || customerColumn == 0
                || modelColumn == 0
                || metrics.Length == 0
                || row >= lastRow)
                continue;

            var nextHeader = Enumerable.Range(row + 1, lastRow - row)
                .FirstOrDefault(candidate =>
                    FindColumn(worksheet, candidate, lastColumn, "Line No.") > 0);
            var lastDataRow = nextHeader > 0 ? nextHeader - 1 : lastRow;

            foreach (var metric in metrics)
            {
                var columns = BuildColumns(
                    worksheet,
                    row,
                    row + 1,
                    lastColumn,
                    metric,
                    lineNoColumn,
                    sapColumn,
                    customerColumn,
                    modelColumn);
                var otherMetric = metric == "UPPH" ? "CPH" : "UPPH";
                var otherCapacityTarget = FindCapacityTarget(
                    worksheet,
                    row + 1,
                    FindColumn(worksheet, row, lastColumn, otherMetric),
                    otherMetric);

                var otherCapacityActual = FindSubColumn(
                    worksheet,
                    row + 1,
                    FindColumn(worksheet, row, lastColumn, otherMetric),
                    "Actual");

                sections.Add(new DailySection(
                    worksheet,
                    row,
                    lastDataRow,
                    metric,
                    columns,
                    otherCapacityTarget,
                    otherCapacityActual));
            }
        }

        return sections;
    }

    private static DailyReportExcelColumns BuildColumns(
        IXLWorksheet worksheet,
        int headerRow,
        int subHeaderRow,
        int lastColumn,
        string metric,
        int lineNo,
        int sapLocation,
        int customer,
        int model)
    {
        int Group(string label) => FindColumn(worksheet, headerRow, lastColumn, label);
        int FirstGroup(params string[] labels)
        {
            foreach (var label in labels)
            {
                var column = Group(label);
                if (column > 0)
                    return column;
            }

            return 0;
        }
        int WipColumn(int days)
        {
            var standaloneColumn = days == 3
                ? FirstGroup(
                    "WIP Lying >3 Days (Qty)",
                    "WIP Lying >3 Days",
                    "WIP >3 Days (Qty)",
                    "WIP >3 Days",
                    "WIP > 3 Days (Qty)",
                    "WIP > 3 Days")
                : FirstGroup(
                    "WIP Lying >7 Days (Qty)",
                    "WIP Lying >7 Days",
                    "WIP >7 Days (Qty)",
                    "WIP >7 Days",
                    "WIP > 7 Days (Qty)",
                    "WIP > 7 Days");
            if (standaloneColumn > 0)
                return standaloneColumn;

            var ageLabel = $">{days} Days";
            foreach (var group in new[] { "WIP Lying", "WIP" })
            {
                var column = FindSubColumn(
                    worksheet,
                    subHeaderRow,
                    Group(group),
                    ageLabel);
                if (column > 0)
                    return column;
            }

            return 0;
        }
        int Pair(string group, string sub) =>
            FindSubColumn(worksheet, subHeaderRow, Group(group), sub);
        int CapacityTarget() =>
            FindCapacityTarget(worksheet, subHeaderRow, Group(metric), metric);

        return new DailyReportExcelColumns(
            lineNo,
            sapLocation,
            customer,
            model,
            Pair("Month Plan", "Plan"),
            Pair("Month Plan", "Actual"),
            Pair("Production", "Plan"),
            Pair("Production", "Actual"),
            Pair("UPH", "Target"),
            Pair("UPH", "Actual"),
            CapacityTarget(),
            Pair(metric, "Actual"),
            Pair("FPY", "Target"),
            Pair("FPY", "Actual"),
            Pair("FTY", "Target"),
            Pair("FTY", "Actual"),
            Pair("RTY", "Target"),
            Pair("RTY", "Actual"),
            Pair("OS&D (Reporting Date)", "Value (INR)"),
            Pair("OS&D (Reporting Date)", "%"),
            Pair("OS&D MTD", "Value (INR)"),
            Pair("OS&D MTD", "%"),
            Pair("Actual OT (Manhours)", "Planned"),
            Pair("Actual OT (Manhours)", "Unplanned"),
            Group("Open WO (Qty)"),
            Group(">7 Days WO (Qty)"),
            WipColumn(3),
            WipColumn(7),
            Group("Daily TRC In (Qty)"),
            Group("Daily TRC Out (Qty)"),
            Group("TRC Overall Failure Inflow %"),
            Group("TRC Lying >3 Days (Cr)"),
            Group("Issue description"));
    }

    private static DailyReportExcelImportValues? ReadValues(
        DailySection section,
        int row,
        string rowLabel,
        List<string> warnings)
    {
        var columns = section.Columns;
        var values = new Dictionary<string, decimal?>();
        var skippedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var decimalColumns = new (string Name, string Field, int Column)[]
        {
            ("Month Plan", "monthPlan", columns.MonthPlan),
            ("Month Actual", "monthActual", columns.MonthActual),
            ("Production Plan", "productionPlan", columns.ProductionPlan),
            ("Production Actual", "productionActual", columns.ProductionActual),
            ("UPH Target", "uphTarget", columns.UphTarget),
            ("UPH Actual", "uphActual", columns.UphActual),
            (section.Metric == "UPPH" ? "UPPH Installed" : "CPH Target",
                section.Metric == "UPPH" ? "upphInstalled" : "cphTarget",
                columns.CapacityTarget),
            (section.Metric == "UPPH" ? "UPPH Actual" : "CPH Actual",
                section.Metric == "UPPH" ? "upphActual" : "cphActual",
                columns.CapacityActual),
            ("FPY Target", "fpyTarget", columns.FpyTarget),
            ("FPY Actual", "fpyActual", columns.FpyActual),
            ("FTY Target", "ftyTarget", columns.FtyTarget),
            ("FTY Actual", "ftyActual", columns.FtyActual),
            ("RTY Target", "rtyTarget", columns.RtyTarget),
            ("RTY Actual", "rtyActual", columns.RtyActual),
            ("OS&D Reporting Date", "osdReportingDateValue", columns.OsdReportingDateValue),
            ("OS&D Reporting Date %", "osdReportingDatePercent", columns.OsdReportingDatePercent),
            ("OS&D MTD", "osdMtdValue", columns.OsdMtdValue),
            ("OS&D MTD %", "osdMtdPercent", columns.OsdMtdPercent),
            ("Planned OT", "plannedOTManhours", columns.PlannedOt),
            ("Unplanned OT", "unplannedOTManhours", columns.UnplannedOt),
            ("TRC Overall Failure Inflow %", "trcOverallFailureInflowPercent", columns.TrcOverallFailureInflowPercent),
            ("TRC Lying >3 Days (Cr)", "trcLyingOver3DaysCr", columns.TrcLyingOver3DaysCr)
        };

        foreach (var (name, field, column) in decimalColumns)
        {
            if (column <= 0)
                continue;

            if (!TryReadNumber(GetValueCell(section.Worksheet, row, column), out var value))
            {
                skippedFields.Add(field);
                warnings.Add($"{rowLabel}: '{name}' was skipped because it does not contain a valid number.");
                continue;
            }

            values[name] = value;
        }

        var integerColumns = new (string Name, string Field, int Column)[]
        {
            ("Open WO (Qty)", "openWOQty", columns.OpenWoQty),
            (">7 Days WO (Qty)", "over7DaysWOBalanceQty", columns.Over7DaysWoQty),
            ("WIP Lying >3 Days", "wipLyingGreater3Days", columns.WipLyingGreater3Days),
            ("WIP Lying >7 Days", "wipLyingGreater7Days", columns.WipLyingGreater7Days),
            ("Daily TRC In (Qty)", "dailyTRCInQty", columns.DailyTrcInQty),
            ("Daily TRC Out (Qty)", "dailyTRCOutQty", columns.DailyTrcOutQty)
        };

        var integers = new Dictionary<string, int?>();
        foreach (var (name, field, column) in integerColumns)
        {
            if (column <= 0)
                continue;

            if (!TryReadNumber(GetValueCell(section.Worksheet, row, column), out var value))
            {
                skippedFields.Add(field);
                warnings.Add($"{rowLabel}: '{name}' was skipped because it does not contain a valid whole number.");
                continue;
            }

            if (value.HasValue
                && (decimal.Truncate(value.Value) != value.Value
                    || value.Value < int.MinValue
                    || value.Value > int.MaxValue))
            {
                skippedFields.Add(field);
                warnings.Add($"{rowLabel}: '{name}' was skipped because it does not contain a valid whole number.");
                continue;
            }

            integers[name] = value.HasValue ? decimal.ToInt32(value.Value) : null;
        }

        decimal? Get(string name) => values.GetValueOrDefault(name);
        int? GetInt(string name) => integers.GetValueOrDefault(name);
        var plannedOt = Get("Planned OT");
        var unplannedOt = Get("Unplanned OT");
        decimal? actualOt = plannedOt.HasValue || unplannedOt.HasValue
            ? (plannedOt ?? 0) + (unplannedOt ?? 0)
            : null;

        return new DailyReportExcelImportValues(
            Get("Month Plan"),
            Get("Month Actual"),
            Get("Production Plan"),
            Get("Production Actual"),
            Get("UPH Target"),
            Get("UPH Actual"),
            section.Metric == "UPPH" ? Get("UPPH Installed") : null,
            section.Metric == "UPPH" ? Get("UPPH Actual") : null,
            section.Metric == "CPH" ? Get("CPH Target") : null,
            section.Metric == "CPH" ? Get("CPH Actual") : null,
            Get("FPY Target"),
            Get("FPY Actual"),
            Get("FTY Target"),
            Get("FTY Actual"),
            Get("RTY Target"),
            Get("RTY Actual"),
            Get("OS&D Reporting Date"),
            Get("OS&D Reporting Date %"),
            Get("OS&D MTD"),
            Get("OS&D MTD %"),
            plannedOt,
            unplannedOt,
            actualOt,
            GetInt("Open WO (Qty)"),
            GetInt(">7 Days WO (Qty)"),
            GetInt("WIP Lying >3 Days"),
            GetInt("WIP Lying >7 Days"),
            GetInt("Daily TRC In (Qty)"),
            GetInt("Daily TRC Out (Qty)"),
            Get("TRC Overall Failure Inflow %"),
            Get("TRC Lying >3 Days (Cr)"),
            ReadText(section.Worksheet, row, columns.IssueDescription),
            GetPresentFields(columns, section.Metric)
                .Where(field => !skippedFields.Contains(field))
                .Where(field => field != "actualOTManhours"
                    || values.ContainsKey("Planned OT")
                    || values.ContainsKey("Unplanned OT"))
                .ToArray());
    }

    private static bool TryReadNumber(IXLCell cell, out decimal? value)
    {
        value = null;
        if (cell.IsEmpty())
            return true;

        if (cell.TryGetValue<decimal>(out var number))
        {
            value = number;
            return true;
        }

        var text = cell.GetString().Trim();
        if (EmptyNumericValues.Contains(text))
            return true;

        if (!decimal.TryParse(
                text,
                NumberStyles.Number | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out number))
        {
            return false;
        }

        value = number;
        return true;
    }

    private static bool TryReadFATPNumber(IXLCell cell, out decimal? value)
    {
        if (string.Equals(cell.GetString().Trim(), "No Plan", StringComparison.OrdinalIgnoreCase))
        {
            value = null;
            return true;
        }

        return TryReadNumber(cell, out value);
    }

    private static DateTime? ReadReportDate(IXLWorksheet worksheet)
    {
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var row = 1; row <= Math.Min(lastRow, 10); row++)
        {
            for (var column = 1; column < lastColumn; column++)
            {
                var label = Normalize(worksheet.Cell(row, column).GetString());
                if (label != "REPORTDATE")
                    continue;

                var dateCell = worksheet.Cell(row, column + 1);
                if (dateCell.TryGetValue<DateTime>(out var date))
                    return date.Date;

                if (dateCell.TryGetValue<double>(out var serial)
                    && serial >= 1
                    && serial <= 2958465)
                {
                    return DateTime.FromOADate(serial).Date;
                }

                if (DateTime.TryParse(
                        dateCell.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces,
                        out date))
                {
                    return date.Date;
                }

                return null;
            }
        }

        return null;
    }

    private static string? ReadText(
        IXLWorksheet worksheet,
        int row,
        int column)
    {
        if (column <= 0)
            return null;
        var value = GetValueCell(worksheet, row, column).GetString().Trim();
        return value.Length == 0 ? null : value;
    }

    private static string? ReadSourceText(
        IXLWorksheet worksheet,
        int row,
        int column)
    {
        if (column <= 0)
            return null;
        var value = GetValueCell(worksheet, row, column).GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IXLCell GetValueCell(
        IXLWorksheet worksheet,
        int row,
        int column)
    {
        if (column <= 0)
            return worksheet.Cell(row, Math.Max(column, 1));

        foreach (var mergedRange in worksheet.MergedRanges)
        {
            var first = mergedRange.RangeAddress.FirstAddress;
            var last = mergedRange.RangeAddress.LastAddress;
            if (row >= first.RowNumber
                && row <= last.RowNumber
                && column >= first.ColumnNumber
                && column <= last.ColumnNumber)
            {
                return worksheet.Cell(first.RowNumber, first.ColumnNumber);
            }
        }

        return worksheet.Cell(row, column);
    }

    private static int FindColumn(
        IXLWorksheet worksheet,
        int row,
        int lastColumn,
        string label)
    {
        var expected = Normalize(label);
        for (var column = 1; column <= lastColumn; column++)
        {
            if (Normalize(worksheet.Cell(row, column).GetString()) == expected)
                return column;
        }

        return 0;
    }

    private static int FindSubColumn(
        IXLWorksheet worksheet,
        int row,
        int groupColumn,
        string label)
    {
        if (groupColumn <= 0)
            return 0;

        var expected = Normalize(label);
        for (var column = groupColumn; column <= groupColumn + 1; column++)
        {
            if (Normalize(worksheet.Cell(row, column).GetString()) == expected)
                return column;
        }

        return 0;
    }

    private static int FindCapacityTarget(
        IXLWorksheet worksheet,
        int subHeaderRow,
        int groupColumn,
        string metric)
    {
        var labels = metric == "UPPH"
            ? new[] { "Installed", "Ins", "Target" }
            : new[] { "Target", "Installed", "Ins" };
        foreach (var label in labels)
        {
            var column = FindSubColumn(worksheet, subHeaderRow, groupColumn, label);
            if (column > 0)
                return column;
        }

        return 0;
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static string NormalizeMatchName(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    private static bool HasCapacityValue(DailySection section, int row)
    {
        var target = section.Columns.CapacityTarget;
        var actual = section.Columns.CapacityActual;
        return (target > 0 && !GetValueCell(section.Worksheet, row, target).IsEmpty())
            || (actual > 0 && !GetValueCell(section.Worksheet, row, actual).IsEmpty());
    }

    private static bool HasNumericValue(
        IXLWorksheet worksheet,
        int row,
        int column)
    {
        if (column <= 0)
            return false;

        return TryReadNumber(GetValueCell(worksheet, row, column), out var value)
            && value.HasValue;
    }

    private static bool HasOtherCapacityValue(DailySection section, int row)
    {
        return (section.OtherCapacityTarget > 0
                && !GetValueCell(section.Worksheet, row, section.OtherCapacityTarget).IsEmpty())
            || (section.OtherCapacityActual > 0
                && !GetValueCell(section.Worksheet, row, section.OtherCapacityActual).IsEmpty());
    }

    private static IReadOnlyList<string> GetPresentFields(
        DailyReportExcelColumns columns,
        string metric)
    {
        var fields = new List<string>();
        void Add(string field, int column)
        {
            if (column > 0)
                fields.Add(field);
        }

        Add("monthPlan", columns.MonthPlan);
        Add("monthActual", columns.MonthActual);
        Add("productionPlan", columns.ProductionPlan);
        Add("productionActual", columns.ProductionActual);
        Add("uphTarget", columns.UphTarget);
        Add("uphActual", columns.UphActual);
        Add(metric == "UPPH" ? "upphInstalled" : "cphTarget", columns.CapacityTarget);
        Add(metric == "UPPH" ? "upphActual" : "cphActual", columns.CapacityActual);
        Add("fpyTarget", columns.FpyTarget);
        Add("fpyActual", columns.FpyActual);
        Add("ftyTarget", columns.FtyTarget);
        Add("ftyActual", columns.FtyActual);
        Add("rtyTarget", columns.RtyTarget);
        Add("rtyActual", columns.RtyActual);
        Add("osdReportingDateValue", columns.OsdReportingDateValue);
        Add("osdReportingDatePercent", columns.OsdReportingDatePercent);
        Add("osdMtdValue", columns.OsdMtdValue);
        Add("osdMtdPercent", columns.OsdMtdPercent);
        Add("plannedOTManhours", columns.PlannedOt);
        Add("unplannedOTManhours", columns.UnplannedOt);
        Add("actualOTManhours", columns.PlannedOt > 0 || columns.UnplannedOt > 0 ? 1 : 0);
        Add("openWOQty", columns.OpenWoQty);
        Add("over7DaysWOBalanceQty", columns.Over7DaysWoQty);
        Add("wipLyingGreater3Days", columns.WipLyingGreater3Days);
        Add("wipLyingGreater7Days", columns.WipLyingGreater7Days);
        Add("dailyTRCInQty", columns.DailyTrcInQty);
        Add("dailyTRCOutQty", columns.DailyTrcOutQty);
        Add("trcOverallFailureInflowPercent", columns.TrcOverallFailureInflowPercent);
        Add("trcLyingOver3DaysCr", columns.TrcLyingOver3DaysCr);
        Add("issueDescription", columns.IssueDescription);
        return fields;
    }

    private static DailyReportExcelImportPreview Failure(
        DateTime reportDate,
        string error) =>
        new(
            false,
            reportDate == default
                ? null
                : reportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            0,
            0,
            [error],
            []);

    private sealed record CatalogLine(int Id, string LineName, string? SapLocation);
    private sealed record CatalogModel(int Id, string Name);
    private sealed record ImportCatalog(
        IReadOnlyList<CatalogLine> Lines,
        IReadOnlyList<CatalogModel> Models,
        IReadOnlyList<CatalogOtherLine> OtherCategoryLines);
    private sealed record CatalogOtherLine(
        string LineName,
        string? SapLocation,
        string CategoryName);
    private sealed record DailySection(
        IXLWorksheet Worksheet,
        int HeaderRow,
        int LastDataRow,
        string Metric,
        DailyReportExcelColumns Columns,
        int OtherCapacityTarget,
        int OtherCapacityActual);
}

public sealed record DailyReportExcelColumns(
    int LineNo,
    int SapLocation,
    int Customer,
    int Model,
    int MonthPlan,
    int MonthActual,
    int ProductionPlan,
    int ProductionActual,
    int UphTarget,
    int UphActual,
    int CapacityTarget,
    int CapacityActual,
    int FpyTarget,
    int FpyActual,
    int FtyTarget,
    int FtyActual,
    int RtyTarget,
    int RtyActual,
    int OsdReportingDateValue,
    int OsdReportingDatePercent,
    int OsdMtdValue,
    int OsdMtdPercent,
    int PlannedOt,
    int UnplannedOt,
    int OpenWoQty,
    int Over7DaysWoQty,
    int WipLyingGreater3Days,
    int WipLyingGreater7Days,
    int DailyTrcInQty,
    int DailyTrcOutQty,
    int TrcOverallFailureInflowPercent,
    int TrcLyingOver3DaysCr,
    int IssueDescription);

public sealed record DailyReportExcelImportPreview(
    bool Valid,
    string? ReportDate,
    int Lines,
    int Models,
    IReadOnlyList<string> Errors,
    IReadOnlyList<DailyReportExcelImportEntry> Data,
    IReadOnlyList<DailyReportExcelImportRow>? Rows = null,
    string? ImportType = null,
    DailyReportExcelImportValues? SharedForm = null);

public sealed record DailyReportExcelImportRow(
    int RowNumber,
    string LineNo,
    string? SapLocation,
    string? Customer,
    string? ModelName,
    string Status,
    string? Reason,
    int? LineId,
    string? MappedLineName,
    int? ModelId,
    string? MappedModelName,
    DailyReportExcelImportValues? Form);

public sealed record DailyReportExcelImportEntry(
    int LineId,
    string LineNo,
    string? SapLocation,
    int ModelId,
    string ModelName,
    DailyReportExcelImportValues Form);

public sealed record DailyReportExcelImportValues(
    decimal? MonthPlan,
    decimal? MonthActual,
    decimal? ProductionPlan,
    decimal? ProductionActual,
    decimal? UphTarget,
    decimal? UphActual,
    decimal? UpphInstalled,
    decimal? UpphActual,
    decimal? CphTarget,
    decimal? CphActual,
    decimal? FpyTarget,
    decimal? FpyActual,
    decimal? FtyTarget,
    decimal? FtyActual,
    decimal? RtyTarget,
    decimal? RtyActual,
    decimal? OsdReportingDateValue,
    decimal? OsdReportingDatePercent,
    decimal? OsdMtdValue,
    decimal? OsdMtdPercent,
    decimal? PlannedOTManhours,
    decimal? UnplannedOTManhours,
    decimal? ActualOTManhours,
    int? OpenWOQty,
    int? Over7DaysWOBalanceQty,
    int? WipLyingGreater3Days,
    int? WipLyingGreater7Days,
    int? DailyTRCInQty,
    int? DailyTRCOutQty,
    decimal? TRCOverallFailureInflowPercent,
    decimal? TRCLyingOver3DaysCr,
    string? IssueDescription,
    IReadOnlyList<string> PresentFields,
    decimal? FpyIntViTarget = null,
    decimal? FpyIntViActual = null,
    decimal? FpyPtTarget = null,
    decimal? FpyPtActual = null,
    decimal? FpyRiTarget = null,
    decimal? FpyRiActual = null,
    decimal? FpyRiImageTarget = null,
    decimal? FpyRiImageActual = null,
    decimal? FpyPackingViTarget = null,
    decimal? FpyPackingViActual = null,
    int? WoCount0To3Days = null,
    int? WoCount4To7Days = null,
    int? WoCountGreater7Days = null,
    int? WoCountTotal = null,
    int? WoQty0To3Days = null,
    int? WoQty4To7Days = null,
    int? WoQtyGreater7Days = null,
    int? WoQtyTotal = null,
    int? WoClearancePlanGreater7Days = null,
    string? WoResponsible = null,
    string? WoTimeline = null);

internal sealed record QualityExcelMetric(
    string Label,
    string Field,
    string[] Aliases,
    int Target = 0,
    int Actual = 0);

internal sealed record QualityExcelColumns(QualityExcelMetric[] Metrics);

internal sealed record WoClosureColumns(
    int Bucket0To3,
    int Bucket4To7,
    int BucketGreater7,
    int Total,
    int ClearancePlan,
    int Responsible,
    int Timeline);
