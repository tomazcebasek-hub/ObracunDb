using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Entity za tabelo OBRACUN_PAKET_MINUTE iz Firebird baze
/// </summary>
[Table("OBRACUN_PAKET_MINUTE")]
public class ObracunPaketMinute
{
    [Column("ID"), PrimaryKey, Identity]
    public int Id { get; set; }

    [Column("DATUM")]
    public DateTime Datum { get; set; }

    [Column("ARTIKEL")]
    public string Artikel { get; set; } = string.Empty;

    [Column("MINUT")]
    public int Minut { get; set; }
}
