using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("FA_KOMERCIALIST")]
public class FaKomercialist
{
    [Column("SIFRA"), PrimaryKey]
    public string Sifra { get; set; } = string.Empty;

    [Column("PRIIMEK")]
    public string? Priimek { get; set; }

    [Column("IME")]
    public string? Ime { get; set; }

    [NotColumn]
    public string PolnoIme => string.IsNullOrEmpty(Ime)
        ? (Priimek ?? "")
        : $"{Priimek ?? ""} {Ime}".Trim();
}
