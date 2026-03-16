using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Entity za tabelo FA_RACUN_KNJIZBA iz Firebird baze
/// </summary>
[Table("FA_RACUN_KNJIZBA")]
public class FaRacunKnjizba
{
    [Column("STEVILKA"), PrimaryKey]
    public int Stevilka { get; set; }

    [Column("LETO"), PrimaryKey]
    public int Leto { get; set; }

    [Column("ZS"), PrimaryKey]
    public int Zs { get; set; }

    [Column("SIFRA")]
    public string? Sifra { get; set; }

    [Column("KOLICINA")]
    public decimal? Kolicina { get; set; }

    [Column("PRODAJNA_VREDNOST")]
    public decimal? ProdajnaVrednost { get; set; }

    [Column("VREDNOST")]
    public decimal? Vrednost { get; set; }
}
