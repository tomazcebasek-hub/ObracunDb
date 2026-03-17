using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Tip postavke v osnutku.
/// </summary>
public enum TipPostavke
{
    /// <summary>
    /// Napaka.
    /// </summary>
    NAPAKA = 0,

    /// <summary>
    /// Rocno vnesena postavka.
    /// </summary>
    ROCNI = 1,

    /// <summary>
    /// Postavka iz pogodbe.
    /// </summary>
    POGODBA = 2,

    /// <summary>
    /// Postavka iz delovnega naloga.
    /// </summary>
    NALOG = 3
}

[Table("OBRACUN_OSNUTEK_POS")]
public class ObracunOsnutekPos
{
    [Column("MESEC"), PrimaryKey(1)]
    public int Mesec { get; set; }

    [Column("LETO"), PrimaryKey(2)]
    public int Leto { get; set; }

    [Column("PARTNER"), PrimaryKey(3)]
    public int Partner { get; set; }

    [Column("ZS"), PrimaryKey(4)]
    public int Zs { get; set; }

    [Column("ARTIKEL")]
    public string? Artikel { get; set; }

    [Column("NAZIV")]
    public string? Naziv { get; set; }

    [Column("KOLICINA")]
    public decimal? Kolicina { get; set; }

    [Column("CENA")]
    public decimal? Cena { get; set; }

    [Column("RABAT")]
    public decimal? Rabat { get; set; }

    [Column("NALOG_STEVILKA")]
    public string? NalogStevilka { get; set; }

    [Column("NALOG_LETO")]
    public int? NalogLeto { get; set; }

    [Column("TIP_POSTAVKE")]
    public TipPostavke TipPostavke { get; set; }

    [Column("KDO")]
    public string? Kdo { get; set; }

    [Column("KDAJ")]
    public DateTime? Kdaj { get; set; }
}
