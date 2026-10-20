
using System.Security.Claims;
using Dixon.CommandCenter.API.Data;
using Dixon.CommandCenter.API.Models;
using Dixon.CommandCenter.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    UserRepository users,
    TokenIssuer tokens,
    InvitationRepository invitations,
    PasswordResetRepository passwordResets,
    InvitationEmailService invitationEmails,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("setup-status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetupStatus(
        CancellationToken cancellationToken)
    {
        return Ok(new
        {
            setupRequired = await users.IsBootstrapRequiredAsync(
                cancellationToken)
        });
    }

    [HttpPost("bootstrap")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Bootstrap(
        BootstrapAdminRequest request,
        CancellationToken cancellationToken)
    {
        var id = await users.BootstrapAdminAsync(
            request,
            PasswordService.Hash(request.Password),
            cancellationToken);

        if (id is null)
        {
            return Conflict(new
            {
                message = "Initial setup is already complete."
            });
        }

        var storedUser = await users.FindForLoginAsync(
            request.Email.Trim(),
            cancellationToken);

        if (storedUser is null)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    message = "Admin was created but could not be loaded."
                });
        }

        return CreatedAtAction(
            nameof(Me),
            new { },
            tokens.Issue(storedUser.User));
    }

    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var storedUser = await users.FindForLoginAsync(
            request.Email.Trim(),
            cancellationToken);

        if (storedUser is null ||
            !PasswordService.Verify(
                request.Password,
                storedUser.PasswordHash))
        {
            return Unauthorized(new
            {
                message = "Email or password is incorrect."
            });
        }

        return Ok(tokens.Issue(storedUser.User));
    }

    [HttpPost("set-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SetPassword(
        SetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new
            {
                message = "Invitation token is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Password) ||
            request.Password.Length < 8 ||
            request.Password.Length > 128)
        {
            return BadRequest(new
            {
                message = "Password must be between 8 and 128 characters."
            });
        }

        var success = await invitations.SetPasswordAsync(
            request.Token,
            request.Password,
            cancellationToken);

        if (!success)
        {
            return BadRequest(new
            {
                message =
                    "Invitation link is invalid, expired, or already used."
            });
        }

        return Ok(new
        {
            message = "Password set successfully. You can now sign in."
        });
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new
            {
                message = "Email is required."
            });
        }

        var email = request.Email.Trim();

        Console.WriteLine(
            "[PASSWORD RESET] Forgot-password request received.");

        var storedUser = await users.FindForLoginAsync(
            email,
            cancellationToken);

        // Keep the response generic to avoid revealing whether
        // an account exists.
        if (storedUser is null)
        {
            Console.WriteLine(
                "[PASSWORD RESET] No active account found for the submitted email.");

            return Ok(new
            {
                message =
                    "If an active account exists for that email, a password reset link will be sent."
            });
        }

        Console.WriteLine(
            $"[PASSWORD RESET] Active account found. UserId: {storedUser.User.UserId}");

        var token = await passwordResets.CreateResetAsync(
            storedUser.User.UserId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine(
                "[PASSWORD RESET] Token creation failed.");

            return Ok(new
            {
                message =
                    "If an active account exists for that email, a password reset link will be sent."
            });
        }

        var frontendOrigin =
            configuration["Frontend:Origin"]?.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(frontendOrigin))
        {
            Console.Error.WriteLine(
                "[PASSWORD RESET] Frontend:Origin is missing.");

            return Ok(new
            {
                message =
                    "If an active account exists for that email, a password reset link will be sent."
            });
        }

        var resetUrl =
            $"{frontendOrigin}/reset-password?token=" +
            Uri.EscapeDataString(token);

        Console.WriteLine(
            "[PASSWORD RESET] Attempting to send reset email.");

        try
        {
            await invitationEmails.SendPasswordResetAsync(
                storedUser.User.Email,
                storedUser.User.FullName,
                resetUrl,
                cancellationToken);

            Console.WriteLine(
                "[PASSWORD RESET] Email send completed successfully.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never log the reset token, reset URL, or SMTP credentials.
            Console.Error.WriteLine(
                $"[PASSWORD RESET] Email sending failed. " +
                $"Exception: {ex.GetType().Name}; Message: {ex.Message}");

            throw;
        }

        return Ok(new
        {
            message =
                "If an active account exists for that email, a password reset link will be sent."
        });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new
            {
                message = "Reset token is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Password) ||
            request.Password.Length < 8 ||
            request.Password.Length > 128)
        {
            return BadRequest(new
            {
                message = "Password must be between 8 and 128 characters."
            });
        }

        var success = await passwordResets.ResetPasswordAsync(
            request.Token,
            request.Password,
            cancellationToken);

        if (!success)
        {
            return BadRequest(new
            {
                message =
                    "Reset link is invalid, expired, or already used."
            });
        }

        return Ok(new
        {
            message = "Password reset successfully. You can now sign in."
        });
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
