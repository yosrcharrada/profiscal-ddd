using FiscalPlatform.Application.Common.Interfaces.Services;

namespace FiscalPlatform.Infrastructure.DomainServices;

public sealed class BranchDetector : IBranchDetector
{
    public HashSet<string> Detect(string situation, string question)
    {
        var t = (situation + " " + question).ToLower();
        var b = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Any(t,"impôt sur les sociétés"," is ","bénéfice","résultat fiscal","déductib","société","distribution","dividende")) b.Add("IS");
        if (Any(t,"irpp","revenu","personne physique","directeur","dirigeant","mandataire","salaire","rémunération","catégorie")) b.Add("IRPP");
        if (Any(t,"tva","taxe sur la valeur","prestation","assujetti","exonér","soumises","affaires","activités")) b.Add("TVA");
        if (Any(t,"retenue à la source","retenue source","non-résident","non résident","non établi")) b.Add("Retenue");
        if (Any(t,"prix de transfert","management fees","pleine concurrence","intragroupe","48 septies","frais de siège")) b.Add("PrixTransfert");
        return b;
    }
    private static bool Any(string t, params string[] terms) =>
        terms.Any(x => t.Contains(x, StringComparison.OrdinalIgnoreCase));
}

public sealed class CountryDetector : ICountryDetector
{
    private static readonly string[] Known =
    {"maroc","france","allemagne","italie","belgique","suisse","espagne","algerie",
     "libye","egypte","canada","turquie","senegal","mauritanie","jordanie","luxembourg",
     "pays-bas","royaume-uni","qatar","emirats","arabie"};
    public (List<string> Countries, bool IsInternational) Detect(string text)
    {
        var lower = text.ToLower();
        var found = Known.Where(c => lower.Contains(c)).Distinct().ToList();
        return (found, found.Any() || lower.Contains("non-résident") || lower.Contains("convention") || lower.Contains("étranger"));
    }
}

public sealed class KeywordExtractor : IKeywordExtractor
{
    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {"le","la","les","de","du","des","à","au","aux","un","une","que","qui","est","sont","en","par","pour",
     "avec","dans","sur","ce","ces","cet","cette","entre","même","aussi","selon","fiscal","impôt"};
    private static readonly char[] Sp = {' ','?','.', ',',';',':','!','"','\'','(',')','-','/','\\','\n','\r'};

    public (List<string> Keywords, List<string> Entities) Extract(string situation, string question)
    {
        IEnumerable<string> Tok(string s) =>
            s.Split(Sp, StringSplitOptions.RemoveEmptyEntries)
             .Where(w => w.Length >= 3 && !Stop.Contains(w.ToLower()))
             .Select(w => w.ToLower()).Distinct();
        var fromQ = Tok(question).Take(10).ToList();
        var fromS = Tok(situation).Except(fromQ).Take(8).ToList();
        var kws   = fromQ.Concat(fromS).Take(22).ToList();
        var lower = (question + " " + situation).ToLower();
        var ents  = new List<string>();
        foreach (var p in new[]{"retenue à la source","prix de transfert","double imposition","résidence fiscale"})
            if (lower.Contains(p)) ents.Add(p);
        ents.AddRange(fromQ.Where(k => k.Length >= 4).Take(4));
        return (kws, ents.Distinct().Take(8).ToList());
    }
}

public sealed class InMemorySessionStore : ISessionStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ConversationSession> _store = new();
    public void Set(string id, ConversationSession s) => _store[id] = s;
    public ConversationSession? Get(string id) => _store.TryGetValue(id, out var s) ? s : null;
    public void Remove(string id) => _store.TryRemove(id, out _);
    public bool Exists(string id) => _store.ContainsKey(id);
}
