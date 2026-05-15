using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Entity za tabelo FA_PREDRACUN iz Firebird baze
/// </summary>
[Table("FA_PREDRACUN")]
public class FaPredracun
{
    [Column("STEVILKA"), PrimaryKey]
    public string Stevilka { get; set; } = string.Empty;

    [Column("LETO"), PrimaryKey]
    public int Leto { get; set; }

    [Column("DATUM")]
    public DateTime? Datum { get; set; }

    [Column("SIFRA_KUPCA")]
    public int SifraKupca { get; set; }

    [Column("STANJE")]
    public int? Stanje { get; set; }

    [Column("ZNESEK_KONCNI")]
    public decimal? ZnesekKoncni { get; set; }

    [Column("KOMERCIALIST")]
    public string? Komercialist { get; set; }
}
