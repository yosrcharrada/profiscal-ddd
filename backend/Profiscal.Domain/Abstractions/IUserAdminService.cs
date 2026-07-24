using Profiscal.Contracts.Common;
using Profiscal.Contracts.Requests;
using Profiscal.Contracts.Responses;

namespace Profiscal.Application.Common.Interfaces;

/// <summary>Admin-only user management. Actor = the admin performing the action.</summary>
public interface IUserAdminService
{
    Task<PagedResponse<UserResponse>> GetUsersAsync(PaginationRequest pagination, string? search, string? role, CancellationToken ct = default);
    Task<UserResponse> UpdateRoleAsync(Guid actorId, Guid userId, string role, CancellationToken ct = default);
    Task<UserResponse> SetLockAsync(Guid actorId, Guid userId, bool locked, CancellationToken ct = default);

    /// <summary>Permanently delete a (locked, non-admin) account and its personal data.</summary>
    Task DeleteUserAsync(Guid actorId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogResponse>> GetActivityAsync(Guid userId, int take = 20, CancellationToken ct = default);

    /// <summary>Provision an account: generated password, credentials email, role + manager attachment.</summary>
    Task<CreateUserResponse> CreateUserAsync(Guid actorId, CreateUserRequest request, CancellationToken ct = default);

    Task<UserResponse> AssignManagerAsync(Guid actorId, Guid userId, Guid? managerId, CancellationToken ct = default);
    Task<IReadOnlyList<ManagerResponse>> GetManagersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<GlobalAuditResponse>> GetGlobalActivityAsync(int take = 50, CancellationToken ct = default);
    Task<AdminOverviewResponse> GetOverviewAsync(CancellationToken ct = default);
}
