namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za statistiko nalogov po mesecu (skupaj, potrjeni, nepotrjeni).
/// </summary>
public class NalogStatistikaDto
{
    public int Mesec { get; set; }
    public int Leto { get; set; }
    public int Skupaj { get; set; }
    public int Potrjeni { get; set; }
    public int Nepotrjeni { get; set; }

    public string Obdobje => $"{Mesec}/{Leto}";
    public double PctPotrjeni => Skupaj > 0 ? (double)Potrjeni / Skupaj * 100 : 0;
    public double PctNepotrjeni => Skupaj > 0 ? (double)Nepotrjeni / Skupaj * 100 : 0;
}
