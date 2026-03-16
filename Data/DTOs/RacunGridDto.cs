namespace ObracunDb.Data.DTOs;

public class RacunGridDto
{
    public string Stevilka { get; set; } = "";
    public int Leto { get; set; }
    public DateTime? Datum { get; set; }
    public decimal? ZnesekKoncni { get; set; }
    public int TipRacuna { get; set; }
    public string Key => $"{Stevilka}_{Leto}";
}
