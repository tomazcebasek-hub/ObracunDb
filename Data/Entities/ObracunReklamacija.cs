using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

public enum TipReklamacije
{
    PrekinitevPogodbe = 1,
    Reklamacija = 2,
    NedokoncanProjekt = 3
}

[Table("OBRACUN_REKLAMACIJA")]
public class ObracunReklamacija
{
    [Column("ID"), PrimaryKey, Identity]
    public int Id { get; set; }

    [Column("TIP_REKLAMACIJE")]
    public TipReklamacije TipReklamacije { get; set; }

    [Column("PARTNER")]
    public int Partner { get; set; }

    [Column("DATUM_ZAHTEVE")]
    public DateTime DatumZahteve { get; set; }

    [Column("STEVILKE_POGODB")]
    public string? StevilkePogodb { get; set; }

    [Column("KONTAKT")]
    public string? Kontakt { get; set; }

    [Column("TIP_PREKINITVE")]
    public string? TipPrekinitve { get; set; }

    [Column("RACUNI_DO_DNE")]
    public DateTime? RacuniDoDne { get; set; }

    [Column("OPIS")]
    public string? Opis { get; set; }
}
