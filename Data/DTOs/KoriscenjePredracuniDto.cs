namespace ObracunDb.Data.DTOs;

public class KoriscenjePredracuniDto
{
    public int Partner { get; set; }
    public string? NazivPartnerja { get; set; }
    public string Stevilka { get; set; } = "";
    public int Leto { get; set; }
    public DateTime? Datum { get; set; }
    public int Minute { get; set; }
    /// <summary>Poraba minut po obračunskih mesecih (key = "1-26").</summary>
    public Dictionary<string, int> Poraba { get; set; } = new();
    public int SkupajPorabljeno => Poraba.Values.Sum();
    public int Preostalo => Minute - SkupajPorabljeno;
    public string Key => $"{Stevilka}_{Leto}";
}
