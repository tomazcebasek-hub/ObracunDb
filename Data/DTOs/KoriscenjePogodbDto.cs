namespace ObracunDb.Data.DTOs;

public class KoriscenjePogodbDto
{
    public int Partner { get; set; }
    public string? NazivPartnerja { get; set; }
    public int SteviloPogodb { get; set; }
    public int VsotaMinut { get; set; }
    public decimal Znesek { get; set; }
    public string? Interval { get; set; }
    public decimal ZnesekIzbranMesec { get; set; }
    public decimal Racun { get; set; }
}
