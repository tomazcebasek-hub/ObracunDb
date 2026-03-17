using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Evidenca ločenih računov po pogodbah za partnerje z več pogodbami.
/// </summary>
[Table("OBRACUN_OSNUTEK_RACUN")]
public class ObracunOsnutekRacun
{
    [Column("MESEC"), PrimaryKey(1)]
    public int Mesec { get; set; }

    [Column("LETO"), PrimaryKey(2)]
    public int Leto { get; set; }

    [Column("PARTNER"), PrimaryKey(3)]
    public int Partner { get; set; }

    [Column("POGODBA_STEVILKA"), PrimaryKey(4)]
    public int PogodbaStevilka { get; set; }

    [Column("POGODBA_LETO"), PrimaryKey(5)]
    public int PogodbaLeto { get; set; }

    [Column("PRODAJALNA")]
    public int Prodajalna { get; set; }

    [Column("RACUN_STEVILKA")]
    public int? RacunStevilka { get; set; }

    [Column("RACUN_LETO")]
    public int? RacunLeto { get; set; }
}
