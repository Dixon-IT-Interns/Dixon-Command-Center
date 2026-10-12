using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dixon.CommandCenter.API.Data;
using System.Security.Claims;

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
            SELECT cat.CategoryId,cat.CustomerId,cat.CategoryName,cat.IsActive FROM Category cat
            WHERE @IsSuperAdmin=1 OR
                  EXISTS (SELECT 1 FROM PlantCustomer pc WHERE pc.CustomerId=cat.CustomerId AND pc.PlantId=@PlantId)
            ORDER BY cat.CustomerId,cat.CategoryName
            """, r => new { categoryId=r.GetInt32(0), customerId=r.GetInt32(1), categoryName=r.GetString(2), isActive=r.GetBoolean(3) }, cancellationToken, CreateScopeParameters());
        var models = await ReadAsync(connection, """
            SELECT m.ModelId,m.CustomerId,m.ModelName,m.IsActive FROM Model m
            WHERE @IsSuperAdmin=1 OR
                  EXISTS (SELECT 1 FROM PlantCustomer pc WHERE pc.CustomerId=m.CustomerId AND pc.PlantId=@PlantId)
            ORDER BY m.CustomerId,m.ModelName
            """, r => new { modelId=r.GetInt32(0), customerId=r.GetInt32(1), modelName=r.GetString(2), isActive=r.GetBoolean(3) }, cancellationToken, CreateScopeParameters());
        var lines = await ReadAsync(connection, """
            SELECT pl.LineId,pl.CategoryId,pl.LineName,pl.SAPLocation,pl.IsActive,cat.CustomerId
            FROM ProductionLine pl INNER JOIN Category cat ON cat.CategoryId=pl.CategoryId
            WHERE @IsSuperAdmin=1 OR
                  EXISTS (SELECT 1 FROM PlantCustomer pc WHERE pc.CustomerId=cat.CustomerId AND pc.PlantId=@PlantId)
            ORDER BY cat.CustomerId,pl.LineName
            """, r => new { lineId=r.GetInt32(0), categoryId=r.GetInt32(1), lineName=r.GetString(2), sapLocation=r.IsDBNull(3)?null:r.GetString(3), isActive=r.GetBoolean(4), customerId=r.GetInt32(5) }, cancellationToken, CreateScopeParameters());
        var lineModels = await ReadAsync(connection, """
            SELECT lm.LineModelId,lm.LineId,lm.ModelId,lm.IsActive,m.ModelName
            FROM LineModel lm INNER JOIN Model m ON m.ModelId=lm.ModelId
            INNER JOIN ProductionLine pl ON pl.LineId=lm.LineId
            INNER JOIN Category cat ON cat.CategoryId=pl.CategoryId
            WHERE @IsSuperAdmin=1 OR
                  EXISTS (SELECT 1 FROM PlantCustomer pc WHERE pc.CustomerId=cat.CustomerId AND pc.PlantId=@PlantId)
            ORDER BY lm.LineId,m.ModelName
            """, r => new { lineModelId=r.GetInt32(0), lineId=r.GetInt32(1), modelId=r.GetInt32(2), isActive=r.GetBoolean(3), modelName=r.GetString(4) }, cancellationToken, CreateScopeParameters());
        return Ok(new { plants, customers, plantCustomers, categories, models, lines, lineModels });
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
                    SELECT COUNT(1), COUNT(DISTINCT CustomerName)
                    FROM Customer
                    WHERE IsActive = 1
                      AND CustomerId IN ({inClause})
                    """,
                    connection,
                    transaction);
                for (var index = 0; index < customerIds.Length; index++)
                {
                    Add(validateCommand, parameters[index], SqlDbType.Int, customerIds[index]);
                }
                await using var validation = await validateCommand.ExecuteReaderAsync(cancellationToken);
                await validation.ReadAsync(cancellationToken);
                var foundCount = validation.GetInt32(0);
                var uniqueNameCount = validation.GetInt32(1);
                await validation.CloseAsync();
                if (foundCount != customerIds.Length)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return BadRequest(new { message = "One or more selected customers do not exist or are inactive." });
                }

                if (uniqueNameCount != customerIds.Length)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return Conflict(new { message = "A plant cannot have two customers with the same name." });
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
    [HttpPut("customers/{id:int}")]
    public async Task<IActionResult> UpdateCustomer(int id,[FromBody] EntityActiveRequest request,CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest(new { message="Name is required." });
        await using var connection=await connectionFactory.OpenAsync(cancellationToken);
        if (!await HasCustomerAccessAsync(id,connection,cancellationToken)) return NotFound();
        if (!IsSuperAdmin)
        {
            await using var shared=new SqlCommand("SELECT COUNT(1) FROM PlantCustomer WHERE CustomerId=@Id AND PlantId<>@PlantId",connection);
            Add(shared,"@Id",SqlDbType.Int,id);Add(shared,"@PlantId",SqlDbType.Int,CurrentPlantId);
            if(Convert.ToInt32(await shared.ExecuteScalarAsync(cancellationToken))>0)
                return Conflict(new { message="This customer is also assigned to another plant and cannot be edited by a plant Admin." });
        }
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
    [HttpDelete("customers/{id:int}")]
    public async Task<IActionResult> DeleteCustomer(int id,CancellationToken cancellationToken)
    {
        await using var connection=await connectionFactory.OpenAsync(cancellationToken);
        if (!await HasCustomerAccessAsync(id,connection,cancellationToken)) return NotFound();
        await using var command=new SqlCommand("UPDATE Customer SET IsActive=0 WHERE CustomerId=@Id",connection);
        Add(command,"@Id",SqlDbType.Int,id);
        return await command.ExecuteNonQueryAsync(cancellationToken)==0?NotFound():Ok(new{message="Deactivated."});
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CategoryRequest request,CancellationToken cancellationToken)
    {
        await using var c=await connectionFactory.OpenAsync(cancellationToken);if(!await HasCustomerAccessAsync(request.CustomerId,c,cancellationToken))return NotFound();try{await using var cmd=new SqlCommand("INSERT INTO Category(CustomerId,CategoryName,IsActive) OUTPUT INSERTED.CategoryId VALUES(@CustomerId,@Name,1)",c); Add(cmd,"@CustomerId",SqlDbType.Int,request.CustomerId);Add(cmd,"@Name",SqlDbType.VarChar,request.CategoryName.Trim());var id=Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));return Ok(new{categoryId=id,message="Category created."});}catch(SqlException ex) when(ex.Number is 2601 or 2627){return Conflict(new{message="This category already exists for the customer."});}
    }
    [HttpPut("categories/{id:int}")]
    public async Task<IActionResult> UpdateCategory(int id,[FromBody] CategoryRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);if(!await HasCategoryAccessAsync(id,c,cancellationToken)||!await HasCustomerAccessAsync(request.CustomerId,c,cancellationToken))return NotFound();await using var cmd=new SqlCommand("UPDATE Category SET CustomerId=@CustomerId,CategoryName=@Name,IsActive=@Active WHERE CategoryId=@Id",c);Add(cmd,"@Id",SqlDbType.Int,id);Add(cmd,"@CustomerId",SqlDbType.Int,request.CustomerId);Add(cmd,"@Name",SqlDbType.VarChar,request.CategoryName.Trim());Add(cmd,"@Active",SqlDbType.Bit,request.IsActive);return await cmd.ExecuteNonQueryAsync(cancellationToken)==0?NotFound():Ok(new{message="Category updated."});}
    [HttpDelete("categories/{id:int}")]
    public async Task<IActionResult> DeleteCategory(int id,CancellationToken ct){await using var c=await connectionFactory.OpenAsync(ct);if(!await HasCategoryAccessAsync(id,c,ct))return NotFound();return await SoftDelete("Category","CategoryId",id,ct);}

    [HttpPost("models")]
    public async Task<IActionResult> CreateModel([FromBody] ModelRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);if(!await HasCustomerAccessAsync(request.CustomerId,c,cancellationToken))return NotFound();try{await using var cmd=new SqlCommand("INSERT INTO Model(CustomerId,ModelName,IsActive) OUTPUT INSERTED.ModelId VALUES(@CustomerId,@Name,1)",c);Add(cmd,"@CustomerId",SqlDbType.Int,request.CustomerId);Add(cmd,"@Name",SqlDbType.VarChar,request.ModelName.Trim());var id=Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));return Ok(new{modelId=id,message="Model created."});}catch(SqlException ex) when(ex.Number is 2601 or 2627){return Conflict(new{message="This model already exists for the customer."});}}
    [HttpPut("models/{id:int}")]
    public async Task<IActionResult> UpdateModel(int id,[FromBody] ModelRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);if(!await HasModelAccessAsync(id,c,cancellationToken)||!await HasCustomerAccessAsync(request.CustomerId,c,cancellationToken))return NotFound();await using var cmd=new SqlCommand("UPDATE Model SET CustomerId=@CustomerId,ModelName=@Name,IsActive=@Active WHERE ModelId=@Id",c);Add(cmd,"@Id",SqlDbType.Int,id);Add(cmd,"@CustomerId",SqlDbType.Int,request.CustomerId);Add(cmd,"@Name",SqlDbType.VarChar,request.ModelName.Trim());Add(cmd,"@Active",SqlDbType.Bit,request.IsActive);return await cmd.ExecuteNonQueryAsync(cancellationToken)==0?NotFound():Ok(new{message="Model updated."});}
    [HttpDelete("models/{id:int}")]
    public async Task<IActionResult> DeleteModel(int id,CancellationToken ct){await using var c=await connectionFactory.OpenAsync(ct);if(!await HasModelAccessAsync(id,c,ct))return NotFound();return await SoftDelete("Model","ModelId",id,ct);}

    [HttpPost("lines")]
    public async Task<IActionResult> CreateLine([FromBody] LineRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);if(!await HasCategoryAccessAsync(request.CategoryId,c,cancellationToken))return NotFound();try{await using var cmd=new SqlCommand("INSERT INTO ProductionLine(CategoryId,LineName,SAPLocation,IsActive) OUTPUT INSERTED.LineId VALUES(@CategoryId,@Name,@SAP,1)",c);Add(cmd,"@CategoryId",SqlDbType.Int,request.CategoryId);Add(cmd,"@Name",SqlDbType.VarChar,request.LineName.Trim());Add(cmd,"@SAP",SqlDbType.VarChar,request.SapLocation?.Trim());var id=Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));return Ok(new{lineId=id,message="Production line created."});}catch(SqlException ex) when(ex.Number is 2601 or 2627){return Conflict(new{message="This line already exists for the category."});}}
    [HttpPut("lines/{id:int}")]
    public async Task<IActionResult> UpdateLine(int id,[FromBody] LineRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);if(!await HasLineAccessAsync(id,c,cancellationToken)||!await HasCategoryAccessAsync(request.CategoryId,c,cancellationToken))return NotFound();await using var cmd=new SqlCommand("UPDATE ProductionLine SET CategoryId=@CategoryId,LineName=@Name,SAPLocation=@SAP,IsActive=@Active WHERE LineId=@Id",c);Add(cmd,"@Id",SqlDbType.Int,id);Add(cmd,"@CategoryId",SqlDbType.Int,request.CategoryId);Add(cmd,"@Name",SqlDbType.VarChar,request.LineName.Trim());Add(cmd,"@SAP",SqlDbType.VarChar,request.SapLocation?.Trim());Add(cmd,"@Active",SqlDbType.Bit,request.IsActive);return await cmd.ExecuteNonQueryAsync(cancellationToken)==0?NotFound():Ok(new{message="Line updated."});}
    [HttpDelete("lines/{id:int}")]
    public async Task<IActionResult> DeleteLine(int id,CancellationToken ct){await using var c=await connectionFactory.OpenAsync(ct);if(!await HasLineAccessAsync(id,c,ct))return NotFound();return await SoftDelete("ProductionLine","LineId",id,ct);}

    [HttpPost("line-models")]
    public async Task<IActionResult> AddLineModel([FromBody] LineModelRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);if(!await HasLineAccessAsync(request.LineId,c,cancellationToken)||!await HasModelAccessAsync(request.ModelId,c,cancellationToken))return NotFound();try{await using var cmd=new SqlCommand("""INSERT INTO LineModel(LineId,ModelId,IsActive) OUTPUT INSERTED.LineModelId SELECT @LineId,@ModelId,1 WHERE EXISTS (SELECT 1 FROM ProductionLine pl INNER JOIN Category cat ON cat.CategoryId=pl.CategoryId INNER JOIN Model m ON m.CustomerId=cat.CustomerId WHERE pl.LineId=@LineId AND m.ModelId=@ModelId AND pl.IsActive=1 AND cat.IsActive=1 AND m.IsActive=1)""",c);Add(cmd,"@LineId",SqlDbType.Int,request.LineId);Add(cmd,"@ModelId",SqlDbType.Int,request.ModelId);var result=await cmd.ExecuteScalarAsync(cancellationToken);if(result is null)return BadRequest(new{message="The selected model does not belong to the customer of this line."});return Ok(new{lineModelId=Convert.ToInt32(result),message="Model mapped to line."});}catch(SqlException ex) when(ex.Number is 2601 or 2627){return Conflict(new{message="This model is already mapped to the line."});}}
    [HttpDelete("line-models/{id:int}")]
    public async Task<IActionResult> DeleteLineModel(int id,CancellationToken ct){await using var c=await connectionFactory.OpenAsync(ct);if(!await HasLineModelAccessAsync(id,c,ct))return NotFound();return await SoftDelete("LineModel","LineModelId",id,ct);}

    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans([FromQuery] int? customerId,[FromQuery] int? categoryId,[FromQuery] int? lineId,[FromQuery] int? modelId,[FromQuery] int? year,[FromQuery] int? month,CancellationToken cancellationToken)
    {
        await using var c=await connectionFactory.OpenAsync(cancellationToken);
        var sql="""SELECT km.KPI_MasterId,km.CustomerId,c.CustomerName,km.CategoryId,cat.CategoryName,km.LineId,pl.LineName,km.ModelId,m.ModelName,km.KPIYEAR,km.KPIMonth,km.ProductionPlan,km.UPHTarget,km.UPPHTarget,km.CPHTarget,km.FPYTarget,km.FTYTarget,km.RTYTarget,km.OSDTarget,km.OTTarget,km.IsActive FROM KPI_Master km INNER JOIN Customer c ON c.CustomerId=km.CustomerId INNER JOIN Category cat ON cat.CategoryId=km.CategoryId INNER JOIN ProductionLine pl ON pl.LineId=km.LineId INNER JOIN Model m ON m.ModelId=km.ModelId WHERE (@CustomerId IS NULL OR km.CustomerId=@CustomerId) AND (@CategoryId IS NULL OR km.CategoryId=@CategoryId) AND (@LineId IS NULL OR km.LineId=@LineId) AND (@ModelId IS NULL OR km.ModelId=@ModelId) AND (@Year IS NULL OR km.KPIYEAR=@Year) AND (@Month IS NULL OR km.KPIMonth=@Month) AND (@IsSuperAdmin=1 OR (EXISTS(SELECT 1 FROM PlantCustomer pc WHERE pc.CustomerId=km.CustomerId AND pc.PlantId=@PlantId) )) ORDER BY km.KPIYEAR DESC,km.KPIMonth DESC,c.CustomerName,pl.LineName,m.ModelName""";
        await using var cmd=new SqlCommand(sql,c);Add(cmd,"@CustomerId",SqlDbType.Int,customerId);Add(cmd,"@CategoryId",SqlDbType.Int,categoryId);Add(cmd,"@LineId",SqlDbType.Int,lineId);Add(cmd,"@ModelId",SqlDbType.Int,modelId);Add(cmd,"@Year",SqlDbType.Int,year);Add(cmd,"@Month",SqlDbType.Int,month);AddScope(cmd);
        var rows=new List<object>();await using var r=await cmd.ExecuteReaderAsync(cancellationToken);while(await r.ReadAsync(cancellationToken))rows.Add(new{planId=r.GetInt32(0),customerId=r.GetInt32(1),customerName=r.GetString(2),categoryId=r.GetInt32(3),categoryName=r.GetString(4),lineId=r.GetInt32(5),lineName=r.GetString(6),modelId=r.GetInt32(7),modelName=r.GetString(8),year=r.GetInt32(9),month=r.GetInt32(10),productionPlan=GetDecimal(r,11),uphTarget=GetDecimal(r,12),upphTarget=GetDecimal(r,13),cphTarget=GetDecimal(r,14),fpyTarget=GetDecimal(r,15),ftyTarget=GetDecimal(r,16),rtyTarget=GetDecimal(r,17),osdTarget=GetDecimal(r,18),otTarget=GetDecimal(r,19),isActive=r.GetBoolean(20)});return Ok(rows);
    }

    [HttpPost("plans")]
    public async Task<IActionResult> UpsertPlan([FromBody] PlanRequest request,CancellationToken cancellationToken)
    {
        if(request.Month is < 1 or > 12 || request.Year < 2000) return BadRequest(new{message="Enter a valid year and month."});
        await using var c=await connectionFactory.OpenAsync(cancellationToken);
        await using var validate = new SqlCommand("""
            SELECT COUNT(1)
            FROM Category cat
            INNER JOIN ProductionLine pl ON pl.CategoryId=cat.CategoryId
            INNER JOIN Model m ON m.CustomerId=cat.CustomerId
            WHERE cat.CustomerId=@CustomerId
              AND cat.CategoryId=@CategoryId
              AND pl.LineId=@LineId
              AND m.ModelId=@ModelId
              AND (@IsSuperAdmin=1 OR (EXISTS
                  (SELECT 1 FROM PlantCustomer pc WHERE pc.CustomerId=cat.CustomerId AND pc.PlantId=@PlantId)
                  AND NOT EXISTS (SELECT 1 FROM PlantCustomer otherPc WHERE otherPc.CustomerId=cat.CustomerId AND otherPc.PlantId<>@PlantId)))
            """,c);
        Add(validate,"@CustomerId",SqlDbType.Int,request.CustomerId);
        Add(validate,"@CategoryId",SqlDbType.Int,request.CategoryId);
        Add(validate,"@LineId",SqlDbType.Int,request.LineId);
        Add(validate,"@ModelId",SqlDbType.Int,request.ModelId);
        AddScope(validate);
        if(Convert.ToInt32(await validate.ExecuteScalarAsync(cancellationToken))==0)
            return NotFound();
        const string sql="""
IF EXISTS(SELECT 1 FROM KPI_Master WHERE CustomerId=@CustomerId AND CategoryId=@CategoryId AND ModelId=@ModelId AND LineId=@LineId AND KPIYEAR=@Year AND KPIMonth=@Month)
BEGIN
 UPDATE KPI_Master SET ProductionPlan=@ProductionPlan,UPHTarget=@UPHTarget,UPPHTarget=@UPPHTarget,CPHTarget=@CPHTarget,FPYTarget=@FPYTarget,FTYTarget=@FTYTarget,RTYTarget=@RTYTarget,OSDTarget=@OSDTarget,OTTarget=@OTTarget,IsActive=@IsActive,UpdatedAt=SYSUTCDATETIME() WHERE CustomerId=@CustomerId AND CategoryId=@CategoryId AND ModelId=@ModelId AND LineId=@LineId AND KPIYEAR=@Year AND KPIMonth=@Month;
 SELECT TOP 1 KPI_MasterId FROM KPI_Master WHERE CustomerId=@CustomerId AND CategoryId=@CategoryId AND ModelId=@ModelId AND LineId=@LineId AND KPIYEAR=@Year AND KPIMonth=@Month;
END
ELSE
BEGIN
 INSERT INTO KPI_Master(CustomerId,CategoryId,ModelId,LineId,KPIYEAR,KPIMonth,ProductionPlan,UPHTarget,UPPHTarget,CPHTarget,FPYTarget,FTYTarget,RTYTarget,OSDTarget,OTTarget,IsActive,CreatedAt,UpdatedAt) OUTPUT INSERTED.KPI_MasterId VALUES(@CustomerId,@CategoryId,@ModelId,@LineId,@Year,@Month,@ProductionPlan,@UPHTarget,@UPPHTarget,@CPHTarget,@FPYTarget,@FTYTarget,@RTYTarget,@OSDTarget,@OTTarget,@IsActive,SYSUTCDATETIME(),SYSUTCDATETIME());
END
""";
        await using var cmd=new SqlCommand(sql,c);Add(cmd,"@CustomerId",SqlDbType.Int,request.CustomerId);Add(cmd,"@CategoryId",SqlDbType.Int,request.CategoryId);Add(cmd,"@ModelId",SqlDbType.Int,request.ModelId);Add(cmd,"@LineId",SqlDbType.Int,request.LineId);Add(cmd,"@Year",SqlDbType.Int,request.Year);Add(cmd,"@Month",SqlDbType.Int,request.Month);AddDecimal(cmd,"@ProductionPlan",request.ProductionPlan);AddDecimal(cmd,"@UPHTarget",request.UphTarget);AddDecimal(cmd,"@UPPHTarget",request.UpphTarget);AddDecimal(cmd,"@CPHTarget",request.CphTarget);AddDecimal(cmd,"@FPYTarget",request.FpyTarget);AddDecimal(cmd,"@FTYTarget",request.FtyTarget);AddDecimal(cmd,"@RTYTarget",request.RtyTarget);AddDecimal(cmd,"@OSDTarget",request.OsdTarget);AddDecimal(cmd,"@OTTarget",request.OtTarget);Add(cmd,"@IsActive",SqlDbType.Bit,request.IsActive);var id=Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));return Ok(new{planId=id,message="KPI plan saved."});
    }
    [HttpDelete("plans/{id:int}")]
    public async Task<IActionResult> DeletePlan(int id,CancellationToken ct){await using var c=await connectionFactory.OpenAsync(ct);await using var check=new SqlCommand("SELECT CustomerId FROM KPI_Master WHERE KPI_MasterId=@Id",c);Add(check,"@Id",SqlDbType.Int,id);var customerId=await check.ExecuteScalarAsync(ct);if(customerId is null||!await HasCustomerAccessAsync(Convert.ToInt32(customerId),c,ct))return NotFound();return await SoftDelete("KPI_Master","KPI_MasterId",id,ct);}

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
        if(IsSuperAdmin)return true;
        await using var cmd=new SqlCommand("""
            SELECT COUNT(1)
            FROM PlantCustomer pc
            WHERE pc.PlantId=@PlantId
              AND pc.CustomerId=@CustomerId
              AND NOT EXISTS
              (
                  SELECT 1 FROM PlantCustomer otherPlant
                  WHERE otherPlant.CustomerId=pc.CustomerId
                    AND otherPlant.PlantId<>pc.PlantId
              )
            """,c);
        Add(cmd,"@PlantId",SqlDbType.Int,CurrentPlantId);Add(cmd,"@CustomerId",SqlDbType.Int,customerId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }
    private async Task<bool> HasCategoryAccessAsync(int categoryId,SqlConnection c,CancellationToken ct)
    {
        if(IsSuperAdmin)return true;
        await using var cmd=new SqlCommand("""
            SELECT COUNT(1) FROM Category cat
            INNER JOIN PlantCustomer pc ON pc.CustomerId=cat.CustomerId
            WHERE cat.CategoryId=@Id AND pc.PlantId=@PlantId
              AND NOT EXISTS (SELECT 1 FROM PlantCustomer otherPc WHERE otherPc.CustomerId=cat.CustomerId AND otherPc.PlantId<>@PlantId)
            """,c);
        Add(cmd,"@Id",SqlDbType.Int,categoryId);Add(cmd,"@PlantId",SqlDbType.Int,CurrentPlantId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }
    private async Task<bool> HasModelAccessAsync(int modelId,SqlConnection c,CancellationToken ct)
    {
        if(IsSuperAdmin)return true;
        await using var cmd=new SqlCommand("""
            SELECT COUNT(1) FROM Model m
            INNER JOIN PlantCustomer pc ON pc.CustomerId=m.CustomerId
            WHERE m.ModelId=@Id AND pc.PlantId=@PlantId
              AND NOT EXISTS (SELECT 1 FROM PlantCustomer otherPc WHERE otherPc.CustomerId=m.CustomerId AND otherPc.PlantId<>@PlantId)
            """,c);
        Add(cmd,"@Id",SqlDbType.Int,modelId);Add(cmd,"@PlantId",SqlDbType.Int,CurrentPlantId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }
    private async Task<bool> HasLineAccessAsync(int lineId,SqlConnection c,CancellationToken ct)
    {
        if(IsSuperAdmin)return true;
        await using var cmd=new SqlCommand("""
            SELECT COUNT(1) FROM ProductionLine pl
            INNER JOIN Category cat ON cat.CategoryId=pl.CategoryId
            INNER JOIN PlantCustomer pc ON pc.CustomerId=cat.CustomerId
            WHERE pl.LineId=@Id AND pc.PlantId=@PlantId
              AND NOT EXISTS (SELECT 1 FROM PlantCustomer otherPc WHERE otherPc.CustomerId=cat.CustomerId AND otherPc.PlantId<>@PlantId)
            """,c);
        Add(cmd,"@Id",SqlDbType.Int,lineId);Add(cmd,"@PlantId",SqlDbType.Int,CurrentPlantId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct))==1;
    }
    private async Task<bool> HasLineModelAccessAsync(int lineModelId,SqlConnection c,CancellationToken ct)
    {
        if(IsSuperAdmin)return true;
        await using var cmd=new SqlCommand("""
            SELECT COUNT(1) FROM LineModel lm
            INNER JOIN ProductionLine pl ON pl.LineId=lm.LineId
            INNER JOIN Category cat ON cat.CategoryId=pl.CategoryId
            INNER JOIN PlantCustomer pc ON pc.CustomerId=cat.CustomerId
            WHERE lm.LineModelId=@Id AND pc.PlantId=@PlantId
              AND NOT EXISTS (SELECT 1 FROM PlantCustomer otherPc WHERE otherPc.CustomerId=cat.CustomerId AND otherPc.PlantId<>@PlantId)
            """,c);
        Add(cmd,"@Id",SqlDbType.Int,lineModelId);Add(cmd,"@PlantId",SqlDbType.Int,CurrentPlantId);
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
public sealed record CategoryRequest(int CustomerId,string CategoryName,bool IsActive=true);
public sealed record ModelRequest(int CustomerId,string ModelName,bool IsActive=true);
public sealed record LineRequest(int CategoryId,string LineName,string? SapLocation,bool IsActive=true);
public sealed record LineModelRequest(int LineId,int ModelId);
public sealed record PlanRequest(int CustomerId,int CategoryId,int LineId,int ModelId,int Year,int Month,decimal? ProductionPlan,decimal? UphTarget,decimal? UpphTarget,decimal? CphTarget,decimal? FpyTarget,decimal? FtyTarget,decimal? RtyTarget,decimal? OsdTarget,decimal? OtTarget,bool IsActive=true);
