namespace ObracunDb.Data.DTOs;

public class PredracunKomercialistGrafDto
{
    public string Mesec { get; set; } = string.Empty;
    public DateTime MesecDatum { get; set; }
    public string Komercialist { get; set; } = string.Empty;
    public decimal Znesek { get; set; }
    public int Kolicina { get; set; }
}
