using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Zapis o izvedeni migraciji baze.
/// Tabela OBRACUN_MIGRATION že obstaja v bazi (ustvarjena v starem projektu Obracun).
/// </summary>
[Table("OBRACUN_MIGRATION")]
public class ObracunMigration
{
    [Column("VERZIJA"), PrimaryKey]
    public int Verzija { get; set; }

    [Column("DATUM")]
    public DateTime Datum { get; set; }

    [Column("OPIS")]
    public string? Opis { get; set; }
}
