using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Entity za tabelo FA_RACUN iz Firebird baze
/// </summary>
[Table("FA_RACUN")]
public class FaRacun
{
    [Column("STEVILKA"), PrimaryKey]
    public int Stevilka { get; set; }

    [Column("LETO"), PrimaryKey]
    public int Leto { get; set; }

    [Column("SIFRA_KUPCA")]
    public int SifraKupca { get; set; }

    [Column("DATUM")]
    public DateTime? Datum { get; set; }

    [Column("DATUM_STORITVE1")]
    public DateTime? DatumStoritve1 { get; set; }

    [Column("DATUM_STORITVE2")]
    public DateTime? DatumStoritve2 { get; set; }

    [Column("NAROCILO_STEVILKA")]
    public string? NarociloStevilka { get; set; }

    [Column("ZNESEK")]
    public decimal? Znesek { get; set; }

    [Column("ZNESEK_KONCNI")]
    public decimal? ZnesekKoncni { get; set; }

    // Predraèun 1
    [Column("PREDRAC1_STEVILKA")]
    public string? Predrac1Stevilka { get; set; }

    [Column("PREDRAC1_LETO")]
    public int? Predrac1Leto { get; set; }

    [Column("PREDRAC1_ZNESEK")]
    public decimal? Predrac1Znesek { get; set; }

    // Predraèun 2
    [Column("PREDRAC2_STEVILKA")]
    public string? Predrac2Stevilka { get; set; }

    [Column("PREDRAC2_LETO")]
    public int? Predrac2Leto { get; set; }

    [Column("PREDRAC2_ZNESEK")]
    public decimal? Predrac2Znesek { get; set; }

    // Povezava
    [Column("POVEZAVA_STEVILKA")]
    public string? PovezavaStevilka { get; set; }

    [Column("POVEZAVA_LETO")]
    public int? PovezavaLeto { get; set; }

    [Column("TIP_RACUNA")]
    public int? TipRacuna { get; set; }
}
