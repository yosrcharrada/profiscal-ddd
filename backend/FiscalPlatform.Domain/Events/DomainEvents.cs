namespace FiscalPlatform.Domain.Events;

public interface IDomainEvent
{
    Guid     EventId     { get; }
    DateTime OccurredAt  { get; }
}

public sealed record ConsultationGeneratedEvent(
    Guid   ConsultationId, string ClientName, string Reference) : IDomainEvent
{
    public Guid     EventId    { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record ConsultationRatedEvent(
    Guid ConsultationId, int Stars) : IDomainEvent
{
    public Guid     EventId    { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record ConsultationRefinedEvent(
    Guid ConsultationId, string SectionName) : IDomainEvent
{
    public Guid     EventId    { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}

public sealed record ConsultationSessionArchivedEvent(
    string SessionId, Guid ConsultationId) : IDomainEvent
{
    public Guid     EventId    { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
