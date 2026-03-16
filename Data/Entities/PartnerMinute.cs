using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("OBRACUN_MINUTE")]
public class PartnerMinute
{
    [Column("ID"), PrimaryKey, Identity]
    public int Id { get; set; }

    [Column("PARTNER")]
    public int Partner { get; set; }

    [Column("DATUM")]
    public DateTime Datum { get; set; }

    [Column("MINUT")]
    public decimal Minut { get; set; }

    [Column("VELJAVNOST_MESECIH")]
    public int VeljavnostMesecih { get; set; } = 1;

    [Column("OPOMBA")]
    public string? Opomba { get; set; }

    [Column("ZACETEK_MESEC")]
    public int? ZacetekMesec { get; set; }

    [Column("ZACETEK_LETO")]
    public int? ZacetekLeto { get; set; }

    [Column("UPORABNIK")]
    public string Uporabnik { get; set; } = "Neznan";
}
