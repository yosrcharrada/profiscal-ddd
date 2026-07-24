namespace Profiscal.Domain.Contracts.Requests;

public record CreateWorkTaskRequest(
    string Title,
    string Description,
    string? ClientName,
    Guid ConsultantId,
    string? Priority,
    DateTime? DueDate,
    IEnumerable<string>? CollaboratorEmails
);

public record SubmitWorkTaskRequest(Guid? ConsultationId, string? Note);

public record AddCollaboratorRequest(string Email);
