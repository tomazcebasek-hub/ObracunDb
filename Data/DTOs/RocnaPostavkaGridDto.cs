namespace ObracunDb.Data.DTOs;

public class RocnaPostavkaGridDto
{
    public int Zs { get; set; }
    public string? NalogStevilka { get; set; }
    public int? NalogLeto { get; set; }
    public DateTime? DatumNaloga { get; set; }
    public string? Artikel { get; set; }
    public string? NazivArtikla { get; set; }
    public decimal Kolicina { get; set; }
    public decimal ProdajnaCena { get; set; }
    public int Minute { get; set; }

    public string Key => $"{Zs}";
}
