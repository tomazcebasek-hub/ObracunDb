namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz paketov minut v gridu
/// </summary>
public class PaketMinuteGridDto
{
    public int Id { get; set; }
    public DateTime Datum { get; set; }
    public string Artikel { get; set; } = string.Empty;
    public string NazivArtikla { get; set; } = string.Empty;
    public string? Enota { get; set; }
    public int Minut { get; set; }
}
