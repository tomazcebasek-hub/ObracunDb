namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz naloga v popupu pregleda ur.
/// </summary>
public class PregledUrNalogDto
{
    public string Stevilka { get; set; } = "";
    public int LetoNaloga { get; set; }
    public DateTime? Datum { get; set; }
    public DateTime? ZacetekUra { get; set; }
    public DateTime? KonecUra { get; set; }
    public int Partner { get; set; }
    public string NazivPartnerja { get; set; } = "";
    public bool Nom { get; set; }

    /// <summary>Vrsta dneva: Delavnik / Vikend / Praznik.</summary>
    public string TipDneva { get; set; } = "";

    /// <summary>Trajanje naloga v minutah.</summary>
    public int TrajanjeMin { get; set; }

    /// <summary>Skupne ure naloga.</summary>
    public decimal SkupajUre => TrajanjeMin / 60m;

    /// <summary>Ure NOM (vse, če je nalog NOM, sicer 0).</summary>
    public decimal UreNom { get; set; }

    /// <summary>Ure za partner 23900 (Pos elektronček).</summary>
    public decimal UrePartner23900 { get; set; }

    /// <summary>Ure stranke razčlenjene po tarifah.</summary>
    public decimal UreStranke_7_16 { get; set; }
    public decimal UreStranke_16_22 { get; set; }
    public decimal UreStranke_22_7 { get; set; }

    public decimal UreStranke => UreStranke_7_16 + UreStranke_16_22 + UreStranke_22_7;

    /// <summary>Združen opis iz NAZIV1..NAZIV9.</summary>
    public string? Opis { get; set; }

    public string Key => $"{Stevilka}/{LetoNaloga}";
}
