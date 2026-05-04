using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Ročna korektura postavke v osnutku računa - sprememba količine za artikel.
/// </summary>
[Table("OBRACUN_OSNUTEK_SPREMEMBA")]
public class ObracunOsnutekSprememba
{
    [Column("ID"), PrimaryKey, Identity]
    public int Id { get; set; }

    [Column("MESEC")]
    public int Mesec { get; set; }

    [Column("LETO")]
    public int Leto { get; set; }

    [Column("PARTNER")]
    public int Partner { get; set; }

    [Column("ARTIKEL")]
    public string Artikel { get; set; } = string.Empty;

    [Column("KOLICINA")]
    public decimal Kolicina { get; set; }

    [Column("OPOMBA")]
    public string? Opomba { get; set; }

    [Column("UPORABNIK")]
    public string Uporabnik { get; set; } = string.Empty;

    [Column("DATUM_VNOSA")]
    public DateTime DatumVnosa { get; set; }
}
