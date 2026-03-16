using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Nastavitve obračuna za delovni nalog.
/// </summary>
[Table("OBRACUN_DN")]
public class ObracunDn
{
    [Column("STEVILKA"), PrimaryKey(1)]
    public string Stevilka { get; set; } = "";

    [Column("LETO"), PrimaryKey(2)]
    public int Leto { get; set; }

    [Column("KAJ_OBRACUNAM")]
    public KajObracunam KajObracunam { get; set; }

    [Column("MINUTE_KI_SE_NE_OBRACUNAJO")]
    public int MinuteKiSeNeObracunajo { get; set; }

    [Column("MINUTE_NALOGA")]
    public int? MinuteNaloga { get; set; }
}
