using Dixon.CommandCenter.API.Data;
using Dixon.CommandCenter.API.Models;
using Dixon.CommandCenter.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/admin")]
public sealed class AdminController(UserRepository users) : ControllerBase
{
    [HttpGet("users")]
    [ProducesResponseType<IReadOnlyList<UserResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        return Ok(await users.GetUsersAsync(IsSuperAdmin ? null : CurrentPlantId, IsSuperAdmin, cancellationToken));
    }

    [HttpGet("users/{id:int}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(int id, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(id, IsSuperAdmin ? null : CurrentPlantId, IsSuperAdmin, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpGet("roles")]
    [ProducesResponseType<IReadOnlyList<RoleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
    {
        return Ok(await users.GetRolesAsync(IsSuperAdmin, cancellationToken));
    }

    [HttpGet("plants")]
    [ProducesResponseType<IReadOnlyList<PlantResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlants(CancellationToken cancellationToken)
    {
        return Ok(await users.GetPlantsAsync(IsSuperAdmin ? null : CurrentPlantId, cancellationToken));
    }

    [HttpPost("users")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        if (!IsSuperAdmin && request.PlantId != CurrentPlantId)
        {
            return Forbid();
        }

        try
        {
            var scopedRequest = IsSuperAdmin ? request : request with { PlantId = CurrentPlantId };
            var id = await users.CreateAsync(scopedRequest, PasswordService.Hash(request.Password), IsSuperAdmin, cancellationToken);
            var createdUser = await users.GetUserAsync(id, IsSuperAdmin ? null : CurrentPlantId, IsSuperAdmin, cancellationToken);
            return CreatedAtAction(nameof(GetUser), new { id }, createdUser);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return Conflict(new { message = "A user with this email already exists." });
        }
        catch (PlantCustomerAssignmentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (UserRoleAssignmentException)
        {
            return Forbid();
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            return BadRequest(new
            {
                message = "Select a valid role, active plant, and existing company."
            });
        }
    }
    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var scopedUser = await users.GetUserAsync(id, IsSuperAdmin ? null : CurrentPlantId, IsSuperAdmin, cancellationToken);
        if (scopedUser is null)
        {
            return NotFound();
        }

        try
        {
            var hash = string.IsNullOrWhiteSpace(request.Password) ? null : PasswordService.Hash(request.Password);
            var scopedRequest = IsSuperAdmin ? request : request with { PlantId = CurrentPlantId };
            var updated = await users.UpdateAsync(id, scopedRequest, hash, IsSuperAdmin ? null : CurrentPlantId, IsSuperAdmin, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return Conflict(new { message = "A user with this email already exists." });
        }
        catch (PlantCustomerAssignmentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (UserRoleAssignmentException)
        {
            return Forbid();
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            return BadRequest(new { message = "Select a valid role, active plant, and existing company." });
        }
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id, CancellationToken cancellationToken)
    {
        var changed = await users.SetActiveAsync(id, false, IsSuperAdmin ? null : CurrentPlantId, IsSuperAdmin, cancellationToken);
        return changed ? Ok(new { message = "User deactivated." }) : NotFound();
    }

    [HttpPut("users/{id:int}/customers")]
    public async Task<IActionResult> UpdateUserCustomers(int id, UpdateUserCustomersRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updatedAssignments = await users.UpdateCustomerAssignmentsAsync(id, request.CustomerIds.Distinct().ToArray(), IsSuperAdmin ? null : CurrentPlantId, IsSuperAdmin, cancellationToken);
            if (!updatedAssignments) return NotFound();
            var updated = await users.GetUserAsync(id, IsSuperAdmin ? null : CurrentPlantId, IsSuperAdmin, cancellationToken);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            return BadRequest(new { message = "One or more selected brands do not exist." });
        }
        catch (PlantCustomerAssignmentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPatch("users/{id:int}/active")]
    public async Task<IActionResult> UpdateUserActive(int id, UpdateUserActiveRequest request, CancellationToken cancellationToken)
    {
        var changed = await users.SetActiveAsync(id, request.IsActive, IsSuperAdmin ? null : CurrentPlantId, IsSuperAdmin, cancellationToken);
        return changed ? Ok(new { message = request.IsActive ? "User activated." : "User deactivated." }) : NotFound();
    }

    private bool IsSuperAdmin => User.IsInRole("SuperAdmin");
    private int CurrentPlantId => int.TryParse(User.FindFirst("plantId")?.Value, out var plantId) ? plantId : 0;
}