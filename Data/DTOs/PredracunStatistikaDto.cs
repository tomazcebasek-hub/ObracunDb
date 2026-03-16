namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za statistiko predraèunov - ena vrstica = en mesec + eno stanje + en tip meritve
/// </summary>
public class PredracunStatistikaDto
{
    public string Mesec { get; set; } = string.Empty;
    public DateTime MesecDatum { get; set; }
    public string Stanje { get; set; } = string.Empty;
    public int Kolicina { get; set; }
    public decimal Znesek { get; set; }
}

/// <summary>
/// DTO za chart - ena vrstica = en mesec + ena serija (Stanje) + en stack (Kolièina/Znesek)
/// </summary>
public class StatistikaChartDto
{
    public string Mesec { get; set; } = string.Empty;
    public string Serija { get; set; } = string.Empty;
    public string Stack { get; set; } = string.Empty;
    public decimal Vrednost { get; set; }
}
