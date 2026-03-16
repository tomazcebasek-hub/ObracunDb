namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za seštevek postavk po artiklu.
/// </summary>
public class SestevekGridDto
{
    public string Sifra { get; set; } = "";
    public string Naziv { get; set; } = "";
    public string Enota { get; set; } = "";
    public decimal Kolicina { get; set; }
    public decimal Vrednost { get; set; }
    public decimal Popust { get; set; }
    public decimal NetoVrednost { get; set; }
    public int StPartnerjev { get; set; }
}
