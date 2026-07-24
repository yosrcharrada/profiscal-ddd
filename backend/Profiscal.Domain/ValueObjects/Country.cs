namespace FiscalPlatform.Domain.ValueObjects;

public sealed record Country(string Name)
{
    public static readonly string[] KnownNames =
    {
        "maroc","france","allemagne","italie","belgique","suisse","espagne",
        "algerie","libye","egypte","canada","turquie","senegal","mauritanie",
        "jordanie","luxembourg","pays-bas","royaume-uni","qatar","emirats"
    };

    public override string ToString() => Name;
}
