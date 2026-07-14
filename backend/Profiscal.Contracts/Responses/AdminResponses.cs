namespace Profiscal.Contracts.Responses;

/// <summary>A manager entry for assignment dropdowns and the admin directory.</summary>
public record ManagerResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    int ConsultantCount
);

/// <summary>One row of the platform-wide activity feed (all users).</summary>
public record GlobalAuditResponse(
    string Event,
    string? Detail,
    string Email,
    string? UserName,
    string? IpAddress,
    DateTime CreatedAt
);

/// <summary>Headline numbers for the admin dashboard.</summary>
public record AdminOverviewResponse(
    int TotalUsers,
    int Admins,
    int Managers,
    int Consultants,
    int LockedUsers,
    int NewUsersThisMonth,
    int PendingReclamations,
    int ResolvedReclamations,
    int TotalConsultations,
    int ConsultationsThisWeek,
    int TasksPending,
    int TasksInProgress,
    int TasksSubmitted,
    int TasksApproved,
    IReadOnlyList<UserResponse> RecentUsers,
    IReadOnlyList<GlobalAuditResponse> RecentActivity
);
