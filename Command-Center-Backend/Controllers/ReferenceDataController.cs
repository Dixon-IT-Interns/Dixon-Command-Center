<<<<<<< HEAD
using System.Security.Claims;
using Dixon.CommandCenter.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
=======
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dixon.CommandCenter.API.Data;
using System.Security.Claims;
>>>>>>> origin/main

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class ReferenceDataController(SqlConnectionFactory connectionFactory) : ControllerBase
{
    [HttpGet("plants")]
    public async Task<IActionResult> GetPlants(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var plants = await ReadAsync(
            connection,
            """
            SELECT PlantId, PlantName, Location
            FROM Plant
            WHERE IsActive = 1
              AND (@IsSuperAdmin = 1 OR PlantId = @PlantId)
            ORDER BY PlantName
            """,
            reader => new
            {
                plantId = reader.GetInt32(0),
                plantName = reader.GetString(1),
                location = reader.IsDBNull(2) ? null : reader.GetString(2)
            },
            cancellationToken,
            new SqlParameter("@IsSuperAdmin", User.IsInRole("SuperAdmin")),
            new SqlParameter("@PlantId", int.TryParse(User.FindFirstValue("plantId"), out var plantId) ? plantId : 0));

        return Ok(plants);
    }

    [HttpGet("customers")]
    public async Task<IActionResult> GetCustomers(CancellationToken cancellationToken)
    {
        if (!TryGetPlantId(out var plantId))
        {
            return Unauthorized();
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var customers = await ReadAsync(
            connection,
            """
            SELECT c.CustomerId, c.CustomerName
            FROM PlantCustomer pc
            INNER JOIN Customer c ON c.CustomerId = pc.CustomerId
            INNER JOIN Plant p ON p.PlantId = pc.PlantId
            WHERE pc.PlantId = @PlantId
              AND c.IsActive = 1
              AND p.IsActive = 1
              AND
              (
                  @CanViewAll = 1
                  OR EXISTS
                  (
                      SELECT 1 FROM UserCustomer uc
                      WHERE uc.UserId = @UserId AND uc.CustomerId = c.CustomerId
                  )
              )
            ORDER BY c.CustomerName
            """,
            reader => new
            {
                customerId = reader.GetInt32(0),
                customerName = reader.GetString(1)
            },
            cancellationToken,
            new SqlParameter("@PlantId", plantId),
            new SqlParameter("@CanViewAll", CanViewAllPlantCustomers),
            new SqlParameter("@UserId", GetUserId()));

        return Ok(customers);
    }

    [HttpGet("customers/{customerId:int}/categories")]
    public async Task<IActionResult> GetCustomerCategories(int customerId, CancellationToken cancellationToken)
    {
        if (customerId <= 0)
        {
            return BadRequest(new { message = "Customer ID must be greater than zero." });
        }

        if (!TryGetPlantId(out var plantId))
        {
            return Unauthorized();
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var categories = await ReadAsync(
            connection,
            """
            SELECT cat.CategoryId, cat.CustomerId, cat.CategoryName
            FROM Category cat
            INNER JOIN PlantCustomer pc ON pc.PlantId = cat.PlantId AND pc.CustomerId = cat.CustomerId
            INNER JOIN Customer c ON c.CustomerId = pc.CustomerId
            INNER JOIN Plant p ON p.PlantId = pc.PlantId
            WHERE cat.IsActive = 1
              AND cat.CustomerId = @CustomerId
              AND cat.PlantId = @PlantId
              AND c.IsActive = 1
              AND p.IsActive = 1
              AND
              (
                  @CanViewAll = 1
                  OR EXISTS
                  (
                      SELECT 1 FROM UserCustomer uc
                      WHERE uc.UserId = @UserId AND uc.CustomerId = c.CustomerId
                  )
              )
            ORDER BY cat.CategoryName
            """,
            reader => new
            {
                categoryId = reader.GetInt32(0),
                customerId = reader.GetInt32(1),
                categoryName = reader.GetString(2)
            },
            cancellationToken,
            new SqlParameter("@CustomerId", customerId),
            new SqlParameter("@PlantId", plantId),
            new SqlParameter("@CanViewAll", CanViewAllPlantCustomers),
            new SqlParameter("@UserId", GetUserId()));

        return Ok(categories);
    }

    [HttpGet("categories/{categoryId:int}/lines")]
    public async Task<IActionResult> GetCategoryLines(int categoryId, CancellationToken cancellationToken)
    {
        if (categoryId <= 0)
        {
            return BadRequest(new { message = "Category ID must be greater than zero." });
        }

        if (!TryGetPlantId(out var plantId))
        {
            return Unauthorized();
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var lines = await ReadAsync(
            connection,
            """
            SELECT pl.LineId, pl.CategoryId, pl.LineName, pl.SAPLocation
            FROM ProductionLine pl
            INNER JOIN Category cat ON cat.CategoryId = pl.CategoryId
            INNER JOIN PlantCustomer pc ON pc.PlantId = cat.PlantId AND pc.CustomerId = cat.CustomerId
            INNER JOIN Customer c ON c.CustomerId = pc.CustomerId
            INNER JOIN Plant p ON p.PlantId = pc.PlantId
            WHERE pl.IsActive = 1
              AND cat.IsActive = 1
              AND cat.CategoryId = @CategoryId
              AND cat.PlantId = @PlantId
              AND pl.PlantId = @PlantId
              AND c.IsActive = 1
              AND p.IsActive = 1
              AND
              (
                  @CanViewAll = 1
                  OR EXISTS
                  (
                      SELECT 1 FROM UserCustomer uc
                      WHERE uc.UserId = @UserId AND uc.CustomerId = c.CustomerId
                  )
              )
            ORDER BY pl.LineName
            """,
            reader => new
            {
                lineId = reader.GetInt32(0),
                categoryId = reader.GetInt32(1),
                lineName = reader.GetString(2),
                sapLocation = reader.IsDBNull(3) ? null : reader.GetString(3)
            },
            cancellationToken,
            new SqlParameter("@CategoryId", categoryId),
            new SqlParameter("@PlantId", plantId),
            new SqlParameter("@CanViewAll", CanViewAllPlantCustomers),
            new SqlParameter("@UserId", GetUserId()));

        return Ok(lines);
    }

    [HttpGet("customers/{customerId:int}/models")]
    public async Task<IActionResult> GetCustomerModels(int customerId, CancellationToken cancellationToken)
    {
        if (customerId <= 0)
        {
            return BadRequest(new { message = "Customer ID must be greater than zero." });
        }

        if (!TryGetPlantId(out var plantId))
        {
            return Unauthorized();
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var models = await ReadAsync(
            connection,
            """
            SELECT m.ModelId, m.CustomerId, m.ModelName
            FROM Model m
            INNER JOIN PlantCustomer pc ON pc.CustomerId = m.CustomerId
            INNER JOIN Customer c ON c.CustomerId = pc.CustomerId
            INNER JOIN Plant p ON p.PlantId = pc.PlantId
            WHERE m.IsActive = 1
              AND m.CustomerId = @CustomerId
              AND pc.PlantId = @PlantId
              AND c.IsActive = 1
              AND p.IsActive = 1
              AND
              (
                  @CanViewAll = 1
                  OR EXISTS
                  (
                      SELECT 1 FROM UserCustomer uc
                      WHERE uc.UserId = @UserId AND uc.CustomerId = c.CustomerId
                  )
              )
            ORDER BY m.ModelName
            """,
            reader => new
            {
                modelId = reader.GetInt32(0),
                customerId = reader.GetInt32(1),
                modelName = reader.GetString(2)
            },
            cancellationToken,
            new SqlParameter("@CustomerId", customerId),
            new SqlParameter("@PlantId", plantId),
            new SqlParameter("@CanViewAll", CanViewAllPlantCustomers),
            new SqlParameter("@UserId", GetUserId()));

        return Ok(models);
    }

    private static async Task<List<T>> ReadAsync<T>(
        SqlConnection connection,
        string sql,
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters)
    {
        var items = new List<T>();
        await using var command = new SqlCommand(sql, connection);

        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(map(reader));
        }

        return items;
    }

    private bool TryGetPlantId(out int plantId) =>
        int.TryParse(User.FindFirstValue("plantId"), out plantId) && plantId > 0;

    private int GetUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : 0;

    private bool CanViewAllPlantCustomers =>
        User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
}
