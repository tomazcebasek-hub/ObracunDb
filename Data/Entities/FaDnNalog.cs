using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("FA_DN_NALOG")]
public class FaDnNalog
{
    [Column("STEVILKA"), PrimaryKey]
    public string Stevilka { get; set; } = string.Empty;

    [Column("LETO"), PrimaryKey]
    public int Leto { get; set; }

    [Column("PARTNER")]
    public int Partner { get; set; }

    [Column("PRODAJALNA")]
    public int Prodajalna { get; set; }

    [Column("ZACETEK_DATUM")]
    public DateTime Datum { get; set; }

    [Column("ZACETEK_URA")]
    public DateTime ZacetekUra { get; set; }

    [Column("KONEC_URA")]
    public DateTime KonecUra { get; set; }

    [Column("FAKTURIRANA")]
    public int Fakturirana { get; set; }

    [Column("OPIS")]
    public string? Opis { get; set; }

    [Column("POTNIK")]
    public string? Potnik { get; set; }

    [Column("KOMERCIALIST")]
    public string? Komercialist { get; set; }

    /// <summary>
    /// Tip storitve (1 = servisna, 2 = strokovna, 3 = programerska).
    /// </summary>
    [Column("SIF26")]
    public int Sif26 { get; set; }

    /// <summary>
    /// Pregledan (0 = ne, 1 = da).
    /// </summary>
    [Column("SIF27")]
    public int Sif27 { get; set; }

    /// <summary>
    /// NOM.
    /// </summary>
    [Column("SIF28")]
    public int Sif28 { get; set; }

    /// <summary>
    /// Polovicna kilometrina (0 = polna, 1 = polovicna).
    /// </summary>
    [Column("SIF29")]
    public int Sif29 { get; set; }

    /// <summary>
    /// Oddaljenost.
    /// </summary>
    [Column("SIF30")]
    public int Sif30 { get; set; }

    // Opis naloga - NAZIV1..NAZIV20
    [Column("NAZIV1")] public string? Naziv1 { get; set; }
    [Column("NAZIV2")] public string? Naziv2 { get; set; }
    [Column("NAZIV3")] public string? Naziv3 { get; set; }
    [Column("NAZIV4")] public string? Naziv4 { get; set; }
    [Column("NAZIV5")] public string? Naziv5 { get; set; }
    [Column("NAZIV6")] public string? Naziv6 { get; set; }
    [Column("NAZIV7")] public string? Naziv7 { get; set; }
    [Column("NAZIV8")] public string? Naziv8 { get; set; }
    [Column("NAZIV9")] public string? Naziv9 { get; set; }

    /// <summary>
    /// Sifre artiklov, ki morajo na racun (locene z vejico).
    /// </summary>
    [Column("NAZIV10")] public string? Naziv10 { get; set; }

    [Column("NAZIV11")] public string? Naziv11 { get; set; }
    [Column("NAZIV12")] public string? Naziv12 { get; set; }
    [Column("NAZIV13")] public string? Naziv13 { get; set; }
    [Column("NAZIV14")] public string? Naziv14 { get; set; }
    [Column("NAZIV15")] public string? Naziv15 { get; set; }
    [Column("NAZIV16")] public string? Naziv16 { get; set; }
    [Column("NAZIV17")] public string? Naziv17 { get; set; }
    [Column("NAZIV18")] public string? Naziv18 { get; set; }
    [Column("NAZIV19")] public string? Naziv19 { get; set; }
    [Column("NAZIV20")] public string? Naziv20 { get; set; }

    /// <summary>
    /// Vrne zdruzen opis iz vseh NAZIV polj.
    /// </summary>
    public string GetOpis()
    {
        var nazivi = new[] { Naziv1, Naziv2, Naziv3, Naziv4, Naziv5,
                             Naziv6, Naziv7, Naziv8, Naziv9, Naziv10,
                             Naziv11, Naziv12, Naziv13, Naziv14, Naziv15,
                             Naziv16, Naziv17, Naziv18, Naziv19, Naziv20 };

        var neprazni = nazivi
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim());

        return string.Join(Environment.NewLine, neprazni);
    }
}
