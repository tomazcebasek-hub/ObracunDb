using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("OBRACUN_LOCENI_RACUNI")]
public class ObracunLoceniRacun
{
    [Column("ID"), PrimaryKey, Identity]
    public int Id { get; set; }

    [Column("PARTNER")]
    public int Partner { get; set; }

    [Column("PRODAJALNA")]
    public int Prodajalna { get; set; }

    [Column("POGODBA_STEVILKA")]
    public int PogodbaStevilka { get; set; }

    [Column("POGODBA_LETO")]
    public int PogodbaLeto { get; set; }

    [Column("DATUM_VNOSA")]
    public DateTime DatumVnosa { get; set; }

    [Column("UPORABNIK")]
    public string Uporabnik { get; set; } = string.Empty;
}
