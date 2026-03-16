using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Entity za tabelo PARTNER iz Firebird baze
/// </summary>
[Table("PARTNER")]
public class Partner
{
    [Column("SIFRA"), PrimaryKey]
    public int Sifra { get; set; }

    [Column("NAZIV")]
    public string? Naziv { get; set; }

    [Column("NASLOV")]
    public string? Naslov { get; set; }

    [Column("POSTA")]
    public string? Posta { get; set; }

    [Column("DAVCNA")]
    public string? Davcna { get; set; }

    /// <summary>
    /// Kilometri za partnerja (razdalja do stranke).
    /// Uporablja se za obračun kilometrine na delovnih nalogih.
    /// </summary>
    [Column("PROSTO_R1")]
    public double? ProstoR1 { get; set; }

    [Column("ROK_PLACILA")]
    public int? RokPlacila { get; set; }
}
