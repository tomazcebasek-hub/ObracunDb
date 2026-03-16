using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Entity za tabelo FA_INTERNI_KNJIZBA iz Firebird baze
/// </summary>
[Table("FA_INTERNI_KNJIZBA")]
public class FaInterniKnjizba
{
    [Column("STEVILKA"), PrimaryKey]
    public int Stevilka { get; set; }

    [Column("LETO"), PrimaryKey]
    public int Leto { get; set; }

    [Column("ZS_SESTAVA")]
    public int ZsSestava { get; set; }

    [Column("NABAVNA_VREDNOST")]
    public decimal? NabavnaVrednost { get; set; }
}
