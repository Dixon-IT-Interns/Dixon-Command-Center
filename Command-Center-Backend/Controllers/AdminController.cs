using Dixon.CommandCenter.API.Data;
using Dixon.CommandCenter.API.Models;
using Dixon.CommandCenter.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin")]
public sealed class AdminController(UserRepository users) : ControllerBase
{
    [HttpGet("users")]
    [ProducesResponseType<IReadOnlyList<UserResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        return Ok(await users.GetUsersAsync(cancellationToken));
    }

    [HttpGet("users/{id:int}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(int id, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(id, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpGet("roles")]
    [ProducesResponseType<IReadOnlyList<RoleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
    {
        return Ok(await users.GetRolesAsync(cancellationToken));
    }

    [HttpGet("plants")]
    [ProducesResponseType<IReadOnlyList<PlantResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlants(CancellationToken cancellationToken)
    {
        return Ok(await users.GetPlantsAsync(cancellationToken));
    }

    [HttpPost("users")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var id = await users.CreateAsync(request, PasswordService.Hash(request.Password), cancellationToken);
            var createdUser = await users.GetUserAsync(id, cancellationToken);
            return CreatedAtAction(nameof(GetUser), new { id }, createdUser);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return Conflict(new { message = "A user with this email already exists." });
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            return BadRequest(new { message = "Select an existing role and active plant." });
        }
    }
}