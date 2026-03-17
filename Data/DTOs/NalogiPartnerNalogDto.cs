namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz naloga v pregledu nalogov za partnerja (master grid).
/// </summary>
public class NalogiPartnerNalogDto
{
    public string Stevilka { get; set; } = string.Empty;
    public int Leto { get; set; }
    public string? Serviser { get; set; }
    public DateTime? Datum { get; set; }
    public DateTime? ZacetekUra { get; set; }
    public DateTime? KonecUra { get; set; }
    public int Trajanje { get; set; }

    /// <summary>Združen opis iz NAZIV1..NAZIV20.</summary>
    public string? Opis { get; set; }

    /// <summary>Podrobnosti obračuna iz OBRACUN_OSNUTEK_NALOG_OBRACUN.</summary>
    public List<NalogiPartnerObracunDto> Obracuni { get; set; } = new();

    public string Key => $"{Stevilka}/{Leto}";
}

/// <summary>
/// DTO za podrobnost obračuna naloga (detail grid).
/// </summary>
public class NalogiPartnerObracunDto
{
    public string StevilkaNaloga { get; set; } = string.Empty;
    public int LetoNaloga { get; set; }
    public bool Obracunam { get; set; }
    public string? SifraArtikla { get; set; }
    public string? NazivArtikla { get; set; }
    public string? EnotaArtikla { get; set; }
    public decimal Kolicina { get; set; }
    public decimal ProdajnaCena { get; set; }
    public int PartnerMinute { get; set; }
    public int PredracunMinute { get; set; }
    public int RocnoMinute { get; set; }
    public int PogodbaMinute { get; set; }
    public int SkupajMinute { get; set; }

    public string Key => $"{StevilkaNaloga}/{LetoNaloga}/{SifraArtikla}";
}
