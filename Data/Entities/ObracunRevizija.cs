using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Revizijska sled sprememb v obèutljivih tabelah.
/// </summary>
[Table("OBRACUN_REVIZIJA")]
public class ObracunRevizija
{
    [Column("ID"), PrimaryKey, Identity]
    public int Id { get; set; }

    [Column("DATUM"), NotNull]
    public DateTime Datum { get; set; }

    [Column("UPORABNIK"), NotNull]
    public string Uporabnik { get; set; } = "";

    [Column("TABELA"), NotNull]
    public string Tabela { get; set; } = "";

    [Column("POLJE"), NotNull]
    public string Polje { get; set; } = "";

    [Column("STARA_VREDNOST")]
    public string? StaraVrednost { get; set; }

    [Column("NOVA_VREDNOST")]
    public string? NovaVrednost { get; set; }

    /// <summary>
    /// Dodatni kontekst (npr. "Nalog 123/2026" ali "Partner 23900")
    /// </summary>
    [Column("KONTEKST")]
    public string? Kontekst { get; set; }

    /// <summary>
    /// Številka naloga (èe je tabela FA_DN_NALOG ali OBRACUN_DN)
    /// </summary>
    [Column("STEVILKA")]
    public string? Stevilka { get; set; }

    /// <summary>
    /// Leto naloga (èe je tabela FA_DN_NALOG ali OBRACUN_DN)
    /// </summary>
    [Column("LETO")]
    public int? Leto { get; set; }

    /// <summary>
    /// ID zapisa v tabeli (npr. ID iz OBRACUN_MINUTE)
    /// </summary>
    [Column("ID_V_TABELI")]
    public int? IdVTabeli { get; set; }
}
