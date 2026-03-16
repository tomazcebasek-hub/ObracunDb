using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("FA_DN_NALOG_KNJ")]
public class FaDnNalogPoz
{
    [Column("STEVILKA")]
    public string Stevilka { get; set; } = "";

    [Column("LETO")]
    public int Leto { get; set; }

    [Column("ZS")]
    public int Zs { get; set; }

    [Column("SIFRA")]
    public string? Sifra { get; set; }

    [Column("KOLICINA")]
    public decimal Kolicina { get; set; }

    [Column("CENA")]
    public decimal Cena { get; set; }

    [Column("RABAT1")]
    public decimal Rabat1 { get; set; }
}
