namespace ObracunDb.Data.DTOs;

public class PredracunKomercialistTipGrafDto
{
    public string Komercialist { get; set; } = string.Empty;
    public string Stanje { get; set; } = string.Empty;
    public int Kolicina { get; set; }
    public decimal Znesek { get; set; }
}
