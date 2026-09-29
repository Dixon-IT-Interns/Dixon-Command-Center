using System.ComponentModel.DataAnnotations;

namespace Dixon.CommandCenter.API.Models;

/// <summary>Credentials used to sign in to the command center.</summary>
public sealed record LoginRequest
{
    [Required, EmailAddress, MaxLength(150)]
    public required string Email { get; init; }

    [Required, MinLength(8), MaxLength(128)]
    public required string Password { get; init; }
}

/// <summary>Authenticated user details and the bearer token.</summary>
public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, AuthenticatedUser User);

/// <summary>User identity and assigned dashboard role.</summary>
public sealed record AuthenticatedUser(int UserId, string FullName, string Email, int RoleId, string RoleName, int PlantId, string PlantName);

/// <summary>Details for a user visible in administration.</summary>
public sealed record UserResponse(int UserId, string FullName, string? Email, int RoleId, string RoleName, int PlantId, string PlantName, bool IsActive);

/// <summary>Fields required to add a user.</summary>
public sealed record CreateUserRequest
{
    [Required, MaxLength(100)]
    public required string FullName { get; init; }

    [Required, EmailAddress, MaxLength(150)]
    public required string Email { get; init; }

    [Required, MinLength(8), MaxLength(128)]
    public required string Password { get; init; }

    [Range(1, int.MaxValue)]
    public int RoleId { get; init; }

    [Range(1, int.MaxValue)]
    public int PlantId { get; init; }
}

/// <summary>Fields used to create the first administrator during initial setup.</summary>
public sealed record BootstrapAdminRequest
{
    [Required, MaxLength(100)]
    public required string FullName { get; init; }

    [Required, EmailAddress, MaxLength(150)]
    public required string Email { get; init; }

    [Required, MinLength(8), MaxLength(128)]
    public required string Password { get; init; }
}

/// <summary>Role available for user assignment.</summary>
public sealed record RoleResponse(int RoleId, string RoleName);

/// <summary>Plant available for user assignment.</summary>
public sealed record PlantResponse(int PlantId, string PlantName, string? Location);

/// <summary>Catalog customers with their manufacturing structure.</summary>
public sealed record CustomerResponse(int CustomerId, string CustomerName, IReadOnlyList<ModelResponse> Models, IReadOnlyList<CategoryResponse> Categories);

/// <summary>Customer product model.</summary>
public sealed record ModelResponse(int ModelId, string ModelName);

/// <summary>Customer manufacturing category.</summary>
public sealed record CategoryResponse(int CategoryId, string CategoryName, IReadOnlyList<ProductionLineResponse> ProductionLines);

/// <summary>Production line belonging to a category.</summary>
public sealed record ProductionLineResponse(int LineId, string LineName, string? SAPLocation);