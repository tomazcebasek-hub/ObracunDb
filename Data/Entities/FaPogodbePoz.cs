using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("FA_POGODBE_POS")]
public class FaPogodbePoz
{
    [Column("STEVILKA")]
    public int Stevilka { get; set; }

    [Column("LETO")]
    public int Leto { get; set; }

    [Column("ZS"), PrimaryKey]
    public int Pozicija { get; set; }

    [Column("SIFRA")]
    public string? Sifra { get; set; }

    [Column("NAZIV")]
    public string? Naziv { get; set; }

    [Column("KOLICINA")]
    public decimal? Kolicina { get; set; }

    [Column("PRODAJNA_CENA")]
    public decimal? ProdajnaCena { get; set; }

    [Column("MESECI")]
    public string? Meseci { get; set; }

    [Column("RABAT1")]
    public decimal? Rabat1 { get; set; }
}
