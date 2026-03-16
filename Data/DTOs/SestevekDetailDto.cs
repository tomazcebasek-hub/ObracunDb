namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za seštevek postavk po partnerju za en artikel (podgrid).
/// </summary>
public class SestevekDetailDto
{
    public int SifraPartnerja { get; set; }
    public string NazivPartnerja { get; set; } = "";
    public decimal Kolicina { get; set; }
    public decimal Vrednost { get; set; }
    public decimal Popust { get; set; }
    public decimal NetoVrednost { get; set; }
}
