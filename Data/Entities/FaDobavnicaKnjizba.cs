using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Entity za tabelo FA_DOBAVNICA_KNJIZBA iz Firebird baze
/// </summary>
[Table("FA_DOBAVNICA_KNJIZBA")]
public class FaDobavnicaKnjizba
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

    [Column("PRODAJNA_CENA")]
    public decimal? ProdajnaCena { get; set; }

    [Column("PRODAJNA_VREDNOST")]
    public decimal? ProdajnaVrednost { get; set; }

    [Column("RABAT1")]
    public decimal? Rabat1 { get; set; }

    [Column("CENA")]
    public decimal? Cena { get; set; }

    [Column("VREDNOST")]
    public decimal? Vrednost { get; set; }

    [Column("NABAVNA_CENA")]
    public decimal? NabavnaCena { get; set; }

    [Column("NABAVNA_VREDNOST")]
    public decimal? NabavnaVrednost { get; set; }
}
