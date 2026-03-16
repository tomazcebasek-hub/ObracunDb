using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Entity za tabelo FA_ARTIKEL iz Firebird baze
/// </summary>
[Table("FA_ARTIKEL")]
public class FaArtikel
{
    [Column("SIFRA"), PrimaryKey]
    public string Sifra { get; set; } = string.Empty;

    [Column("NAZIV")]
    public string Naziv { get; set; } = string.Empty;

    [Column("NAZIV2")]
    public string? Naziv2 { get; set; }

    [Column("KARTICA_ARTIKLA")]
    public int KarticaArtikla { get; set; }

    [Column("PRODAJNA_CENA")]
    public decimal ProdajnaCena { get; set; }

    [Column("ENOTA")]
    public string? Enota { get; set; }

    [NotColumn]
    public string PolniNaziv => string.IsNullOrEmpty(Naziv2) ? Naziv : $"{Naziv} {Naziv2}";
}
