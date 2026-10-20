using System.Data;
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
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/admin/catalog")]
public sealed class AdminCatalogController(SqlConnectionFactory connectionFactory) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var plants = await ReadAsync(connection, """
            SELECT PlantId,PlantName,Location,IsActive FROM Plant
            WHERE @IsSuperAdmin=1 OR PlantId=@PlantId ORDER BY PlantName
            """, r => new { plantId=r.GetInt32(0), plantName=r.GetString(1), location=r.IsDBNull(2)?null:r.GetString(2), isActive=r.GetBoolean(3) }, cancellationToken, CreateScopeParameters());
        var customers = await ReadAsync(connection, """
            SELECT c.CustomerId,c.CustomerName,c.IsActive FROM Customer c
            WHERE @IsSuperAdmin=1 OR
                  EXISTS (SELECT 1 FROM PlantCustomer pc WHERE pc.CustomerId=c.CustomerId AND pc.PlantId=@PlantId)
            ORDER BY c.CustomerName
            """, r => new { customerId=r.GetInt32(0), customerName=r.GetString(1), isActive=r.GetBoolean(2) }, cancellationToken, CreateScopeParameters());
        var plantCustomers = await ReadAsync(connection, """
            SELECT PlantId,CustomerId FROM PlantCustomer
            WHERE @IsSuperAdmin=1 OR PlantId=@PlantId ORDER BY PlantId,CustomerId
            """, r => new { plantId=r.GetInt32(0), customerId=r.GetInt32(1) }, cancellationToken, CreateScopeParameters());
        var categories = await ReadAsync(connection, """
            SELECT cat.CategoryId,cat.CustomerId,cat.CategoryName,cat.IsActive,cat.PlantId
            FROM Category cat
            WHERE @IsSuperAdmin=1 OR
                  (cat.PlantId=@PlantId AND EXISTS
                   (SELECT 1 FROM PlantCustomer pc
                    WHERE pc.PlantId=cat.PlantId AND pc.CustomerId=cat.CustomerId))
            ORDER BY cat.PlantId,cat.CustomerId,cat.CategoryName
            """, r => new { categoryId=r.GetInt32(0), customerId=r.GetInt32(1), categoryName=r.GetString(2), isActive=r.GetBoolean(3), plantId=r.GetInt32(4) }, cancellationToken, CreateScopeParameters());
        var models = await ReadAsync(connection, """
            SELECT m.ModelId,m.CustomerId,m.ModelName,m.IsActive FROM Model m
            WHERE @IsSuperAdmin=1 OR
                  EXISTS (SELECT 1 FROM PlantCustomer pc WHERE pc.CustomerId=m.CustomerId AND pc.PlantId=@PlantId)
            ORDER BY m.CustomerId,m.ModelName
            """, r => new { modelId=r.GetInt32(0), customerId=r.GetInt32(1), modelName=r.GetString(2), isActive=r.GetBoolean(3) }, cancellationToken, CreateScopeParameters());
        var lines = await ReadAsync(connection, """
            SELECT pl.LineId,pl.CategoryId,pl.LineName,pl.SAPLocation,pl.IsActive,cat.CustomerId,pl.PlantId
            FROM ProductionLine pl INNER JOIN Category cat ON cat.CategoryId=pl.CategoryId
            WHERE @IsSuperAdmin=1 OR
                  (pl.PlantId=@PlantId AND EXISTS
                   (SELECT 1 FROM PlantCustomer pc
                    WHERE pc.PlantId=pl.PlantId AND pc.CustomerId=cat.CustomerId))
            ORDER BY pl.PlantId,cat.CustomerId,pl.LineName
            """, r => new { lineId=r.GetInt32(0), categoryId=r.GetInt32(1), lineName=r.GetString(2), sapLocation=r.IsDBNull(3)?null:r.GetString(3), isActive=r.GetBoolean(4), customerId=r.GetInt32(5), plantId=r.GetInt32(6) }, cancellationToken, CreateScopeParameters());
        return Ok(new { plants, customers, plantCustomers, categories, models, lines });
    }

    [HttpGet("plants/{id:int}/customers")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> GetPlantCustomers(int id, CancellationToken cancellationToken)
    {
        if (!CanAccessPlant(id)) return Forbid();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT c.CustomerId, c.CustomerName, c.IsActive
            FROM PlantCustomer pc
            INNER JOIN Customer c ON c.CustomerId = pc.CustomerId
            WHERE pc.PlantId = @PlantId
            ORDER BY c.CustomerName
            """,
            connection);
        Add(command, "@PlantId", SqlDbType.Int, id);

        var customers = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            customers.Add(new
            {
                customerId = reader.GetInt32(0),
                customerName = reader.GetString(1),
                isActive = reader.GetBoolean(2)
            });
        }

        await reader.CloseAsync();
        await using var plantCommand = new SqlCommand(
            "SELECT COUNT(1) FROM Plant WHERE PlantId = @PlantId",
            connection);
        Add(plantCommand, "@PlantId", SqlDbType.Int, id);
        if (Convert.ToInt32(await plantCommand.ExecuteScalarAsync(cancellationToken)) == 0)
        {
            return NotFound();
        }

        return Ok(customers);
    }

    [HttpPut("plants/{id:int}/customers")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> SetPlantCustomers(
        int id,
        [FromBody] PlantCustomerRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanAccessPlant(id)) return Forbid();
        var customerIds = request.CustomerIds.Distinct().ToArray();
        if (customerIds.Any(customerId => customerId <= 0))
        {
            return BadRequest(new { message = "Customer IDs must be greater than zero." });
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var plantCommand = new SqlCommand(
                "SELECT COUNT(1) FROM Plant WHERE PlantId = @PlantId AND IsActive = 1",
                connection,
                transaction);
            Add(plantCommand, "@PlantId", SqlDbType.Int, id);
            if (Convert.ToInt32(await plantCommand.ExecuteScalarAsync(cancellationToken)) == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return NotFound();
            }

            if (customerIds.Length > 0)
            {
                var parameters = customerIds.Select((_, index) => $"@CustomerId{index}").ToArray();
                var inClause = string.Join(", ", parameters);
                await using var validateCommand = new SqlCommand(
                    $"""
                    SELECT COUNT(1)
                    FROM Customer c
                    WHERE (c.IsActive = 1 OR EXISTS
                           (SELECT 1 FROM PlantCustomer existing
                            WHERE existing.PlantId=@PlantId AND existing.CustomerId=c.CustomerId))
                      AND c.CustomerId IN ({inClause})
                    """,
                    connection,
                    transaction);
                for (var index = 0; index < customerIds.Length; index++)
                {
                    Add(validateCommand, parameters[index], SqlDbType.Int, customerIds[index]);
                }
                Add(validateCommand, "@PlantId", SqlDbType.Int, id);
                await using var validation = await validateCommand.ExecuteReaderAsync(cancellationToken);
                await validation.ReadAsync(cancellationToken);
                var foundCount = validation.GetInt32(0);
                await validation.CloseAsync();
                if (foundCount != customerIds.Length)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return BadRequest(new { message = "One or more selected customers do not exist or are inactive." });
                }
            }

            var keepCustomerClause = customerIds.Length == 0
                ? string.Empty
                : $"AND uc.CustomerId NOT IN ({string.Join(", ", customerIds.Select((_, index) => $"@CustomerId{index}"))})";
            await using (var assignmentCleanup = new SqlCommand(
                $"""
                DELETE uc
                FROM UserCustomer uc
                INNER JOIN [User] u ON u.UserId = uc.UserId
                WHERE u.PlantId = @PlantId
                  {keepCustomerClause}
                """,
                connection,
                transaction))
            {
                Add(assignmentCleanup, "@PlantId", SqlDbType.Int, id);
                for (var index = 0; index < customerIds.Length; index++)
                {
                    Add(assignmentCleanup, $"@CustomerId{index}", SqlDbType.Int, customerIds[index]);
                }

                await assignmentCleanup.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var clearCommand = new SqlCommand(
                "DELETE FROM PlantCustomer WHERE PlantId = @PlantId",
                connection,
                transaction))
            {
                Add(clearCommand, "@PlantId", SqlDbType.Int, id);
                await clearCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var customerId in customerIds)
            {
                await using var addCommand = new SqlCommand(
                    "INSERT INTO PlantCustomer (PlantId, CustomerId) VALUES (@PlantId, @CustomerId)",
                    connection,
                    transaction);
                Add(addCommand, "@PlantId", SqlDbType.Int, id);
                Add(addCommand, "@CustomerId", SqlDbType.Int, customerId);
                await addCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return Ok(new { message = "Plant customers updated.", customerIds });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("plants")]
    public async Task<IActionResult> CreatePlant([FromBody] PlantRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlantName)) return BadRequest(new { message="Plant name is required." });
        await using var connection=await connectionFactory.OpenAsync(cancellationToken);
        try
        {
            await using var cmd=new SqlCommand("INSERT INTO Plant(PlantName,Location,IsActive) OUTPUT INSERTED.PlantId VALUES(@Name,@Location,1)",connection);
            Add(cmd,"@Name",SqlDbType.VarChar,request.PlantName.Trim()); Add(cmd,"@Location",SqlDbType.VarChar,request.Location?.Trim());
            var id=Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)); return Ok(new { plantId=id, message="Plant created." });
        } catch(SqlException ex) when(ex.Number is 2601 or 2627){return Conflict(new{message="A plant with this name already exists."});}
    }

    [Authorize(Roles = "SuperAdmin")]
    [HttpPut("plants/{id:int}")]
    public async Task<IActionResult> UpdatePlant(int id,[FromBody] PlantRequest request,CancellationToken cancellationToken)
    {
        await using var connection=await connectionFactory.OpenAsync(cancellationToken); await using var cmd=new SqlCommand("UPDATE Plant SET PlantName=@Name,Location=@Location,IsActive=@Active WHERE PlantId=@Id",connection);
        Add(cmd,"@Id",SqlDbType.Int,id); Add(cmd,"@Name",SqlDbType.VarChar,request.PlantName.Trim()); Add(cmd,"@Location",SqlDbType.VarChar,request.Location?.Trim()); Add(cmd,"@Active",SqlDbType.Bit,request.IsActive); var n=await cmd.ExecuteNonQueryAsync(cancellationToken); return n==0?NotFound():Ok(new{message="Plant updated."});
    }
    [Authorize(Roles = "SuperAdmin")]
    [HttpDelete("plants/{id:int}")]
    public Task<IActionResult> DeletePlant(int id,CancellationToken ct)=>SoftDelete("Plant","PlantId",id,ct);

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("customers")]
    public async Task<IActionResult> CreateCustomer([FromBody] NameRequest request,CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { message="Name is required." });
        await using var connection=await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            if (!IsSuperAdmin)
            {
                await using var duplicate=new SqlCommand("""
                    SELECT COUNT(1) FROM Customer c
                    INNER JOIN PlantCustomer pc ON pc.CustomerId=c.CustomerId
                    WHERE pc.PlantId=@PlantId AND c.CustomerName=@Name
                    """,connection,transaction);
                Add(duplicate,"@PlantId",SqlDbType.Int,CurrentPlantId);
                Add(duplicate,"@Name",SqlDbType.VarChar,request.Name.Trim());
                if(Convert.ToInt32(await duplicate.ExecuteScalarAsync(cancellationToken))>0)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Conflict(new { message="A customer with this name already exists for this plant." });
                }
            }

            await using var create=new SqlCommand("""
                INSERT INTO Customer(CustomerName,IsActive)
                OUTPUT INSERTED.CustomerId
                VALUES(@Name,1)
                """,connection,transaction);
            Add(create,"@Name",SqlDbType.VarChar,request.Name.Trim());
            var customerId=Convert.ToInt32(await create.ExecuteScalarAsync(cancellationToken));
            if (!IsSuperAdmin)
            {
                await using var assign=new SqlCommand("INSERT INTO PlantCustomer(PlantId,CustomerId) VALUES(@PlantId,@CustomerId)",connection,transaction);
                Add(assign,"@PlantId",SqlDbType.Int,CurrentPlantId);
                Add(assign,"@CustomerId",SqlDbType.Int,customerId);
                await assign.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return Ok(new { customerId, message="Customer created." });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
    [Authorize(Roles = "SuperAdmin")]
    [HttpPut("customers/{id:int}")]
    public async Task<IActionResult> UpdateCustomer(int id,[FromBody] EntityActiveRequest request,CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { message="Name is required." });
        await using var connection=await connectionFactory.OpenAsync(cancellationToken);
        if (!await CustomerExistsAsync(id,connection,cancellationToken)) return NotFound();
        await using var duplicateCheck=new SqlCommand("""
            SELECT COUNT(1)
            FROM PlantCustomer pc
            INNER JOIN PlantCustomer otherPc
                ON otherPc.PlantId=pc.PlantId
               AND otherPc.CustomerId<>pc.CustomerId
            INNER JOIN Customer otherCustomer
                ON otherCustomer.CustomerId=otherPc.CustomerId
            WHERE pc.CustomerId=@Id
              AND otherCustomer.CustomerName=@Name
            """,connection);
        Add(duplicateCheck,"@Id",SqlDbType.Int,id);
        Add(duplicateCheck,"@Name",SqlDbType.VarChar,request.Name.Trim());
        if(Convert.ToInt32(await duplicateCheck.ExecuteScalarAsync(cancellationToken))>0)
            return Conflict(new { message="A plant cannot have two customers with the same name." });

        await using var command=new SqlCommand("UPDATE Customer SET CustomerName=@Name,IsActive=@Active WHERE CustomerId=@Id",connection);
        Add(command,"@Id",SqlDbType.Int,id);
        Add(command,"@Name",SqlDbType.VarChar,request.Name.Trim());
        Add(command,"@Active",SqlDbType.Bit,request.IsActive);
        return await command.ExecuteNonQueryAsync(cancellationToken)==0?NotFound():Ok(new{message="Updated."});
    }
    [Authorize(Roles = "SuperAdmin")]
    [HttpDelete("customers/{id:int}")]
    public async Task<IActionResult> DeleteCustomer(int id,CancellationToken cancellationToken)
    {
        await using var connection=await connectionFactory.OpenAsync(cancellationToken);
        if (!await CustomerExistsAsync(id,connection,cancellationToken)) return NotFound();
        await using var command=new SqlCommand("UPDATE Customer SET IsActive=0 WHERE CustomerId=@Id",connection);
        Add(command,"@Id",SqlDbType.Int,id);
        return await command.ExecuteNonQueryAsync(cancellationToken)==0?NotFound():Ok(new{message="Deactivated."});
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CategoryRequest request,CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CategoryName))
            return BadRequest(new { message = "Category name is required." });

        var plantId = IsSuperAdmin ? request.PlantId : CurrentPlantId;
        if (plantId <= 0 || (!IsSuperAdmin && request.PlantId > 0 && request.PlantId != plantId))
            return Forbid();

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        if (!await HasCustomerAccessAsync(request.CustomerId, plantId, connection, cancellationToken))
            return BadRequest(new { message = "The selected customer is not assigned to this plant." });

        try
        {
            await using var command = new SqlCommand("""
                INSERT INTO Category(PlantId,CustomerId,CategoryName,IsActive)
                OUTPUT INSERTED.CategoryId
                VALUES(@PlantId,@CustomerId,@Name,1)
                """, connection);
            Add(command, "@PlantId", SqlDbType.Int, plantId);
            Add(command, "@CustomerId", SqlDbType.Int, request.CustomerId);
            Add(command, "@Name", SqlDbType.VarChar, request.CategoryName.Trim());
            var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            return Ok(new { categoryId = id, message = "Category created." });
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return Conflict(new { message = "This category already exists for the plant and customer." });
        }
    }
    [HttpPut("categories/{id:int}")]
    public async Task<IActionResult> UpdateCategory(int id,[FromBody] CategoryRequest request,CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CategoryName))
            return BadRequest(new { message = "Category name is required." });
        var plantId = IsSuperAdmin ? request.PlantId : CurrentPlantId;
        if (plantId <= 0 || (!IsSuperAdmin && request.PlantId > 0 && request.PlantId != plantId))
            return Forbid();

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        if (!await HasCategoryAccessAsync(id, connection, cancellationToken))
            return NotFound();
        if (!await HasCustomerAccessAsync(request.CustomerId, plantId, connection, cancellationToken))
            return BadRequest(new { message = "The selected customer is not assigned to this plant." });

        try
        {
            await using var command = new SqlCommand("""
                UPDATE Category
                SET PlantId=@PlantId,CustomerId=@CustomerId,CategoryName=@Name,IsActive=@Active
                WHERE CategoryId=@Id
                """, connection);
            Add(command, "@Id", SqlDbType.Int, id);
            Add(command, "@PlantId", SqlDbType.Int, plantId);
            Add(command, "@CustomerId", SqlDbType.Int, request.CustomerId);
            Add(command, "@Name", SqlDbType.VarChar, request.CategoryName.Trim());
            Add(command, "@Active", SqlDbType.Bit, request.IsActive);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 0
                ? NotFound()
                : Ok(new { message = "Category updated." });
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627 or 547)
        {
            return Conflict(new { message = "The category cannot be moved because it conflicts with existing lines or reports." });
        }
    }
    [HttpDelete("categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id,CancellationToken ct){await using var c=await connectionFactory.OpenAsync(ct);if(!await HasCategoryAccessAsync(id,c,ct))return NotFound();return await SoftDelete("Category","CategoryId",id,ct);}

    [Authorize(Roles = "SuperAdmin")]
    [HttpPost("models")]
    public async Task<IActionResult> CreateModel([FromBody] ModelRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);if(!await HasCustomerAccessAsync(request.CustomerId,c,cancellationToken))return NotFound();try{await using var cmd=new SqlCommand("INSERT INTO Model(CustomerId,ModelName,IsActive) OUTPUT INSERTED.ModelId VALUES(@CustomerId,@Name,1)",c);Add(cmd,"@CustomerId",SqlDbType.Int,request.CustomerId);Add(cmd,"@Name",SqlDbType.VarChar,request.ModelName.Trim());var id=Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));return Ok(new{modelId=id,message="Model created."});}catch(SqlException ex) when(ex.Number is 2601 or 2627){return Conflict(new{message="This model already exists for the customer."});}}
    [Authorize(Roles = "SuperAdmin")]
    [HttpPut("models/{id:int}")]
    public async Task<IActionResult> UpdateModel(int id,[FromBody] ModelRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);if(!await HasModelAccessAsync(id,c,cancellationToken)||!await HasCustomerAccessAsync(request.CustomerId,c,cancellationToken))return NotFound();await using var cmd=new SqlCommand("UPDATE Model SET CustomerId=@CustomerId,ModelName=@Name,IsActive=@Active WHERE ModelId=@Id",c);Add(cmd,"@Id",SqlDbType.Int,id);Add(cmd,"@CustomerId",SqlDbType.Int,request.CustomerId);Add(cmd,"@Name",SqlDbType.VarChar,request.ModelName.Trim());Add(cmd,"@Active",SqlDbType.Bit,request.IsActive);return await cmd.ExecuteNonQueryAsync(cancellationToken)==0?NotFound():Ok(new{message="Model updated."});}
    [Authorize(Roles = "SuperAdmin")]
    [HttpDelete("models/{id:int}")]
    public async Task<IActionResult> DeleteModel(int id,CancellationToken ct){await using var c=await connectionFactory.OpenAsync(ct);if(!await HasModelAccessAsync(id,c,ct))return NotFound();return await SoftDelete("Model","ModelId",id,ct);}

    [HttpPost("lines")]
    public async Task<IActionResult> CreateLine([FromBody] LineRequest request,CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LineName))
            return BadRequest(new { message = "Line name is required." });
        var plantId = IsSuperAdmin ? request.PlantId : CurrentPlantId;
        if (plantId <= 0 || (!IsSuperAdmin && request.PlantId > 0 && request.PlantId != plantId))
            return Forbid();

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        if (!await HasCategoryAccessAsync(request.CategoryId, plantId, connection, cancellationToken))
            return BadRequest(new { message = "The selected category does not belong to this plant." });

        try
        {
            await using var command = new SqlCommand("""
                INSERT INTO ProductionLine(PlantId,CategoryId,LineName,SAPLocation,IsActive)
                OUTPUT INSERTED.LineId
                VALUES(@PlantId,@CategoryId,@Name,@SAP,1)
                """, connection);
            Add(command, "@PlantId", SqlDbType.Int, plantId);
            Add(command, "@CategoryId", SqlDbType.Int, request.CategoryId);
            Add(command, "@Name", SqlDbType.VarChar, request.LineName.Trim());
            Add(command, "@SAP", SqlDbType.VarChar, request.SapLocation?.Trim());
            var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            return Ok(new { lineId = id, message = "Production line created." });
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return Conflict(new { message = "This line already exists for the category." });
        }
    }
    [HttpPut("lines/{id:int}")]
    public async Task<IActionResult> UpdateLine(int id,[FromBody] LineRequest request,CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.LineName))
            return BadRequest(new { message = "Line name is required." });
        var plantId = IsSuperAdmin ? request.PlantId : CurrentPlantId;
        if (plantId <= 0 || (!IsSuperAdmin && request.PlantId > 0 && request.PlantId != plantId))
            return Forbid();

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        if (!await HasLineAccessAsync(id, connection, cancellationToken)
            || !await HasCategoryAccessAsync(request.CategoryId, plantId, connection, cancellationToken))
            return NotFound();

        try
        {
            await using var command = new SqlCommand("""
                UPDATE ProductionLine
                SET PlantId=@PlantId,CategoryId=@CategoryId,LineName=@Name,SAPLocation=@SAP,IsActive=@Active
                WHERE LineId=@Id
                """, connection);
            Add(command, "@Id", SqlDbType.Int, id);
            Add(command, "@PlantId", SqlDbType.Int, plantId);
            Add(command, "@CategoryId", SqlDbType.Int, request.CategoryId);
            Add(command, "@Name", SqlDbType.VarChar, request.LineName.Trim());
            Add(command, "@SAP", SqlDbType.VarChar, request.SapLocation?.Trim());
            Add(command, "@Active", SqlDbType.Bit, request.IsActive);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 0
                ? NotFound()
                : Ok(new { message = "Line updated." });
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627 or 547)
        {
            return Conflict(new { message = "The line cannot be moved because existing reports or plans depend on it." });
        }
    }
    [HttpDelete("lines/{id:int}")]
    public async Task<IActionResult> DeleteLine(int id,CancellationToken ct){await using var c=await connectionFactory.OpenAsync(ct);if(!await HasLineAccessAsync(id,c,ct))return NotFound();return await SoftDelete("ProductionLine","LineId",id,ct);}

    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans([FromQuery] int? customerId,[FromQuery] int? categoryId,[FromQuery] int? lineId,[FromQuery] int? modelId,[FromQuery] int? year,[FromQuery] int? month,CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        const string sql = """
            SELECT km.KPI_MasterId,km.CustomerId,c.CustomerName,km.CategoryId,cat.CategoryName,
                   km.LineId,pl.LineName,km.ModelId,m.ModelName,km.KPIYEAR,km.KPIMonth,
                   km.ProductionPlan,km.UPHTarget,km.UPPHTarget,km.CPHTarget,km.FPYTarget,
                   km.FTYTarget,km.RTYTarget,km.OSDTarget,km.OTTarget,km.IsActive,km.PlantId
            FROM KPI_Master km
            INNER JOIN Customer c ON c.CustomerId=km.CustomerId
            INNER JOIN Category cat ON cat.CategoryId=km.CategoryId
            INNER JOIN ProductionLine pl ON pl.LineId=km.LineId
            INNER JOIN Model m ON m.ModelId=km.ModelId
            WHERE (@CustomerId IS NULL OR km.CustomerId=@CustomerId)
              AND (@CategoryId IS NULL OR km.CategoryId=@CategoryId)
              AND (@LineId IS NULL OR km.LineId=@LineId)
              AND (@ModelId IS NULL OR km.ModelId=@ModelId)
              AND (@Year IS NULL OR km.KPIYEAR=@Year)
              AND (@Month IS NULL OR km.KPIMonth=@Month)
              AND
              (
                  @IsSuperAdmin=1
                  OR
                  (
                      km.PlantId=@PlantId
                      AND EXISTS
                      (
                          SELECT 1
                          FROM PlantCustomer pc
                          INNER JOIN Customer mappedCustomer ON mappedCustomer.CustomerId=pc.CustomerId
                          INNER JOIN Plant mappedPlant ON mappedPlant.PlantId=pc.PlantId
                          WHERE pc.PlantId=km.PlantId
                            AND pc.CustomerId=km.CustomerId
                            AND mappedCustomer.IsActive=1
                            AND mappedPlant.IsActive=1
                      )
                  )
              )
            ORDER BY km.KPIYEAR DESC,km.KPIMonth DESC,c.CustomerName,pl.LineName,m.ModelName;
            """;
        await using var command = new SqlCommand(sql, connection);
        Add(command,"@CustomerId",SqlDbType.Int,customerId);
        Add(command,"@CategoryId",SqlDbType.Int,categoryId);
        Add(command,"@LineId",SqlDbType.Int,lineId);
        Add(command,"@ModelId",SqlDbType.Int,modelId);
        Add(command,"@Year",SqlDbType.Int,year);
        Add(command,"@Month",SqlDbType.Int,month);
        AddScope(command);

        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                planId=reader.GetInt32(0), customerId=reader.GetInt32(1), customerName=reader.GetString(2),
                categoryId=reader.GetInt32(3), categoryName=reader.GetString(4), lineId=reader.GetInt32(5),
                lineName=reader.GetString(6), modelId=reader.GetInt32(7), modelName=reader.GetString(8),
                year=reader.GetInt32(9), month=reader.GetInt32(10), productionPlan=GetDecimal(reader,11),
                uphTarget=GetDecimal(reader,12), upphTarget=GetDecimal(reader,13), cphTarget=GetDecimal(reader,14),
                fpyTarget=GetDecimal(reader,15), ftyTarget=GetDecimal(reader,16), rtyTarget=GetDecimal(reader,17),
                osdTarget=GetDecimal(reader,18), otTarget=GetDecimal(reader,19), isActive=reader.GetBoolean(20),
                plantId=reader.GetInt32(21)
            });
        }
        return Ok(rows);
    }

    [HttpPost("plans")]
    public async Task<IActionResult> UpsertPlan([FromBody] PlanRequest request,CancellationToken cancellationToken)
    {
        if (request.Month is < 1 or > 12 || request.Year < 2000)
            return BadRequest(new { message = "Enter a valid year and month." });
        var plantId = IsSuperAdmin ? request.PlantId : CurrentPlantId;
        if (plantId <= 0 || (!IsSuperAdmin && request.PlantId > 0 && request.PlantId != plantId))
            return Forbid();

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var validate = new SqlCommand("""
                SELECT COUNT(1)
                FROM Category cat
                INNER JOIN ProductionLine pl ON pl.CategoryId=cat.CategoryId AND pl.PlantId=cat.PlantId
                INNER JOIN Model m ON m.CustomerId=cat.CustomerId
                INNER JOIN PlantCustomer pc ON pc.PlantId=cat.PlantId AND pc.CustomerId=cat.CustomerId
                INNER JOIN Customer c ON c.CustomerId=cat.CustomerId
                INNER JOIN Plant p ON p.PlantId=pc.PlantId
                WHERE cat.PlantId=@PlantId AND cat.CustomerId=@CustomerId
                  AND cat.CategoryId=@CategoryId AND pl.LineId=@LineId AND m.ModelId=@ModelId
                  AND cat.IsActive=1 AND pl.IsActive=1 AND m.IsActive=1
                  AND c.IsActive=1 AND p.IsActive=1
                """, connection, transaction);
            Add(validate,"@PlantId",SqlDbType.Int,plantId);
            Add(validate,"@CustomerId",SqlDbType.Int,request.CustomerId);
            Add(validate,"@CategoryId",SqlDbType.Int,request.CategoryId);
            Add(validate,"@LineId",SqlDbType.Int,request.LineId);
            Add(validate,"@ModelId",SqlDbType.Int,request.ModelId);
            if (Convert.ToInt32(await validate.ExecuteScalarAsync(cancellationToken)) != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                return BadRequest(new { message = "The selected customer, category, line or model is not valid for this plant." });
            }

            const string sql = """
                UPDATE KPI_Master WITH (UPDLOCK,HOLDLOCK)
                SET ProductionPlan=@ProductionPlan,UPHTarget=@UPHTarget,UPPHTarget=@UPPHTarget,
                    CPHTarget=@CPHTarget,FPYTarget=@FPYTarget,FTYTarget=@FTYTarget,RTYTarget=@RTYTarget,
                    OSDTarget=@OSDTarget,OTTarget=@OTTarget,IsActive=@IsActive,UpdatedAt=SYSUTCDATETIME()
                WHERE PlantId=@PlantId AND CustomerId=@CustomerId AND CategoryId=@CategoryId
                  AND ModelId=@ModelId AND LineId=@LineId AND KPIYEAR=@Year AND KPIMonth=@Month;
                IF @@ROWCOUNT=0
                    INSERT INTO KPI_Master(PlantId,CustomerId,CategoryId,ModelId,LineId,KPIYEAR,KPIMonth,
                        ProductionPlan,UPHTarget,UPPHTarget,CPHTarget,FPYTarget,FTYTarget,RTYTarget,
                        OSDTarget,OTTarget,IsActive,CreatedAt,UpdatedAt)
                    VALUES(@PlantId,@CustomerId,@CategoryId,@ModelId,@LineId,@Year,@Month,
                        @ProductionPlan,@UPHTarget,@UPPHTarget,@CPHTarget,@FPYTarget,@FTYTarget,@RTYTarget,
                        @OSDTarget,@OTTarget,@IsActive,SYSUTCDATETIME(),SYSUTCDATETIME());
                SELECT KPI_MasterId FROM KPI_Master
                WHERE PlantId=@PlantId AND CustomerId=@CustomerId AND CategoryId=@CategoryId
                  AND ModelId=@ModelId AND LineId=@LineId AND KPIYEAR=@Year AND KPIMonth=@Month;
                """;
            await using var command = new SqlCommand(sql, connection, transaction);
            Add(command,"@PlantId",SqlDbType.Int,plantId);
            Add(command,"@CustomerId",SqlDbType.Int,request.CustomerId);
            Add(command,"@CategoryId",SqlDbType.Int,request.CategoryId);
            Add(command,"@ModelId",SqlDbType.Int,request.ModelId);
            Add(command,"@LineId",SqlDbType.Int,request.LineId);
            Add(command,"@Year",SqlDbType.Int,request.Year);
            Add(command,"@Month",SqlDbType.Int,request.Month);
            AddDecimal(command,"@ProductionPlan",request.ProductionPlan);
            AddDecimal(command,"@UPHTarget",request.UphTarget);
            AddDecimal(command,"@UPPHTarget",request.UpphTarget);
            AddDecimal(command,"@CPHTarget",request.CphTarget);
            AddDecimal(command,"@FPYTarget",request.FpyTarget);
            AddDecimal(command,"@FTYTarget",request.FtyTarget);
            AddDecimal(command,"@RTYTarget",request.RtyTarget);
            AddDecimal(command,"@OSDTarget",request.OsdTarget);
            AddDecimal(command,"@OTTarget",request.OtTarget);
            Add(command,"@IsActive",SqlDbType.Bit,request.IsActive);
            var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            await transaction.CommitAsync(cancellationToken);
            return Ok(new { planId = id, message = "KPI plan saved." });
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627 or 547)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new { message = "The KPI plan conflicts with an existing plan or invalid configuration." });
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
    [HttpDelete("plans/{id:int}")]
    public async Task<IActionResult> DeletePlan(int id,CancellationToken ct)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var check = new SqlCommand("""
            SELECT COUNT(1) FROM KPI_Master
            WHERE KPI_MasterId=@Id
              AND
              (
                  @IsSuperAdmin=1
                  OR
                  (
                      PlantId=@PlantId
                      AND EXISTS
                      (
                          SELECT 1
                          FROM PlantCustomer pc
                          INNER JOIN Customer c ON c.CustomerId=pc.CustomerId
                          INNER JOIN Plant p ON p.PlantId=pc.PlantId
                          WHERE pc.PlantId=KPI_Master.PlantId
                            AND pc.CustomerId=KPI_Master.CustomerId
                            AND c.IsActive=1 AND p.IsActive=1
                      )
                  )
              )
            """, connection);
        Add(check,"@Id",SqlDbType.Int,id);
        AddScope(check);
        if (Convert.ToInt32(await check.ExecuteScalarAsync(ct)) == 0)
            return NotFound();
        return await SoftDelete("KPI_Master","KPI_MasterId",id,ct);
    }

    private async Task<IActionResult> CreateName(string table,string idColumn,string nameColumn,string? name,CancellationToken ct){if(string.IsNullOrWhiteSpace(name))return BadRequest(new{message="Name is required."});await using var c=await connectionFactory.OpenAsync(ct);try{await using var cmd=new SqlCommand($"INSERT INTO {table}({nameColumn},IsActive) OUTPUT INSERTED.{idColumn} VALUES(@Name,1)",c);Add(cmd,"@Name",SqlDbType.VarChar,name.Trim());var id=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));return Ok(new{id,message="Created."});}catch(SqlException ex) when(ex.Number is 2601 or 2627){return Conflict(new{message="An entity with this name already exists."});}}
    private async Task<IActionResult> UpdateName(string table,string idColumn,string nameColumn,int id,string? name,bool active,CancellationToken ct){if(string.IsNullOrWhiteSpace(name))return BadRequest(new{message="Name is required."});await using var c=await connectionFactory.OpenAsync(ct);await using var cmd=new SqlCommand($"UPDATE {table} SET {nameColumn}=@Name,IsActive=@Active WHERE {idColumn}=@Id",c);Add(cmd,"@Id",SqlDbType.Int,id);Add(cmd,"@Name",SqlDbType.VarChar,name.Trim());Add(cmd,"@Active",SqlDbType.Bit,active);return await cmd.ExecuteNonQueryAsync(ct)==0?NotFound():Ok(new{message="Updated."});}
    private async Task<IActionResult> SoftDelete(string table,string idColumn,int id,CancellationToken ct){await using var c=await connectionFactory.OpenAsync(ct);await using var cmd=new SqlCommand($"UPDATE {table} SET IsActive=0 WHERE {idColumn}=@Id",c);Add(cmd,"@Id",SqlDbType.Int,id);return await cmd.ExecuteNonQueryAsync(ct)==0?NotFound():Ok(new{message="Deactivated."});}
    private static async Task<List<T>> ReadAsync<T>(SqlConnection c,string sql,Func<SqlDataReader,T> map,CancellationToken ct,params SqlParameter[] parameters){var list=new List<T>();await using var cmd=new SqlCommand(sql,c);foreach(var parameter in parameters)cmd.Parameters.Add(parameter);await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))list.Add(map(r));return list;}
    private bool IsSuperAdmin => User.IsInRole("SuperAdmin");
    private int CurrentPlantId => int.TryParse(User.FindFirstValue("plantId"),out var plantId)?plantId:0;
    private bool CanAccessPlant(int plantId) => IsSuperAdmin || plantId==CurrentPlantId;
    private SqlParameter[] CreateScopeParameters() => [new SqlParameter("@IsSuperAdmin",IsSuperAdmin),new SqlParameter("@PlantId",CurrentPlantId)];
    private void AddScope(SqlCommand command){command.Parameters.AddRange(CreateScopeParameters());}
    private async Task<bool> HasCustomerAccessAsync(int customerId,SqlConnection c,CancellationToken ct)
    {
        if (IsSuperAdmin)
        {
            await using var adminCommand = new SqlCommand(
                "SELECT COUNT(1) FROM Customer WHERE CustomerId=@CustomerId AND IsActive=1",
                c);
            Add(adminCommand, "@CustomerId", SqlDbType.Int, customerId);
            return Convert.ToInt32(await adminCommand.ExecuteScalarAsync(ct)) == 1;
        }

        return await HasCustomerAccessAsync(customerId, CurrentPlantId, c, ct);
    }

    private static async Task<bool> CustomerExistsAsync(int customerId,SqlConnection connection,CancellationToken ct)
    {
        await using var command = new SqlCommand(
            "SELECT COUNT(1) FROM Customer WHERE CustomerId=@CustomerId",
            connection);
        Add(command,"@CustomerId",SqlDbType.Int,customerId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct)) == 1;
    }

    private async Task<bool> HasCustomerAccessAsync(int customerId,int plantId,SqlConnection c,CancellationToken ct)
    {
        await using var cmd=new SqlCommand("""
            SELECT COUNT(1)
            FROM PlantCustomer pc
            INNER JOIN Customer customer ON customer.CustomerId=pc.CustomerId
            INNER JOIN Plant plant ON plant.PlantId=pc.PlantId
            WHERE pc.PlantId=@PlantId
              AND pc.CustomerId=@CustomerId
              AND customer.IsActive=1
              AND plant.IsActive=1
            """,c);
        Add(cmd,"@PlantId",SqlDbType.Int,plantId);Add(cmd,"@CustomerId",SqlDbType.Int,customerId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }
    private async Task<bool> HasCategoryAccessAsync(int categoryId,SqlConnection c,CancellationToken ct)
    {
        if(IsSuperAdmin)return true;
        await using var cmd=new SqlCommand("""
            SELECT COUNT(1) FROM Category cat
            WHERE cat.CategoryId=@Id AND cat.PlantId=@PlantId
              AND EXISTS
              (
                  SELECT 1 FROM PlantCustomer pc
                  INNER JOIN Customer c ON c.CustomerId=pc.CustomerId
                  INNER JOIN Plant p ON p.PlantId=pc.PlantId
                  WHERE pc.PlantId=cat.PlantId
                    AND pc.CustomerId=cat.CustomerId
                    AND c.IsActive=1 AND p.IsActive=1
              )
            """,c);
        Add(cmd,"@Id",SqlDbType.Int,categoryId);Add(cmd,"@PlantId",SqlDbType.Int,CurrentPlantId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }
    private async Task<bool> HasCategoryAccessAsync(int categoryId,int plantId,SqlConnection c,CancellationToken ct)
    {
        await using var cmd=new SqlCommand("""
            SELECT COUNT(1) FROM Category cat
            INNER JOIN PlantCustomer pc ON pc.PlantId=cat.PlantId AND pc.CustomerId=cat.CustomerId
            INNER JOIN Customer c ON c.CustomerId=pc.CustomerId
            INNER JOIN Plant p ON p.PlantId=pc.PlantId
            WHERE cat.CategoryId=@Id AND cat.PlantId=@PlantId
              AND c.IsActive=1 AND p.IsActive=1
            """,c);
        Add(cmd,"@Id",SqlDbType.Int,categoryId);Add(cmd,"@PlantId",SqlDbType.Int,plantId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }
    private async Task<bool> HasModelAccessAsync(int modelId,SqlConnection c,CancellationToken ct)
    {
        if(IsSuperAdmin)return true;
        await using var cmd=new SqlCommand("""
            SELECT COUNT(1) FROM Model m
            INNER JOIN PlantCustomer pc ON pc.CustomerId=m.CustomerId
            WHERE m.ModelId=@Id AND pc.PlantId=@PlantId
            """,c);
        Add(cmd,"@Id",SqlDbType.Int,modelId);Add(cmd,"@PlantId",SqlDbType.Int,CurrentPlantId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }
    private async Task<bool> HasLineAccessAsync(int lineId,SqlConnection c,CancellationToken ct)
    {
        if(IsSuperAdmin)return true;
        await using var cmd=new SqlCommand("""
            SELECT COUNT(1) FROM ProductionLine pl
            INNER JOIN Category cat ON cat.CategoryId=pl.CategoryId AND cat.PlantId=pl.PlantId
            INNER JOIN PlantCustomer pc ON pc.PlantId=cat.PlantId AND pc.CustomerId=cat.CustomerId
            INNER JOIN Customer c ON c.CustomerId=pc.CustomerId
            INNER JOIN Plant p ON p.PlantId=pc.PlantId
            WHERE pl.LineId=@Id AND pl.PlantId=@PlantId
              AND c.IsActive=1 AND p.IsActive=1
            """,c);
        Add(cmd,"@Id",SqlDbType.Int,lineId);Add(cmd,"@PlantId",SqlDbType.Int,CurrentPlantId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }
    private static void Add(SqlCommand c,string n,SqlDbType t,object? v){var p=c.Parameters.Add(n,t);p.Value=v??DBNull.Value;}
   private static void AddDecimal(SqlCommand c, string n, decimal? v)
{
    var p = c.Parameters.Add(n, SqlDbType.Decimal);
    p.Precision = 18;
    p.Scale = 4;
    p.Value = v.HasValue ? (object)v.Value : DBNull.Value;
}
    private static decimal? GetDecimal(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToDecimal(r.GetValue(i));
}

public sealed record PlantRequest(string PlantName,string? Location,bool IsActive=true);
public sealed record PlantCustomerRequest(IReadOnlyList<int> CustomerIds);
public sealed record NameRequest(string Name);
public sealed record EntityActiveRequest(string Name,bool IsActive=true);
public sealed record CategoryRequest(int PlantId,int CustomerId,string CategoryName,bool IsActive=true);
public sealed record ModelRequest(int CustomerId,string ModelName,bool IsActive=true);
public sealed record LineRequest(int PlantId,int CategoryId,string LineName,string? SapLocation,bool IsActive=true);
public sealed record PlanRequest(int PlantId,int CustomerId,int CategoryId,int LineId,int ModelId,int Year,int Month,decimal? ProductionPlan,decimal? UphTarget,decimal? UpphTarget,decimal? CphTarget,decimal? FpyTarget,decimal? FtyTarget,decimal? RtyTarget,decimal? OsdTarget,decimal? OtTarget,bool IsActive=true);
