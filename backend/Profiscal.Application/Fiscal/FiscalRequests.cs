using Profiscal.Domain.Dtos;

namespace Profiscal.Application.Fiscal;

public sealed class GenerateConsultationApiRequest
{
    public string? Reference      { get; set; }
    public string? ClientName     { get; set; }
    public string  Situation      { get; set; } = "";
    public string  FiscalQuestion { get; set; } = "";
    public List<string>? Documents { get; set; }
    public List<string>? AttachedDocumentTexts { get; set; }
    /// <summary>"concise" or "detaillee" (default).</summary>
    public string? Mode { get; set; }
}

public sealed class ChatApiRequest
{
    public string        Question { get; set; } = "";
    public List<string>? History  { get; set; }
}

public sealed class StartSessionApiRequest
{
    public Guid   ConsultationId { get; set; }
    public string ClientName     { get; set; } = "";
    public string Reference      { get; set; } = "";
}

public sealed class RefineApiRequest
{
    public string               SessionId     { get; set; } = "";
    public string               UserMessage   { get; set; } = "";
    /// <summary>The section the user selected to edit (analyses, sommaire, etendue, contextefaits, documents). Authoritative when set.</summary>
    public string?              TargetSection { get; set; }
    public ConsultationOutput?  CurrentOutput { get; set; }
    public List<LegalSourceDto>? Sources      { get; set; }
}

public sealed class EndSessionApiRequest { public string SessionId { get; set; } = ""; }

public sealed class RateApiRequest
{
    public Guid    ConsultationId { get; set; }
    public string  Reference      { get; set; } = "";
    public int     Stars          { get; set; }
    public string? Comment        { get; set; }
}

public sealed class RenameConsultationApiRequest
{
    /// <summary>New display name for the consultation (shown in histories/rails).</summary>
    public string ClientName { get; set; } = "";
}

public sealed class ExportApiRequest
{
    public string?             Reference     { get; set; }
    public string?             ClientName    { get; set; }
    public string?             Situation     { get; set; }
    public string?             FiscalQuestion { get; set; }
    public List<string>?       Documents     { get; set; }
    public ConsultationOutput  Output        { get; set; } = new();
}
