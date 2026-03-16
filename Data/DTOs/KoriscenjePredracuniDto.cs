namespace ObracunDb.Data.DTOs;

public class KoriscenjePredracuniDto
{
    public int Partner { get; set; }
    public string? NazivPartnerja { get; set; }

    // Predračuni preteklega meseca
    public int PretVse { get; set; }
    public int PretPreteklo { get; set; }
    public int PretMesec { get; set; }
    public int PretPreostalo => PretVse - PretPreteklo - PretMesec;

    // Predračuni trenutnega meseca
    public int TreVse { get; set; }
    public int TrePreteklo { get; set; }
    public int TreMesec { get; set; }
    public int TrePreostalo => TreVse - TrePreteklo - TreMesec;
}
