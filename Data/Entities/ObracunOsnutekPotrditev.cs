using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("OBRACUN_OSNUTEK_POTRDITEV")]
public class ObracunOsnutekPotrditev
{
    [Column("ID"), PrimaryKey, Identity]
    public int Id { get; set; }

    [Column("PARTNER")]
    public int Partner { get; set; }

    [Column("MESEC")]
    public int Mesec { get; set; }

    [Column("LETO")]
    public int Leto { get; set; }

    [Column("KDO")]
    public string Kdo { get; set; } = "";

    [Column("KDAJ")]
    public DateTime Kdaj { get; set; }
}
