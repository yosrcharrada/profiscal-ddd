namespace FiscalPlatform.Application.Common.Interfaces.Services;

public interface IBranchDetector   { HashSet<string> Detect(string situation, string question); }
public interface ICountryDetector  { (List<string> Countries, bool IsInternational) Detect(string text); }
public interface IKeywordExtractor { (List<string> Keywords, List<string> Entities) Extract(string situation, string question); }
public interface ISessionStore
{
    void   Set(string sessionId, ConversationSession session);
    ConversationSession? Get(string sessionId);
    void   Remove(string sessionId);
    bool   Exists(string sessionId);
}

public sealed class ConversationSession
{
    public string SessionId      { get; set; } = "";
    public Guid   ConsultationId { get; set; }
    public string ClientName     { get; set; } = "";
    public string Reference      { get; set; } = "";
    public List<(string Role, string Content)> History { get; set; } = new();
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}
