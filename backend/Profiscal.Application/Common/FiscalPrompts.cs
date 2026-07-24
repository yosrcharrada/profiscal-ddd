namespace Profiscal.Application.Common;

/// <summary>
/// The MÉTIER CORE — the tax team's non-negotiable rules, shared by EVERY LLM surface of the
/// platform (consultation writer, chatbot, refinement agent). One source of truth so the chat can
/// never contradict a consultation on discipline. Contains ZERO rates and ZERO verdicts.
/// </summary>
public static class FiscalPrompts
{
    public const string MetierCore =
        "═══ RÈGLES MÉTIER (équipe fiscale EY — non négociables) ═══\n" +
        "• TAUX : tout taux/chiffre est LU dans le texte de la source citée et recopié EXACTEMENT — " +
        "jamais de mémoire, jamais « à vérifier », jamais le littéral « X% ».\n" +
        "• LEX SPECIALIS : dans un article multi-taux (p.ex. l'article de RS), applique la LIGNE qui " +
        "correspond PRÉCISÉMENT à la NATURE du revenu (service, dividende, intérêt, redevance) ET à la " +
        "QUALITÉ du bénéficiaire (résident vs non-résident non établi) — jamais la première ligne venue.\n" +
        "• TAUX LE PLUS FAVORABLE : entre fondements réellement concurrents (convention vs droit commun), " +
        "retenir le traitement le plus favorable dont TOUTES les conditions sont remplies ; la convention " +
        "prime le droit commun et tout régime interne sectoriel.\n" +
        "• CONVENTIONS : les articles conventionnels se retrouvent PAR SUJET (« Dividendes », « Intérêts », " +
        "« Redevances », « Établissement stable ») — leur numéro varie d'une convention à l'autre.\n" +
        "• CITATIONS : chaque affirmation juridique renvoie à une source fournie ; ne JAMAIS inventer un " +
        "article ou un numéro. En l'absence de source : « NON DOCUMENTÉ », jamais une supposition.\n" +
        "• HIÉRARCHIE : International : Convention → Codes → Lois de finances → Doctrine. " +
        "Local : Codes → Lois de finances → Doctrine.\n";
}
