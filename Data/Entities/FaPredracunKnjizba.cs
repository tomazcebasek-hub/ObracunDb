using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Entity za tabelo FA_PREDRACUN_KNJIZBA iz Firebird baze
/// </summary>
[Table("FA_PREDRACUN_KNJIZBA")]
public class FaPredracunKnjizba
{
    [Column("STEVILKA"), PrimaryKey]
    public string Stevilka { get; set; } = string.Empty;

    [Column("LETO"), PrimaryKey]
    public int Leto { get; set; }

    [Column("ZS"), PrimaryKey]
    public int Zs { get; set; }

    [Column("SIFRA")]
    public string? Sifra { get; set; }

    [Column("KOLICINA")]
    public decimal? Kolicina { get; set; }

    [Column("PRODAJNA_CENA")]
    public decimal? ProdajnaCena { get; set; }

    [Column("PRODAJNA_VREDNOST")]
    public decimal? ProdajnaVrednost { get; set; }

    [Column("RABAT1")]
    public decimal? Rabat1 { get; set; }
}
