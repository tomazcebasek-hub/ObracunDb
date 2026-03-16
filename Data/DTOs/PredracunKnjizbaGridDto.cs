namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz knjižbe predraèuna (detail grid)
/// </summary>
public class PredracunKnjizbaGridDto
{
    public string Stevilka { get; set; } = string.Empty;
    public int Leto { get; set; }
    public int Zs { get; set; }
    public string? SifraArtikla { get; set; }
    public string? NazivArtikla { get; set; }
    public decimal? Kolicina { get; set; }
    public decimal? ProdajnaCena { get; set; }
    public decimal? ProdajnaVrednost { get; set; }
    public decimal? Rabat1 { get; set; }
    public int? Minute { get; set; }
}
