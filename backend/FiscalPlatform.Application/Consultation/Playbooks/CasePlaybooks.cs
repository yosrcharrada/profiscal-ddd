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
        "• Citations : le NUMÉRO RÉEL de la source (p.ex. [S1], [S7]) — JAMAIS le littéral « [Sn] ».\n";
}
