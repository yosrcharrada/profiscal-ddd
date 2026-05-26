using FiscalPlatform.Application.Common.DTOs;

namespace FiscalPlatform.API.Requests;

public sealed class GenerateConsultationApiRequest
{
    public string?        Reference      { get; set; }
    public string?        ClientName     { get; set; }
    public string         Situation      { get; set; } = "";
    public string         FiscalQuestion { get; set; } = "";
    public List<string>?  Documents      { get; set; }
}

public sealed class RefineConsultationApiRequest
{
    public string              SessionId     { get; set; } = "";
    public string              UserMessage   { get; set; } = "";
    public ConsultationOutput? CurrentOutput { get; set; }
    public List<LegalSourceDto>? Sources     { get; set; }
}

public sealed class RateConsultationApiRequest
{
    public Guid    ConsultationId { get; set; }
    public string  Reference      { get; set; } = "";
    public int     Stars          { get; set; }
    public string? Comment        { get; set; }
}

public sealed class StartSessionRequest
{
    public Guid   ConsultationId { get; set; }
    public string ClientName     { get; set; } = "";
    public string Reference      { get; set; } = "";
}

public sealed class EndSessionRequest { public string SessionId { get; set; } = ""; }

public sealed class ChatApiRequest
{
    public string        Question { get; set; } = "";
    public List<string>? History  { get; set; }
}
