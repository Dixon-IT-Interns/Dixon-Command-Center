using Dixon.CommandCenter.API.Data;
using Dixon.CommandCenter.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        return Ok(await catalog.GetCustomersAsync(cancellationToken));
    }
}