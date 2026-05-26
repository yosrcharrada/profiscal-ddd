namespace FiscalPlatform.Domain.ValueObjects;

public sealed record LegalBranch
{
    public static readonly LegalBranch IS            = new("IS");
    public static readonly LegalBranch IRPP          = new("IRPP");
    public static readonly LegalBranch TVA           = new("TVA");
    public static readonly LegalBranch Retenue       = new("Retenue");
    public static readonly LegalBranch PrixTransfert = new("PrixTransfert");

    public string Code { get; }
    private LegalBranch(string code) => Code = code;

    public static LegalBranch? TryFrom(string code) => code.ToUpper() switch
    {
        "IS"            => IS,
        "IRPP"          => IRPP,
        "TVA"           => TVA,
        "RETENUE"       => Retenue,
        "PRIXTRANSFERT" => PrixTransfert,
        _               => null
    };

    public override string ToString() => Code;
}
