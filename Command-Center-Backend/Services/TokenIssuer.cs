using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Dixon.CommandCenter.API.Models;
using Microsoft.IdentityModel.Tokens;

namespace Dixon.CommandCenter.API.Services;

public sealed class TokenIssuer(SecurityKey signingKey)
{
    public LoginResponse Issue(AuthenticatedUser user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.RoleName),
                new Claim("roleId", user.RoleId.ToString()),
                new Claim("plantId", user.PlantId.ToString()),
                new Claim("plantName", user.PlantName)
            ],
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expiresAt, user);
    }
}