using System.Security.Claims;
using Dixon.CommandCenter.API.Data;
using Dixon.CommandCenter.API.Models;
using Dixon.CommandCenter.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(UserRepository users, TokenIssuer tokens) : ControllerBase
{
    [HttpGet("setup-status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetupStatus(CancellationToken cancellationToken)
    {
        return Ok(new { setupRequired = await users.IsBootstrapRequiredAsync(cancellationToken) });
    }

    [HttpPost("bootstrap")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Bootstrap(BootstrapAdminRequest request, CancellationToken cancellationToken)
    {
        var id = await users.BootstrapAdminAsync(request, PasswordService.Hash(request.Password), cancellationToken);
        if (id is null)
        {
            return Conflict(new { message = "Initial setup is already complete." });
        }

        var storedUser = await users.FindForLoginAsync(request.Email.Trim(), cancellationToken);
        return CreatedAtAction(nameof(Me), new { }, tokens.Issue(storedUser!.User));
    }

    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var storedUser = await users.FindForLoginAsync(request.Email.Trim(), cancellationToken);
        if (storedUser is null || !PasswordService.Verify(request.Password, storedUser.PasswordHash))
        {
            return Unauthorized(new { message = "Email or password is incorrect." });
        }

        return Ok(tokens.Issue(storedUser.User));
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<AuthenticatedUser>(StatusCodes.Status200OK)]
    public IActionResult Me()
    {
        var user = new AuthenticatedUser(
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            User.Identity?.Name ?? string.Empty,
            User.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            int.Parse(User.FindFirstValue("roleId")!),
            User.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            int.Parse(User.FindFirstValue("plantId")!),
            User.FindFirstValue("plantName") ?? string.Empty);
        return Ok(user);
    }
}