using System.Security.Claims;
using EWasteManagement.API.Features.Admin.DTOs;
using EWasteManagement.API.Features.Admin.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EWasteManagement.API.Features.Admin.Controllers;

// Only an admin adds or removes admin accounts, and an admin edits only their own account.
[ApiController]
[Route("api/v1/admin/admins")]
[Authorize(Roles = "Admin")]
public class AdminAccountsController : ControllerBase
{
    private readonly IAdminAccountService _service;

    public AdminAccountsController(IAdminAccountService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(await _service.ListAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAdminRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await _service.CreateAsync(request, cancellationToken));

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateAdminRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
            return Unauthorized("Could not resolve the authenticated admin's id.");

        return Ok(await _service.UpdateAsync(id, adminId, request, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var adminId))
            return Unauthorized("Could not resolve the authenticated admin's id.");

        await _service.DeleteAsync(id, adminId, cancellationToken);
        return NoContent();
    }
}
