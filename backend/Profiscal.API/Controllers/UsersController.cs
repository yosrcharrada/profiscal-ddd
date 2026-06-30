using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Profiscal.Application.Common.Interfaces;
using Profiscal.Contracts.Common;
using Profiscal.Contracts.Requests;
using Profiscal.Contracts.Responses;

namespace Profiscal.API.Controllers;

/// <summary>User administration. Admin role required on every endpoint.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
[Produces("application/json")]
public class UsersController(IUserAdminService userAdminService) : ControllerBase
{
    /// <summary>Paged list of users, optionally filtered by name/email.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<UserResponse>>), 200)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var result = await userAdminService.GetUsersAsync(new PaginationRequest(page, pageSize), search, ct);
        return Ok(ApiResponse<PagedResponse<UserResponse>>.Ok(result));
    }

    /// <summary>Replace a user's role (revokes their sessions so the new role takes effect).</summary>
    [HttpPut("{id:guid}/role")]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> UpdateRole(Guid id, [FromBody] UpdateUserRoleRequest request, CancellationToken ct)
    {
        var result = await userAdminService.UpdateRoleAsync(CurrentUserId, id, request.Role, ct);
        return Ok(ApiResponse<UserResponse>.Ok(result));
    }

    /// <summary>Lock a user's account and revoke all their sessions.</summary>
    [HttpPost("{id:guid}/lock")]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), 200)]
    public async Task<IActionResult> Lock(Guid id, CancellationToken ct)
    {
        var result = await userAdminService.SetLockAsync(CurrentUserId, id, locked: true, ct);
        return Ok(ApiResponse<UserResponse>.Ok(result));
    }

    /// <summary>Unlock a user's account.</summary>
    [HttpPost("{id:guid}/unlock")]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), 200)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken ct)
    {
        var result = await userAdminService.SetLockAsync(CurrentUserId, id, locked: false, ct);
        return Ok(ApiResponse<UserResponse>.Ok(result));
    }

    /// <summary>Recent authentication activity for a user (login attempts, sessions, changes).</summary>
    [HttpGet("{id:guid}/activity")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AuditLogResponse>>), 200)]
    public async Task<IActionResult> Activity(Guid id, [FromQuery] int take = 20, CancellationToken ct = default)
    {
        var result = await userAdminService.GetActivityAsync(id, take, ct);
        return Ok(ApiResponse<IReadOnlyList<AuditLogResponse>>.Ok(result));
    }

    private Guid CurrentUserId => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);
}
