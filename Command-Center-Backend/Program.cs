using System.Security.Cryptography;
using System.Text;
using Dixon.CommandCenter.API.Data;
using Dixon.CommandCenter.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
var builder = WebApplication.CreateBuilder(args);

var jwtSecret = builder.Configuration["Jwt:Secret"];

if (string.IsNullOrWhiteSpace(jwtSecret))
{
    if (!builder.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Configure Jwt:Secret before starting outside development.");
    }

    jwtSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
    policy.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton(new TokenIssuer(signingKey));
builder.Services.AddSingleton<SqlConnectionFactory>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<CatalogRepository>();
builder.Services.AddScoped<DailyReportExcelImportService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
