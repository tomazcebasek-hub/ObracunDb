namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za pregled ur po serviserju.
/// </summary>
public class PregledUrGridDto
{
    public string Serviser { get; set; } = "";
    public string NazivServiserja { get; set; } = "";
    public int SteviloNalogov { get; set; }
    public decimal SkupajUre { get; set; }
    public decimal UreNom { get; set; }
    public decimal UrePartner23900 { get; set; }

    // Razčlenitev ur strank po tarifah (07-16 / 16-22 / 22-07)
    public decimal UreStranke_7_16 { get; set; }
    public decimal UreStranke_16_22 { get; set; }
    public decimal UreStranke_22_7 { get; set; }

    public decimal UreStranke => UreStranke_7_16 + UreStranke_16_22 + UreStranke_22_7;
}
