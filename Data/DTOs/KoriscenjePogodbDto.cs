namespace ObracunDb.Data.DTOs;

public class KoriscenjePogodbDto
{
    public int Partner { get; set; }
    public string? NazivPartnerja { get; set; }
    public int SteviloPogodb { get; set; }
    public int VsotaMinut { get; set; }
    public int KoriscenoMinut { get; set; }
    public int SkupajRazpolozljivih { get; set; }
    public decimal ProcentKoriscenja => SkupajRazpolozljivih > 0
        ? Math.Round((decimal)KoriscenoMinut / SkupajRazpolozljivih * 100, 1)
        : 0;
}
