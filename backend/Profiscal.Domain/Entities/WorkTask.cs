using Profiscal.Domain.Enums;

namespace Profiscal.Domain.Entities;

/// <summary>
/// A consultation task assigned by a manager to one of their consultants.
/// The consultant works on it (optionally with collaborators), links the
/// generated consultation, and submits it back for the manager's review.
/// </summary>
public class WorkTask
{
    public Guid   Id          { get; set; } = Guid.NewGuid();
    public string Title       { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ClientName  { get; set; } = string.Empty;

    public WorkTaskStatus   Status   { get; set; } = WorkTaskStatus.Pending;
    public WorkTaskPriority Priority { get; set; } = WorkTaskPriority.Medium;
    public DateTime?        DueDate  { get; set; }

    public Guid    ManagerId    { get; set; }
    public AppUser Manager      { get; set; } = null!;
    public Guid    ConsultantId { get; set; }
    public AppUser Consultant   { get; set; } = null!;

    /// <summary>The FiscalConsultation the consultant attached when submitting.</summary>
    public Guid?   ConsultationId { get; set; }
    public string? SubmitNote     { get; set; }

    public DateTime  CreatedAt   { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt   { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt  { get; set; }

    public ICollection<WorkTaskCollaborator> Collaborators { get; set; } = [];
}

/// <summary>A consultant invited to collaborate on someone else's task.</summary>
public class WorkTaskCollaborator
{
    public Guid     Id         { get; set; } = Guid.NewGuid();
    public Guid     WorkTaskId { get; set; }
    public WorkTask WorkTask   { get; set; } = null!;
    public Guid     UserId     { get; set; }
    public AppUser  User       { get; set; } = null!;
    public DateTime AddedAt    { get; set; } = DateTime.UtcNow;
}
