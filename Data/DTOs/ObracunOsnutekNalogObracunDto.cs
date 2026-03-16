namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za tabelo OBRACUN_OSNUTEK_NALOG_OBRACUN - uporablja se v UI
/// </summary>
public class ObracunOsnutekNalogObracunDto
{
    public int Mesec { get; set; }
    public int Leto { get; set; }
    public int Partner { get; set; }
    public string StevilkaNaloga { get; set; } = string.Empty;
    public int? LetoNaloga { get; set; }
    public int? Obracunam { get; set; }
    public string? SifraArtikla { get; set; }
    public string? SifraKomercialista { get; set; }
    public decimal? Kolicina { get; set; }
    public decimal? ProdajnaCena { get; set; }
    public int? MinuteOdstetePartnerMinute { get; set; }
    public int? MinuteOdstetePredracun { get; set; }
    public int? MinuteOdsteteRocno { get; set; }
    public int? MinuteOdstetePogodba { get; set; }
    public int? MinuteNalog { get; set; }
    public decimal? KolicinaFakturirana { get; set; }

    /// <summary>
    /// Skupne minute (dejanske minute naloga)
    /// </summary>
    public int SkupneMinute => MinuteNalog ?? 0;

    /// <summary>
    /// Skupne ure (minute pretvorjene v ure)
    /// </summary>
    public decimal SkupneUre => Math.Round(SkupneMinute / 60m, 2);

    /// <summary>
    /// Obdobje v formatu MM/LLLL
    /// </summary>
    public string Obdobje => $"{Mesec:D2}/{Leto}";

    /// <summary>
    /// Vrednost (Kolicina * ProdajnaCena)
    /// </summary>
    public decimal Vrednost => (Kolicina ?? 0) * (ProdajnaCena ?? 0);
}
