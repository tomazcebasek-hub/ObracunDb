using ObracunDb.Data.Entities;

namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz uporabnika v gridu.
/// </summary>
public class UporabnikGridDto
{
    public int Id { get; set; }
    public string UporabniskoIme { get; set; } = string.Empty;
    public UporabnikVloga Vloga { get; set; }
    public string VlogaText => Vloga switch
    {
        UporabnikVloga.Admin => "Admin",
        UporabnikVloga.Vodja => "Vodja",
        UporabnikVloga.Potrjevalec => "Potrjevalec",
        _ => Vloga.ToString()
    };
    public bool Aktiven { get; set; }
    public bool PrvaPrijava { get; set; }
    public DateTime DatumUstvarjen { get; set; }
    public DateTime? DatumZadnjaPrijava { get; set; }
}
