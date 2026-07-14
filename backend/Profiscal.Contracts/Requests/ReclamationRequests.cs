namespace Profiscal.Contracts.Requests;

public record CreateReclamationRequest(string Subject, string Description, string? Category);

public record ResolveReclamationRequest(string? AdminNote);
