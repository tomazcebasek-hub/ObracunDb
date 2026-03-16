using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Tip porabe minut
/// </summary>
public enum TipPorabeMinut
{
    Predracun = 1,
    PartnerMinute = 2
}

/// <summary>
/// Tabela za beleženje porabe minut iz predraèunov in partner_minute.
/// </summary>
[Table("OBRACUN_PORABA_MINUT")]
public class ObracunPorabaMinut
{
    [Column("ID"), PrimaryKey, Identity]
    public int Id { get; set; }

    [Column("MESEC")]
    public int Mesec { get; set; }

    [Column("LETO")]
    public int Leto { get; set; }

    [Column("PARTNER")]
    public int Partner { get; set; }

    [Column("TIP")]
    public TipPorabeMinut Tip { get; set; }

    [Column("PREDRACUN_STEVILKA")]
    public string? PredracunStevilka { get; set; }

    [Column("PREDRACUN_LETO")]
    public int? PredracunLeto { get; set; }

    [Column("ID_OBRACUN_MINUTE")]
    public int? IdObracunMinute { get; set; }

    [Column("KOLICINA")]
    public int Kolicina { get; set; }
}
