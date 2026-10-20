<<<<<<< HEAD

using System.Security.Cryptography;
=======
>>>>>>> origin/main
using Dixon.CommandCenter.API.Data;
using Dixon.CommandCenter.API.Models;
using Dixon.CommandCenter.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
<<<<<<< HEAD
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
=======
>>>>>>> origin/main

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/admin")]
<<<<<<< HEAD
public sealed class AdminController(
    UserRepository users,
    InvitationRepository invitations,
    InvitationEmailService invitationEmail) : ControllerBase
{
    [HttpGet("users")]
    [ProducesResponseType<IReadOnlyList<UserResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(
        CancellationToken cancellationToken)
    {
        return Ok(await users.GetUsersAsync(
            IsSuperAdmin ? null : CurrentPlantId,
            IsSuperAdmin,
            cancellationToken));
=======
public sealed class AdminController(UserRepository users) : ControllerBase
{
    [HttpGet("users")]
    [ProducesResponseType<IReadOnlyList<UserResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        return Ok(await users.GetUsersAsync(IsSuperAdmin ? null : CurrentPlantId, IsSuperAdmin, cancellationToken));
>>>>>>> origin/main
    }

    [HttpGet("users/{id:int}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
<<<<<<< HEAD
    public async Task<IActionResult> GetUser(
        int id,
        CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(
            id,
            IsSuperAdmin ? null : CurrentPlantId,
            IsSuperAdmin,
            cancellationToken);

=======
    public async Task<IActionResult> GetUser(int id, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(id, IsSuperAdmin ? null : CurrentPlantId, IsSuperAdmin, cancellationToken);
>>>>>>> origin/main
        return user is null ? NotFound() : Ok(user);
    }

    [HttpGet("roles")]
    [ProducesResponseType<IReadOnlyList<RoleResponse>>(StatusCodes.Status200OK)]
<<<<<<< HEAD
    public async Task<IActionResult> GetRoles(
        CancellationToken cancellationToken)
    {
        return Ok(await users.GetRolesAsync(
            IsSuperAdmin,
            cancellationToken));
=======
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
    {
        return Ok(await users.GetRolesAsync(IsSuperAdmin, cancellationToken));
>>>>>>> origin/main
    }

    [HttpGet("plants")]
    [ProducesResponseType<IReadOnlyList<PlantResponse>>(StatusCodes.Status200OK)]
<<<<<<< HEAD
    public async Task<IActionResult> GetPlants(
        CancellationToken cancellationToken)
    {
        return Ok(await users.GetPlantsAsync(
            IsSuperAdmin ? null : CurrentPlantId,
            cancellationToken));
=======
    public async Task<IActionResult> GetPlants(CancellationToken cancellationToken)
    {
        return Ok(await users.GetPlantsAsync(IsSuperAdmin ? null : CurrentPlantId, cancellationToken));
>>>>>>> origin/main
    }

    [HttpPost("users")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
<<<<<<< HEAD
    public async Task<IActionResult> CreateUser(
        CreateUserRequest request,
        CancellationToken cancellationToken)
=======
    public async Task<IActionResult> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
>>>>>>> origin/main
    {
        if (!IsSuperAdmin && request.PlantId != CurrentPlantId)
        {
            return Forbid();
        }

        try
        {
<<<<<<< HEAD
            var scopedRequest = IsSuperAdmin
                ? request
                : request with { PlantId = CurrentPlantId };

            // The user will set their own password through the invitation link.
            var temporaryPassword = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(48));

            var temporaryPasswordHash =
                PasswordService.Hash(temporaryPassword);

            var id = await users.CreateAsync(
                scopedRequest,
                temporaryPasswordHash,
                IsSuperAdmin,
                cancellationToken);

            // Generate a secure invitation token and save its hash.
            var token = await invitations.CreateInvitationAsync(
                id,
                cancellationToken);

            var configuration = HttpContext.RequestServices
                .GetRequiredService<IConfiguration>();

            var frontendOrigin =
                configuration["Frontend:Origin"]
                ?? "http://localhost:5173";

            var setupUrl =
                $"{frontendOrigin.TrimEnd('/')}/setup-password" +
                $"?token={Uri.EscapeDataString(token)}";

            // Send the password setup link to the new user's email.
            await invitationEmail.SendInvitationAsync(
                request.Email.Trim(),
                request.FullName.Trim(),
                setupUrl,
                cancellationToken);

            var createdUser = await users.GetUserAsync(
                id,
                IsSuperAdmin ? null : CurrentPlantId,
                IsSuperAdmin,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetUser),
                new { id },
                createdUser);
        }
        catch (SqlException exception)
            when (exception.Number is 2601 or 2627)
        {
            return Conflict(new
            {
                message = "A user with this email already exists."
            });
        }
        catch (PlantCustomerAssignmentException exception)
        {
            return BadRequest(new
            {
                message = exception.Message
            });
=======
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
>>>>>>> origin/main
        }
        catch (UserRoleAssignmentException)
        {
            return Forbid();
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
            return BadRequest(new
            {
<<<<<<< HEAD
                message =
                    "Select a valid role, active plant, and existing company."
            });
        }
    }

    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> UpdateUser(
        int id,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var scopedUser = await users.GetUserAsync(
            id,
            IsSuperAdmin ? null : CurrentPlantId,
            IsSuperAdmin,
            cancellationToken);

=======
                message = "Select a valid role, active plant, and existing company."
            });
        }
    }
    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var scopedUser = await users.GetUserAsync(id, IsSuperAdmin ? null : CurrentPlantId, IsSuperAdmin, cancellationToken);
>>>>>>> origin/main
        if (scopedUser is null)
        {
            return NotFound();
        }

        try
        {
<<<<<<< HEAD
            var hash = string.IsNullOrWhiteSpace(request.Password)
                ? null
                : PasswordService.Hash(request.Password);

            var scopedRequest = IsSuperAdmin
                ? request
                : request with { PlantId = CurrentPlantId };

            var updated = await users.UpdateAsync(
                id,
                scopedRequest,
                hash,
                IsSuperAdmin ? null : CurrentPlantId,
                IsSuperAdmin,
                cancellationToken);

            return updated is null ? NotFound() : Ok(updated);
        }
        catch (SqlException exception)
            when (exception.Number is 2601 or 2627)
        {
            return Conflict(new
            {
                message = "A user with this email already exists."
            });
        }
        catch (PlantCustomerAssignmentException exception)
        {
            return BadRequest(new
            {
                message = exception.Message
            });
=======
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
>>>>>>> origin/main
        }
        catch (UserRoleAssignmentException)
        {
            return Forbid();
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
<<<<<<< HEAD
            return BadRequest(new
            {
                message =
                    "Select a valid role, active plant, and existing company."
            });
=======
            return BadRequest(new { message = "Select a valid role, active plant, and existing company." });
>>>>>>> origin/main
        }
    }

    [HttpDelete("users/{id:int}")]
<<<<<<< HEAD
    public async Task<IActionResult> DeleteUser(
        int id,
        CancellationToken cancellationToken)
    {
        var changed = await users.SetActiveAsync(
            id,
            false,
            IsSuperAdmin ? null : CurrentPlantId,
            IsSuperAdmin,
            cancellationToken);

        return changed
            ? Ok(new { message = "User deactivated." })
            : NotFound();
    }

    [HttpPut("users/{id:int}/customers")]
    public async Task<IActionResult> UpdateUserCustomers(
        int id,
        UpdateUserCustomersRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var updatedAssignments =
                await users.UpdateCustomerAssignmentsAsync(
                    id,
                    request.CustomerIds.Distinct().ToArray(),
                    IsSuperAdmin ? null : CurrentPlantId,
                    IsSuperAdmin,
                    cancellationToken);

            if (!updatedAssignments)
            {
                return NotFound();
            }

            var updated = await users.GetUserAsync(
                id,
                IsSuperAdmin ? null : CurrentPlantId,
                IsSuperAdmin,
                cancellationToken);

=======
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
>>>>>>> origin/main
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (SqlException exception) when (exception.Number == 547)
        {
<<<<<<< HEAD
            return BadRequest(new
            {
                message = "One or more selected brands do not exist."
            });
        }
        catch (PlantCustomerAssignmentException exception)
        {
            return BadRequest(new
            {
                message = exception.Message
            });
=======
            return BadRequest(new { message = "One or more selected brands do not exist." });
        }
        catch (PlantCustomerAssignmentException exception)
        {
            return BadRequest(new { message = exception.Message });
>>>>>>> origin/main
        }
    }

    [HttpPatch("users/{id:int}/active")]
<<<<<<< HEAD
    public async Task<IActionResult> UpdateUserActive(
        int id,
        UpdateUserActiveRequest request,
        CancellationToken cancellationToken)
    {
        var changed = await users.SetActiveAsync(
            id,
            request.IsActive,
            IsSuperAdmin ? null : CurrentPlantId,
            IsSuperAdmin,
            cancellationToken);

        return changed
            ? Ok(new
            {
                message = request.IsActive
                    ? "User activated."
                    : "User deactivated."
            })
            : NotFound();
    }

    private bool IsSuperAdmin => User.IsInRole("SuperAdmin");

    private int CurrentPlantId =>
        int.TryParse(User.FindFirst("plantId")?.Value, out var plantId)
            ? plantId
            : 0;
}
=======
    public async Task<IActionResult> UpdateUserActive(int id, UpdateUserActiveRequest request, CancellationToken cancellationToken)
    {
        var changed = await users.SetActiveAsync(id, request.IsActive, IsSuperAdmin ? null : CurrentPlantId, IsSuperAdmin, cancellationToken);
        return changed ? Ok(new { message = request.IsActive ? "User activated." : "User deactivated." }) : NotFound();
    }

    private bool IsSuperAdmin => User.IsInRole("SuperAdmin");
    private int CurrentPlantId => int.TryParse(User.FindFirst("plantId")?.Value, out var plantId) ? plantId : 0;
}
>>>>>>> origin/main
