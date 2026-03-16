using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("OBRACUN_LOG")]
public class ObracunLog
{
    [Column("MESEC"), PrimaryKey]
    public int Mesec { get; set; }

    [Column("LETO"), PrimaryKey]
    public int Leto { get; set; }

    [Column("DATUM")]
    public DateTime Datum { get; set; }

    [Column("LOG_DATA")]
    public string? LogData { get; set; }
}
