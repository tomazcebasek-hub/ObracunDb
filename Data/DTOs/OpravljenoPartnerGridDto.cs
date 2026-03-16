namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz seštevkov po partnerju v gridu (opravljeno delo)
/// </summary>
public class OpravljenoPartnerGridDto
{
    public int Partner { get; set; }
    public string? NazivPartnerja { get; set; }
    public int SteviloNalogov { get; set; }
    public int SteviloObracunanih { get; set; }
    public int SteviloNeobracunanih { get; set; }
    public decimal Kolicina { get; set; }
    public decimal Vrednost { get; set; }
    public decimal KolicinaPos { get; set; }
    public decimal VrednostPos { get; set; }
}
