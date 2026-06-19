using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("OBRACUN_REKLAMACIJA_POS")]
public class ObracunReklamacijaPos
{
    [Column("ID"), PrimaryKey, Identity]
    public int Id { get; set; }

    [Column("ID_REKLAMACIJA")]
    public int IdReklamacija { get; set; }

    [Column("DATUM")]
    public DateTime Datum { get; set; }

    [Column("UPORABNIK")]
    public string Uporabnik { get; set; } = string.Empty;

    [Column("OPIS")]
    public string? Opis { get; set; }

    [Column("KDO_NAJ_OBDELA")]
    public string? KdoNajObdela { get; set; }
}
