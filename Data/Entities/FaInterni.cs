using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Entity za tabelo FA_INTERNI iz Firebird baze
/// </summary>
[Table("FA_INTERNI")]
public class FaInterni
{
    [Column("STEVILKA"), PrimaryKey]
    public int Stevilka { get; set; }

    [Column("LETO"), PrimaryKey]
    public int Leto { get; set; }

    [Column("POVEZAVA_STEVILKA")]
    public int? PovezavaStevilka { get; set; }

    [Column("POVEZAVA_LETO")]
    public int? PovezavaLeto { get; set; }
}
