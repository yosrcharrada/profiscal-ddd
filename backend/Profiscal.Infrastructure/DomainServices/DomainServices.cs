using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Profiscal.Domain.Abstractions.Services;

namespace Profiscal.Infrastructure.DomainServices;

public sealed class BranchDetector : IBranchDetector
{
    public HashSet<string> Detect(string situation, string question)
    {
        var t = (situation + " " + question).ToLower();
        var b = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Any(t, "impôt sur les sociétés", " is ", "bénéfice", "résultat fiscal",
                   "déductib", "société", "distribution", "dividende", "capital social",
                   "holding", "filiale", "participation"))
            b.Add("IS");

        if (Any(t, "irpp", "revenu", "personne physique", "directeur", "dirigeant",
                   "mandataire", "salaire", "rémunération", "catégorie", "gérant",
                   "associé", "traitement"))
            b.Add("IRPP");

        if (Any(t, "tva", "taxe sur la valeur", "prestation", "assujetti",
                   "exonér", "soumises", "affaires", "activités", "facturation"))
            b.Add("TVA");

        if (Any(t, "retenue à la source", "retenue source", "non-résident",
                   "non résident", "non établi", "prestataire étranger",
                   "fournisseur étranger", "management fee", "assistance technique",
                   "redevance", "rémunération versée"))
            b.Add("Retenue");

        if (Any(t, "prix de transfert", "management fees", "pleine concurrence",
                   "intragroupe", "48 septies", "frais de siège", "parties liées",
                   "dépendance", "contrôle", "entreprises associées"))
            b.Add("PrixTransfert");

        return b;
    }

    private static bool Any(string t, params string[] terms) =>
        terms.Any(x => t.Contains(x, StringComparison.OrdinalIgnoreCase));
}

public sealed class CountryDetector : ICountryDetector
{
    private static readonly string[] Known =
    {
        "maroc", "algerie", "libye", "egypte", "jordanie", "emirats",
        "arabie saoudite", "arabie", "qatar", "koweit", "bahrain", "oman",
        "mauritanie", "senegal", "mali", "niger", "cameroun", "gabon",
        "france", "allemagne", "italie", "belgique", "suisse", "espagne",
        "pays-bas", "pays bas", "luxembourg", "portugal", "autriche",
        "royaume-uni", "royaume uni", "angleterre", "pologne", "roumanie",
        "bulgarie", "danemark", "suede", "norvege", "finlande", "grece",
        "canada", "etats-unis", "etats unis", "usa", "chine", "japon",
        "coree", "inde", "iran", "pakistan", "turquie", "vietnam",
        "syrie", "liban", "irak", "indonesie",
        // Common no-convention / privileged-regime jurisdictions (drive the droit-commun + 25%
        // majoration branch). Distinct strings — no clash with "hongrie".
        "hong kong", "hong-kong", "iles caimans", "îles caïmans", "panama", "seychelles",
        "iles vierges", "bermudes", "jersey", "guernesey", "bahamas",
    };

    // ONLY unambiguously-international phrases. Deliberately excluded because they occur verbatim
    // in purely DOMESTIC files and used to flip the case onto the treaty / foreign-service flow:
    //   • bare "convention"   → matches "convention de prestation de services" (an ordinary contract)
    //   • "redevance"         → a domestic licence/royalty fee (e.g. ANF frequency fee)
    //   • "devises"           → a domestic company can hold foreign-currency accounts
    //   • "associé unique" / "société mère" / "holding" → intra-group links exist domestically too
    // The tax-treaty sense is captured by the precise phrases below instead.
    private static readonly string[] InternationalSignals =
    {
        "non-résident", "non résident", "non établi", "non-établi",
        "convention fiscale", "convention de non double", "non double imposition",
        "double imposition", "cndi",
        "étranger", "étrangère",
        "management fee", "frais de siège",
        "prestataire étranger", "fournisseur étranger",
        "filiale tunisienne", "résidence fiscale",
    };

    // "non-résident DE CHANGE" / "résident de change" is a Tunisian FOREIGN-EXCHANGE (BCT) status,
    // NOT a tax residency — a company can be resident for tax yet « non-résidente de change ». The
    // signal scan must not read it as an international (tax) marker.
    private static readonly Regex ResidentDeChange =
        new(@"non[\s-]?résidente?\s+(?:de\s+)?change", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public (List<string> Countries, bool IsInternational) Detect(string text)
    {
        var lower = text.ToLower();
        var found = Known.Where(c => ContainsWholeWord(lower, c))
            .Distinct().ToList();

        var scan = ResidentDeChange.Replace(lower, " ");
        bool intlSignals = found.Any() ||
            InternationalSignals.Any(sig =>
                scan.Contains(sig, StringComparison.OrdinalIgnoreCase));

        return (found, intlSignals);
    }

    private static bool ContainsWholeWord(string text, string word)
    {
        int idx = 0;
        while ((idx = text.IndexOf(word, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            bool leftOk  = idx == 0 || !char.IsLetter(text[idx - 1]);
            int  end     = idx + word.Length;
            bool rightOk = end >= text.Length || !char.IsLetter(text[end]);
            if (leftOk && rightOk) return true;
            idx += 1;
        }
        return false;
    }
}

public sealed class KeywordExtractor : IKeywordExtractor
{
    private static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "le","la","les","de","du","des","à","au","aux","un","une","que","qui",
        "est","sont","en","par","pour","avec","dans","sur","ce","ces","cet",
        "cette","entre","même","aussi","selon","fiscal","impôt","taxe","tunisie",
        "tunisien","tunisienne","société","client","question","analyse","cas"
    };

    private static readonly char[] Sp =
        {' ','?','.',',',';',':','!','"','\'','(',')','[',']','-','/','\\','\n','\r','\t'};

    public (List<string> Keywords, List<string> Entities) Extract(
        string situation, string question)
    {
        IEnumerable<string> Tokenize(string src) =>
            src.Split(Sp, StringSplitOptions.RemoveEmptyEntries)
               .Where(w => w.Length >= 3 && !Stop.Contains(w.ToLower()))
               .Select(w => w.ToLower()).Distinct();

        var fromQ = Tokenize(question).Take(12).ToList();
        var fromS = Tokenize(situation).Except(fromQ).Take(10).ToList();
        var kws   = fromQ.Concat(fromS).Take(22).ToList();

        var combined = (question + " " + situation).ToLower();
        var ents     = new List<string>();

        foreach (var p in new[]
        {
            "retenue à la source", "prix de transfert", "pleine concurrence",
            "double imposition", "résidence fiscale", "établissement stable",
            "management fee", "frais de siège", "note commune",
            "prestation de services", "assistance technique"
        })
            if (combined.Contains(p)) ents.Add(p);

        ents.AddRange(fromQ.Where(k => k.Length >= 4).Take(4));
        return (kws, ents.Distinct().Take(8).ToList());
    }
}

// InMemorySessionStore must stay in DomainServices.cs — it's registered in DependencyInjection.cs
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, ConversationSession> _store = new();
    public void Set(string id, ConversationSession s) => _store[id] = s;
    public ConversationSession? Get(string id) => _store.TryGetValue(id, out var s) ? s : null;
    public void Remove(string id) => _store.TryRemove(id, out _);
    public bool Exists(string id) => _store.ContainsKey(id);
}
