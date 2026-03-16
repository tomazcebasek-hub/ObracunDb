namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz partner minut v gridu
/// </summary>
public class PartnerMinuteGridDto
{
    public int Id { get; set; }
    public int Partner { get; set; }
    public string NazivPartnerja { get; set; } = string.Empty;
    public DateTime Datum { get; set; }
    public decimal Minut { get; set; }
    public int VeljavnostMesecih { get; set; }
    public string? Opomba { get; set; }
    public int? ZacetekMesec { get; set; }
    public int? ZacetekLeto { get; set; }
    public string Uporabnik { get; set; } = "";

    /// <summary>
    /// Preostale minute (Minut - vsota porabe v preteklih mesecih).
    /// </summary>
    public int Preostalo { get; set; }

    /// <summary>
    /// Poraba po mesecih (mesec/leto -> količina).
    /// </summary>
    public List<PorabaMesecDto> PorabaPoMesecih { get; set; } = new();
}

/// <summary>
/// Poraba minut v enem mesecu.
/// </summary>
public class PorabaMesecDto
{
    public int Mesec { get; set; }
    public int Leto { get; set; }
    public int Kolicina { get; set; }
}
