using Dixon.CommandCenter.API.Data;
using Dixon.CommandCenter.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize]
[Route("api/catalog")]
public sealed class CatalogController(CatalogRepository catalog) : ControllerBase
{
    [HttpGet("customers")]
    [ProducesResponseType<IReadOnlyList<CustomerResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCustomers(CancellationToken cancellationToken)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
        var roleName = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        return Ok(await catalog.GetCustomersAsync(userId, roleName, cancellationToken));
    }
}