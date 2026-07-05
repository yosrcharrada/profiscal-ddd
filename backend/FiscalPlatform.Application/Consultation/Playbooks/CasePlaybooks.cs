namespace FiscalPlatform.Application.Consultation.Playbooks;

/// <summary>
/// The income qualification of a consultation point. A real EY tax memo is driven, first and
/// foremost, by WHAT KIND of income is being paid — a service fee, a dividend, an interest, a
/// royalty. Each qualification has its own démarche, its own source map, and its own output shape.
/// The old pipeline baked in ONE démarche (foreign-service RS: ES → RS → TVA → assiette → transfert)
/// and applied it to everything, which mangled dividends. This enum is the routing key.
/// </summary>
public enum CaseType
{
    /// <summary>Fallback = the legacy, general foreign/int'l RS-service flow (unchanged behaviour).</summary>
    Generic,
    /// <summary>Services rendered by a NON-RESIDENT supplier (établissement stable → RS → TVA → …).</summary>
    RsServiceForeign,
    /// <summary>Services between two RESIDENT entities (droit commun: qualification honoraires via
    /// NC 3/2015 → the reduced Art.52 line for PM soumises à l'IS; no ES, no privileged regime,
    /// no foreign-transfer formalism).</summary>
    RsServiceLocal,
    /// <summary>Dividends distributed to a non-resident shareholder (no ES-of-service, no TVA).</summary>
    Dividende,
    /// <summary>Interest paid to a non-resident (convention + domestic rate; no ES-of-service, no TVA).</summary>
    Interet,
    /// <summary>Royalties (redevances) paid to a non-resident (treaty Art. royalties + Art. 52).</summary>
    Redevance,
}

/// <summary>
/// Universal EY house-style — the WAY a memo reads, independent of the case. This is how we fix
/// "reads like a draft / weak analysis" without teaching any answer: it contains zero tax content,
/// only writing rules. Applied to every case, service or dividend alike.
/// </summary>
public static class EyStyle
{
    public const string Card =
        "═══ STYLE RÉDACTIONNEL — CONSULTATION EY REMISE AU CLIENT (universel) ═══\n" +
        "Rédige une consultation ABOUTIE et PROFESSIONNELLE, en PROSE juridique continue — un mémo\n" +
        "effectivement remis au client, PAS un brouillon ni un exercice.\n" +
        "• Phrases FLUIDES et liées : « Conformément à l'article … [Sn], … », « Il en résulte que … »,\n" +
        "  « En conséquence, … », « Dès lors, … », « En application des dispositions combinées de … ».\n" +
        "• INTERDIT les étiquettes de raisonnement : « Principe applicable : », « Application au cas : »,\n" +
        "  « Détermination », « le scénario applicable », « sur la base du fait établi », et tout « Si X alors Y ».\n" +
        "• UN SEUL verdict par point, affirmé directement, sans hésitation ni condition non étayée par les faits.\n" +
        "• Chaque taux/chiffre est LU dans le texte de la source citée [Sn] et recopié EXACTEMENT — jamais de\n" +
        "  mémoire, jamais « à vérifier », jamais « il convient de consulter ».\n" +
        "• Citations : le NUMÉRO RÉEL de la source (p.ex. [S1], [S7]) — JAMAIS le littéral « [Sn] ».\n" +
        "• DROIT AU BUT — article multi-taux : NE CITE QUE l'alinéa qui S'APPLIQUE au cas ; ne reproduis pas,\n" +
        "  ne discute pas et n'écarte pas longuement les alinéas non applicables (aucun détour par les\n" +
        "  catégories écartées — la lex specialis se constate, elle ne se plaide pas).\n" +
        "• Ne re-cite pas le texte d'un article déjà exploité : UNE citation textuelle par règle suffit ;\n" +
        "  après le verdict, on n'y revient pas. Chaque sous-section : 2 paragraphes courts MAXIMUM.\n" +
        "• Ne commente JAMAIS ce que les sources ne contiennent pas (« non reproduit dans les sources »,\n" +
        "  « les sources ne comportent pas… ») — soit tu cites la source, soit le point est NON DOCUMENTÉ,\n" +
        "  sans méta-commentaire sur le corpus.\n" +
        "• ÉDITION UNIQUE : cite un article de code dans sa SEULE édition la plus récente — jamais deux\n" +
        "  éditions du même article côte à côte.\n";
}
