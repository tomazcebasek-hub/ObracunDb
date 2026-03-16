namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz naloga v gridu (obraèunani / neobraèunani)
/// </summary>
public class NalogGridDto
{
    public string Stevilka { get; set; } = string.Empty;
    public string? Artikel { get; set; }
    public string? NazivArtikla { get; set; }
    public string? EnotaArtikla { get; set; }
    public string? NazivPotnika { get; set; }
    public DateTime? Datum { get; set; }
    public DateTime? ZacetekUra { get; set; }
    public DateTime? KonecUra { get; set; }
    public int? Minute { get; set; }
    public string? Opis { get; set; }
    public string? Naziv1 { get; set; }

    // Pomožna polja za združevanje (se ne prikažejo v gridu)
    internal int? _LetoNaloga { get; set; }
    internal string? _SifraPotnika { get; set; }

    public string Key => $"{Stevilka}";
}
