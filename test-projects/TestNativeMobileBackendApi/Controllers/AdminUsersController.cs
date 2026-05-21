using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TestNativeMobileBackendApi.Configuration;
using TestNativeMobileBackendApi.Interfaces;
using TestNativeMobileBackendApi.Models;
using TestNativeMobileBackendApi.Models.Admin;

namespace TestNativeMobileBackendApi.Controllers;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[ApiController]
[Route("api/admin/users")]
public class AdminUsersController : ControllerBase
{
    private readonly IUserRepository _users;

    public AdminUsersController(IUserRepository users)
    {
        _users = users;
    }

    [HttpPut("{id:guid}/role")]
    public IActionResult UpdateRole(Guid id, [FromBody] UpdateUserRoleRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var role = request.Role.Trim();
        if (role is not AppRoles.User and not AppRoles.Admin)
        {
            return BadRequest($"Role must be '{AppRoles.User}' or '{AppRoles.Admin}'.");
        }

        var callerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (callerId is not null && Guid.TryParse(callerId, out var currentUserId) && currentUserId == id)
        {
            return BadRequest("Self role changes are not allowed.");
        }

        var updated = _users.UpdateRole(id, role);
        return updated ? Ok() : NotFound();
    }
}
