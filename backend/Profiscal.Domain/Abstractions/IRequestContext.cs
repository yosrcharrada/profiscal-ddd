namespace Profiscal.Application.Common.Interfaces;

/// <summary>Ambient info about the current HTTP request, used for session tracking and auditing.</summary>
public interface IRequestContext
{
    string? IpAddress { get; }
    string? UserAgent { get; }
}
