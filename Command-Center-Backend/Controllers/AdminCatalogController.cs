using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dixon.CommandCenter.API.Data;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/catalog")]
public sealed class AdminCatalogController(SqlConnectionFactory connectionFactory) : ControllerBase
{
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        var plants = await ReadAsync(connection, "SELECT PlantId,PlantName,Location,IsActive FROM Plant ORDER BY PlantName", r => new { plantId=r.GetInt32(0), plantName=r.GetString(1), location=r.IsDBNull(2)?null:r.GetString(2), isActive=r.GetBoolean(3) }, cancellationToken);
        var customers = await ReadAsync(connection, "SELECT CustomerId,CustomerName,IsActive FROM Customer ORDER BY CustomerName", r => new { customerId=r.GetInt32(0), customerName=r.GetString(1), isActive=r.GetBoolean(2) }, cancellationToken);
        var categories = await ReadAsync(connection, "SELECT CategoryId,CustomerId,CategoryName,IsActive FROM Category ORDER BY CustomerId,CategoryName", r => new { categoryId=r.GetInt32(0), customerId=r.GetInt32(1), categoryName=r.GetString(2), isActive=r.GetBoolean(3) }, cancellationToken);
        var models = await ReadAsync(connection, "SELECT ModelId,CustomerId,ModelName,IsActive FROM Model ORDER BY CustomerId,ModelName", r => new { modelId=r.GetInt32(0), customerId=r.GetInt32(1), modelName=r.GetString(2), isActive=r.GetBoolean(3) }, cancellationToken);
        var lines = await ReadAsync(connection, "SELECT pl.LineId,pl.CategoryId,pl.LineName,pl.SAPLocation,pl.IsActive,cat.CustomerId FROM ProductionLine pl INNER JOIN Category cat ON cat.CategoryId=pl.CategoryId ORDER BY cat.CustomerId,pl.LineName", r => new { lineId=r.GetInt32(0), categoryId=r.GetInt32(1), lineName=r.GetString(2), sapLocation=r.IsDBNull(3)?null:r.GetString(3), isActive=r.GetBoolean(4), customerId=r.GetInt32(5) }, cancellationToken);
        var lineModels = await ReadAsync(connection, "SELECT lm.LineModelId,lm.LineId,lm.ModelId,lm.IsActive,m.ModelName FROM LineModel lm INNER JOIN Model m ON m.ModelId=lm.ModelId ORDER BY lm.LineId,m.ModelName", r => new { lineModelId=r.GetInt32(0), lineId=r.GetInt32(1), modelId=r.GetInt32(2), isActive=r.GetBoolean(3), modelName=r.GetString(4) }, cancellationToken);
        return Ok(new { plants, customers, categories, models, lines, lineModels });
    }

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

    [HttpPut("plants/{id:int}")]
    public async Task<IActionResult> UpdatePlant(int id,[FromBody] PlantRequest request,CancellationToken cancellationToken)
    {
        await using var connection=await connectionFactory.OpenAsync(cancellationToken); await using var cmd=new SqlCommand("UPDATE Plant SET PlantName=@Name,Location=@Location,IsActive=@Active WHERE PlantId=@Id",connection);
        Add(cmd,"@Id",SqlDbType.Int,id); Add(cmd,"@Name",SqlDbType.VarChar,request.PlantName.Trim()); Add(cmd,"@Location",SqlDbType.VarChar,request.Location?.Trim()); Add(cmd,"@Active",SqlDbType.Bit,request.IsActive); var n=await cmd.ExecuteNonQueryAsync(cancellationToken); return n==0?NotFound():Ok(new{message="Plant updated."});
    }
    [HttpDelete("plants/{id:int}")]
    public Task<IActionResult> DeletePlant(int id,CancellationToken ct)=>SoftDelete("Plant","PlantId",id,ct);

    [HttpPost("customers")]
    public async Task<IActionResult> CreateCustomer([FromBody] NameRequest request,CancellationToken cancellationToken)=>await CreateName("Customer","CustomerId","CustomerName",request.Name,cancellationToken);
    [HttpPut("customers/{id:int}")]
    public Task<IActionResult> UpdateCustomer(int id,[FromBody] EntityActiveRequest request,CancellationToken ct)=>UpdateName("Customer","CustomerId","CustomerName",id,request.Name,request.IsActive,ct);
    [HttpDelete("customers/{id:int}")]
    public Task<IActionResult> DeleteCustomer(int id,CancellationToken ct)=>SoftDelete("Customer","CustomerId",id,ct);

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CategoryRequest request,CancellationToken cancellationToken)
    {
        await using var c=await connectionFactory.OpenAsync(cancellationToken); try{await using var cmd=new SqlCommand("INSERT INTO Category(CustomerId,CategoryName,IsActive) OUTPUT INSERTED.CategoryId VALUES(@CustomerId,@Name,1)",c); Add(cmd,"@CustomerId",SqlDbType.Int,request.CustomerId);Add(cmd,"@Name",SqlDbType.VarChar,request.CategoryName.Trim());var id=Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));return Ok(new{categoryId=id,message="Category created."});}catch(SqlException ex) when(ex.Number is 2601 or 2627){return Conflict(new{message="This category already exists for the customer."});}
    }
    [HttpPut("categories/{id:int}")]
    public async Task<IActionResult> UpdateCategory(int id,[FromBody] CategoryRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);await using var cmd=new SqlCommand("UPDATE Category SET CustomerId=@CustomerId,CategoryName=@Name,IsActive=@Active WHERE CategoryId=@Id",c);Add(cmd,"@Id",SqlDbType.Int,id);Add(cmd,"@CustomerId",SqlDbType.Int,request.CustomerId);Add(cmd,"@Name",SqlDbType.VarChar,request.CategoryName.Trim());Add(cmd,"@Active",SqlDbType.Bit,request.IsActive);return await cmd.ExecuteNonQueryAsync(cancellationToken)==0?NotFound():Ok(new{message="Category updated."});}
    [HttpDelete("categories/{id:int}")]
    public Task<IActionResult> DeleteCategory(int id,CancellationToken ct)=>SoftDelete("Category","CategoryId",id,ct);

    [HttpPost("models")]
    public async Task<IActionResult> CreateModel([FromBody] ModelRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);try{await using var cmd=new SqlCommand("INSERT INTO Model(CustomerId,ModelName,IsActive) OUTPUT INSERTED.ModelId VALUES(@CustomerId,@Name,1)",c);Add(cmd,"@CustomerId",SqlDbType.Int,request.CustomerId);Add(cmd,"@Name",SqlDbType.VarChar,request.ModelName.Trim());var id=Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));return Ok(new{modelId=id,message="Model created."});}catch(SqlException ex) when(ex.Number is 2601 or 2627){return Conflict(new{message="This model already exists for the customer."});}}
    [HttpPut("models/{id:int}")]
    public async Task<IActionResult> UpdateModel(int id,[FromBody] ModelRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);await using var cmd=new SqlCommand("UPDATE Model SET CustomerId=@CustomerId,ModelName=@Name,IsActive=@Active WHERE ModelId=@Id",c);Add(cmd,"@Id",SqlDbType.Int,id);Add(cmd,"@CustomerId",SqlDbType.Int,request.CustomerId);Add(cmd,"@Name",SqlDbType.VarChar,request.ModelName.Trim());Add(cmd,"@Active",SqlDbType.Bit,request.IsActive);return await cmd.ExecuteNonQueryAsync(cancellationToken)==0?NotFound():Ok(new{message="Model updated."});}
    [HttpDelete("models/{id:int}")]
    public Task<IActionResult> DeleteModel(int id,CancellationToken ct)=>SoftDelete("Model","ModelId",id,ct);

    [HttpPost("lines")]
    public async Task<IActionResult> CreateLine([FromBody] LineRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);try{await using var cmd=new SqlCommand("INSERT INTO ProductionLine(CategoryId,LineName,SAPLocation,IsActive) OUTPUT INSERTED.LineId VALUES(@CategoryId,@Name,@SAP,1)",c);Add(cmd,"@CategoryId",SqlDbType.Int,request.CategoryId);Add(cmd,"@Name",SqlDbType.VarChar,request.LineName.Trim());Add(cmd,"@SAP",SqlDbType.VarChar,request.SapLocation?.Trim());var id=Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));return Ok(new{lineId=id,message="Production line created."});}catch(SqlException ex) when(ex.Number is 2601 or 2627){return Conflict(new{message="This line already exists for the category."});}}
    [HttpPut("lines/{id:int}")]
    public async Task<IActionResult> UpdateLine(int id,[FromBody] LineRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);await using var cmd=new SqlCommand("UPDATE ProductionLine SET CategoryId=@CategoryId,LineName=@Name,SAPLocation=@SAP,IsActive=@Active WHERE LineId=@Id",c);Add(cmd,"@Id",SqlDbType.Int,id);Add(cmd,"@CategoryId",SqlDbType.Int,request.CategoryId);Add(cmd,"@Name",SqlDbType.VarChar,request.LineName.Trim());Add(cmd,"@SAP",SqlDbType.VarChar,request.SapLocation?.Trim());Add(cmd,"@Active",SqlDbType.Bit,request.IsActive);return await cmd.ExecuteNonQueryAsync(cancellationToken)==0?NotFound():Ok(new{message="Line updated."});}
    [HttpDelete("lines/{id:int}")]
    public Task<IActionResult> DeleteLine(int id,CancellationToken ct)=>SoftDelete("ProductionLine","LineId",id,ct);

    [HttpPost("line-models")]
    public async Task<IActionResult> AddLineModel([FromBody] LineModelRequest request,CancellationToken cancellationToken){await using var c=await connectionFactory.OpenAsync(cancellationToken);try{await using var cmd=new SqlCommand("""INSERT INTO LineModel(LineId,ModelId,IsActive) OUTPUT INSERTED.LineModelId SELECT @LineId,@ModelId,1 WHERE EXISTS (SELECT 1 FROM ProductionLine pl INNER JOIN Category cat ON cat.CategoryId=pl.CategoryId INNER JOIN Model m ON m.CustomerId=cat.CustomerId WHERE pl.LineId=@LineId AND m.ModelId=@ModelId AND pl.IsActive=1 AND cat.IsActive=1 AND m.IsActive=1)""",c);Add(cmd,"@LineId",SqlDbType.Int,request.LineId);Add(cmd,"@ModelId",SqlDbType.Int,request.ModelId);var result=await cmd.ExecuteScalarAsync(cancellationToken);if(result is null)return BadRequest(new{message="The selected model does not belong to the customer of this line."});return Ok(new{lineModelId=Convert.ToInt32(result),message="Model mapped to line."});}catch(SqlException ex) when(ex.Number is 2601 or 2627){return Conflict(new{message="This model is already mapped to the line."});}}
    [HttpDelete("line-models/{id:int}")]
    public Task<IActionResult> DeleteLineModel(int id,CancellationToken ct)=>SoftDelete("LineModel","LineModelId",id,ct);

    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans([FromQuery] int? customerId,[FromQuery] int? categoryId,[FromQuery] int? lineId,[FromQuery] int? modelId,[FromQuery] int? year,[FromQuery] int? month,CancellationToken cancellationToken)
    {
        await using var c=await connectionFactory.OpenAsync(cancellationToken);
        var sql="""SELECT km.KPI_MasterId,km.CustomerId,c.CustomerName,km.CategoryId,cat.CategoryName,km.LineId,pl.LineName,km.ModelId,m.ModelName,km.KPIYEAR,km.KPIMonth,km.ProductionPlan,km.UPHTarget,km.UPPHTarget,km.CPHTarget,km.FPYTarget,km.FTYTarget,km.RTYTarget,km.OSDTarget,km.OTTarget,km.IsActive FROM KPI_Master km INNER JOIN Customer c ON c.CustomerId=km.CustomerId INNER JOIN Category cat ON cat.CategoryId=km.CategoryId INNER JOIN ProductionLine pl ON pl.LineId=km.LineId INNER JOIN Model m ON m.ModelId=km.ModelId WHERE (@CustomerId IS NULL OR km.CustomerId=@CustomerId) AND (@CategoryId IS NULL OR km.CategoryId=@CategoryId) AND (@LineId IS NULL OR km.LineId=@LineId) AND (@ModelId IS NULL OR km.ModelId=@ModelId) AND (@Year IS NULL OR km.KPIYEAR=@Year) AND (@Month IS NULL OR km.KPIMonth=@Month) ORDER BY km.KPIYEAR DESC,km.KPIMonth DESC,c.CustomerName,pl.LineName,m.ModelName""";
        await using var cmd=new SqlCommand(sql,c);Add(cmd,"@CustomerId",SqlDbType.Int,customerId);Add(cmd,"@CategoryId",SqlDbType.Int,categoryId);Add(cmd,"@LineId",SqlDbType.Int,lineId);Add(cmd,"@ModelId",SqlDbType.Int,modelId);Add(cmd,"@Year",SqlDbType.Int,year);Add(cmd,"@Month",SqlDbType.Int,month);
        var rows=new List<object>();await using var r=await cmd.ExecuteReaderAsync(cancellationToken);while(await r.ReadAsync(cancellationToken))rows.Add(new{planId=r.GetInt32(0),customerId=r.GetInt32(1),customerName=r.GetString(2),categoryId=r.GetInt32(3),categoryName=r.GetString(4),lineId=r.GetInt32(5),lineName=r.GetString(6),modelId=r.GetInt32(7),modelName=r.GetString(8),year=r.GetInt32(9),month=r.GetInt32(10),productionPlan=GetDecimal(r,11),uphTarget=GetDecimal(r,12),upphTarget=GetDecimal(r,13),cphTarget=GetDecimal(r,14),fpyTarget=GetDecimal(r,15),ftyTarget=GetDecimal(r,16),rtyTarget=GetDecimal(r,17),osdTarget=GetDecimal(r,18),otTarget=GetDecimal(r,19),isActive=r.GetBoolean(20)});return Ok(rows);
    }

    [HttpPost("plans")]
    public async Task<IActionResult> UpsertPlan([FromBody] PlanRequest request,CancellationToken cancellationToken)
    {
        if(request.Month is < 1 or > 12 || request.Year < 2000) return BadRequest(new{message="Enter a valid year and month."});
        await using var c=await connectionFactory.OpenAsync(cancellationToken);
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
    public Task<IActionResult> DeletePlan(int id,CancellationToken ct)=>SoftDelete("KPI_Master","KPI_MasterId",id,ct);

    private async Task<IActionResult> CreateName(string table,string idColumn,string nameColumn,string? name,CancellationToken ct){if(string.IsNullOrWhiteSpace(name))return BadRequest(new{message="Name is required."});await using var c=await connectionFactory.OpenAsync(ct);try{await using var cmd=new SqlCommand($"INSERT INTO {table}({nameColumn},IsActive) OUTPUT INSERTED.{idColumn} VALUES(@Name,1)",c);Add(cmd,"@Name",SqlDbType.VarChar,name.Trim());var id=Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));return Ok(new{id,message="Created."});}catch(SqlException ex) when(ex.Number is 2601 or 2627){return Conflict(new{message="An entity with this name already exists."});}}
    private async Task<IActionResult> UpdateName(string table,string idColumn,string nameColumn,int id,string? name,bool active,CancellationToken ct){if(string.IsNullOrWhiteSpace(name))return BadRequest(new{message="Name is required."});await using var c=await connectionFactory.OpenAsync(ct);await using var cmd=new SqlCommand($"UPDATE {table} SET {nameColumn}=@Name,IsActive=@Active WHERE {idColumn}=@Id",c);Add(cmd,"@Id",SqlDbType.Int,id);Add(cmd,"@Name",SqlDbType.VarChar,name.Trim());Add(cmd,"@Active",SqlDbType.Bit,active);return await cmd.ExecuteNonQueryAsync(ct)==0?NotFound():Ok(new{message="Updated."});}
    private async Task<IActionResult> SoftDelete(string table,string idColumn,int id,CancellationToken ct){await using var c=await connectionFactory.OpenAsync(ct);await using var cmd=new SqlCommand($"UPDATE {table} SET IsActive=0 WHERE {idColumn}=@Id",c);Add(cmd,"@Id",SqlDbType.Int,id);return await cmd.ExecuteNonQueryAsync(ct)==0?NotFound():Ok(new{message="Deactivated."});}
    private static async Task<List<T>> ReadAsync<T>(SqlConnection c,string sql,Func<SqlDataReader,T> map,CancellationToken ct){var list=new List<T>();await using var cmd=new SqlCommand(sql,c);await using var r=await cmd.ExecuteReaderAsync(ct);while(await r.ReadAsync(ct))list.Add(map(r));return list;}
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
public sealed record NameRequest(string Name);
public sealed record EntityActiveRequest(string Name,bool IsActive=true);
public sealed record CategoryRequest(int CustomerId,string CategoryName,bool IsActive=true);
public sealed record ModelRequest(int CustomerId,string ModelName,bool IsActive=true);
public sealed record LineRequest(int CategoryId,string LineName,string? SapLocation,bool IsActive=true);
public sealed record LineModelRequest(int LineId,int ModelId);
public sealed record PlanRequest(int CustomerId,int CategoryId,int LineId,int ModelId,int Year,int Month,decimal? ProductionPlan,decimal? UphTarget,decimal? UpphTarget,decimal? CphTarget,decimal? FpyTarget,decimal? FtyTarget,decimal? RtyTarget,decimal? OsdTarget,decimal? OtTarget,bool IsActive=true);
