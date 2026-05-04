namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za seštevek dela po šifri artikla.
/// </summary>
public class SestevekDelaGridDto
{
    public string SifraArtikla { get; set; } = "";
    public string NazivArtikla { get; set; } = "";
    public string Enota { get; set; } = "";

    /// <summary>Skupaj količina (ure) vseh nalogov.</summary>
    public decimal KolicinaSkupaj { get; set; }

    /// <summary>Količina (ure) nalogov ki se NE obračunajo.</summary>
    public decimal KolicinaNeobracunana { get; set; }

    /// <summary>Količina (ure) nalogov ki se obračunajo.</summary>
    public decimal KolicinaObracunana { get; set; }

    /// <summary>Koriščene minute iz ročno vnešenih postavk.</summary>
    public int OdsteteRocno { get; set; }

    /// <summary>Koriščene minute iz partner minut.</summary>
    public int OdstetePartnerMinute { get; set; }

    /// <summary>Koriščene minute iz predračunov.</summary>
    public int OdstetePredracun { get; set; }

    /// <summary>Koriščene minute iz pogodb.</summary>
    public int OdstetePogodba { get; set; }

    /// <summary>Skupaj koriščene minute (vsota vseh virov).</summary>
    public int OdsteteSkupaj { get; set; }

    /// <summary>Zaračunana količina (minute).</summary>
    public decimal KolicinaFakturirana { get; set; }
}
