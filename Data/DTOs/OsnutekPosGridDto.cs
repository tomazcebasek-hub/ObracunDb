namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz postavk osnutka v gridu
/// </summary>
public class OsnutekPosGridDto
{
    public int Zs { get; set; }
    public string? Artikel { get; set; }
    public string? NazivArtikla { get; set; }
    public string? EnotaArtikla { get; set; }
    public decimal Kolicina { get; set; }
    public decimal Cena { get; set; }
    public decimal Rabat { get; set; }
    public decimal Vrednost { get; set; }
}
