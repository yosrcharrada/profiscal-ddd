namespace Profiscal.Domain.Abstractions;

/// <summary>Ambient info about the current HTTP request, used for session tracking and auditing.</summary>
public interface IRequestContext
{
    string? IpAddress { get; }
    string? UserAgent { get; }
}
