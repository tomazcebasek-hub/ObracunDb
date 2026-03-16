using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("OBRACUN_OSNUTEK")]
public class ObracunOsnutek
{
    [Column("MESEC"), PrimaryKey]
    public int Mesec { get; set; }

    [Column("LETO"), PrimaryKey]
    public int Leto { get; set; }

    [Column("PARTNER"), PrimaryKey]
    public int Partner { get; set; }

    [Column("IMA_POGODBO")]
    public int ImaPogodbo { get; set; }

    [Column("IMA_PREDRACUN")]
    public int ImaPredracun { get; set; }

    [Column("IMA_NALOGE")]
    public int ImaNaloge { get; set; }

    [Column("OPIS")]
    public string? Opis { get; set; }

    /// <summary>
    /// Minute nalogov ki se obracunajo (KajObracunam = KmMin ali Min).
    /// </summary>
    [Column("MINUTE_OBRACUNANE")]
    public int MinuteObracunane { get; set; }

    /// <summary>
    /// Minute nalogov ki se NE obracunajo.
    /// </summary>
    [Column("MINUTE_NEOBRACUNANE")]
    public int MinuteNeobracunane { get; set; }

    /// <summary>
    /// Minute, ki so bile koriscene (zaradi pogodb, rocno, paketov) in ne bodo zaracunane.
    /// </summary>
    [Column("MINUTE_KORISCENE")]
    public int MinuteKoriscene { get; set; }

    [Column("PLUS_MINUTE_PARTNER_MINUTE")]
    public int PlusMinutePartnerMinute { get; set; }

    [Column("PLUS_MINUTE_PREDRACUN")]
    public int PlusMinutePredracun { get; set; }

    [Column("PLUS_MINUTE_ROCNO")]
    public int PlusMinuteRocno { get; set; }

    [Column("PLUS_MINUTE_POGODBA")]
    public int PlusMinutePogodba { get; set; }

    [Column("VSE_MINUTE_PREDRACUN")]
    public int VseMinutePredracun { get; set; }

    [Column("ZE_PORABLJENE_PREDRACUN")]
    public int ZePorabljenePredracun { get; set; }

    [Column("VSE_MINUTE_PARTNER_MINUTE")]
    public int VseMinutePartnerMinute { get; set; }

    [Column("ZE_PORABLJENE_PARTNER_MINUTE")]
    public int ZePorabljenePartnerMinute { get; set; }

    [Column("LETNA_POGODBA")]
    public int LetnaPogodba { get; set; }

    [Column("RACUN_STEVILKA")]
    public int? RacunStevilka { get; set; }

    [Column("RACUN_LETO")]
    public int? RacunLeto { get; set; }
}
