using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("FA_PRODAJALNA")]
public class FaProdajalna
{
    [Column("SIFRA"), PrimaryKey]
    public int Sifra { get; set; }

    [Column("KUPEC")]
    public int Kupec { get; set; }

    [Column("NAZIV")]
    public string? Naziv { get; set; }
}
