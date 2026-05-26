namespace FiscalPlatform.Application.Common.DTOs;

public sealed record ConsultationGeneratedDto(
    byte[] DocBytes, string Filename, ConsultationOutput Output,
    string Method, double ElapsedMs, Guid ConsultationId);

public sealed class ConsultationOutput
{
    public string ContexteFaits   { get; set; } = "";
    public string Etendue         { get; set; } = "";
    public string Abbreviations   { get; set; } = "";
    public string SommairExecutif { get; set; } = "";
    public string Analyses        { get; set; } = "";
    public string Documents       { get; set; } = "";
    public List<AnalysisRow>    AnalysisTable { get; set; } = new();
    public List<LegalSourceDto> Sources       { get; set; } = new();
    public string Method    { get; set; } = "";
    public double ElapsedMs { get; set; }
}

public sealed record AnalysisRow(string Sujet, string Analyse, string Conclusion);

public sealed class LegalSourceDto
{
    public int    Index        { get; set; }
    public string ChunkId      { get; set; } = "";
    public string DocName      { get; set; } = "";
    public string DocType      { get; set; } = "";
    public string ArticleRef   { get; set; } = "";
    public string SectionTitle { get; set; } = "";
    public string Year         { get; set; } = "";
    public string Text         { get; set; } = "";
    public double Score        { get; set; }
    public bool   IsExpert     { get; set; }
    // Shortens convention names in citations
    public string Citation
    {
        get
        {
            var name = DocName.Replace("-", " ");
            if ((DocType == "Convention" || name.Contains("convention")) && name.Contains(" "))
            {
                var countries = new[]{"maroc","france","allemagne","italie","belgique","suisse",
                    "espagne","algerie","libye","egypte","canada","turquie","senegal",
                    "mauritanie","jordanie","luxembourg"};
                var found = countries.FirstOrDefault(c => name.Contains(c));
                name = found is not null
                    ? $"Convention Tunisie-{char.ToUpper(found[0]) + found[1..]}"
                    : "Convention fiscale";
            }
            return "[" + string.Join(", ",
                new[]{ name, Year, ArticleRef }.Where(s => !string.IsNullOrEmpty(s))) + "]";
        }
    }
}

public sealed record ConversationTurn(string Role, string Content, DateTime At);
