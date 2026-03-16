using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Entity za tabelo FA_RACUN_PLACILO iz Firebird baze
/// </summary>
[Table("FA_RACUN_PLACILO")]
public class FaRacunPlacilo
{
    [Column("PREDRACUN_STEVILKA"), PrimaryKey]
    public int PredracunStevilka { get; set; }

    [Column("PREDRACUN_LETO"), PrimaryKey]
    public int PredracunLeto { get; set; }

    [Column("ZNESEK")]
    public decimal Znesek { get; set; }

    [Column("SCONTO")]
    public decimal? Sconto { get; set; }
}
