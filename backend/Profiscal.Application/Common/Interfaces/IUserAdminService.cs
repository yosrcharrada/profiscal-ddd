using Profiscal.Contracts.Common;
using Profiscal.Contracts.Responses;

namespace Profiscal.Application.Common.Interfaces;

/// <summary>Admin-only user management. Actor = the admin performing the action.</summary>
public interface IUserAdminService
{
    Task<PagedResponse<UserResponse>> GetUsersAsync(PaginationRequest pagination, string? search, CancellationToken ct = default);
    Task<UserResponse> UpdateRoleAsync(Guid actorId, Guid userId, string role, CancellationToken ct = default);
    Task<UserResponse> SetLockAsync(Guid actorId, Guid userId, bool locked, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogResponse>> GetActivityAsync(Guid userId, int take = 20, CancellationToken ct = default);
}
