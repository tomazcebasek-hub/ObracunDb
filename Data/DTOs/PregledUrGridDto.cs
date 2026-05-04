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
    public decimal UreStranke => SkupajUre - UreNom - UrePartner23900;
}
