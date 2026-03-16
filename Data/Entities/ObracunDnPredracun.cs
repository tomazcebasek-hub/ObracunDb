using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("OBRACUN_DN_PREDRACUN")]
public class ObracunDnPredracun
{
    [Column("STEVILKA")]
    public string Stevilka { get; set; } = string.Empty;

    [Column("LETO")]
    public int Leto { get; set; }

    [Column("PREDRACUN_STEVILKA")]
    public string PredracunStevilka { get; set; } = string.Empty;

    [Column("PREDRACUN_LETO")]
    public int PredracunLeto { get; set; }
}
