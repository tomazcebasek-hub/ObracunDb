using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("OBRACUN_UPORABNIK")]
public class ObracunUporabnik
{
    [Column("ID"), PrimaryKey, Identity]
    public int Id { get; set; }

    [Column("UPORABNISKO_IME")]
    public string UporabniskoIme { get; set; } = string.Empty;

    [Column("GESLO_HASH")]
    public string GesloHash { get; set; } = string.Empty;

    [Column("VLOGA")]
    public int Vloga { get; set; }

    [Column("AKTIVEN")]
    public int Aktiven { get; set; } = 1;

    [Column("PRVA_PRIJAVA")]
    public int PrvaPrijava { get; set; } = 1;

    [Column("DATUM_USTVARJEN")]
    public DateTime DatumUstvarjen { get; set; } = DateTime.Now;

    [Column("DATUM_ZADNJA_PRIJAVA")]
    public DateTime? DatumZadnjaPrijava { get; set; }

    /// <summary>
    /// Vrne vlogo kot enum.
    /// </summary>
    public UporabnikVloga VlogaEnum => (UporabnikVloga)Vloga;
}

public enum UporabnikVloga
{
    /// <summary>
    /// Administrator - polni dostop.
    /// </summary>
    Admin = 0,

    /// <summary>
    /// Vodja - lahko upravlja uporabnike.
    /// </summary>
    Vodja = 1,

    /// <summary>
    /// Potrjevalec - osnovni dostop.
    /// </summary>
    Potrjevalec = 2
}
