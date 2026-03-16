using ObracunDb.Data.Entities;

namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz naloga v gridu Potrjevanje nalogov.
/// </summary>
public class PotrjevanjeNalogDto
{
    public string Stevilka { get; set; } = string.Empty;
    public int Leto { get; set; }
    public int Partner { get; set; }
    public string? NazivPartnerja { get; set; }
    public string? NaslovPartnerja { get; set; }
    public string? PostaPartnerja { get; set; }
    public int Prodajalna { get; set; }
    public string? NazivProdajalne { get; set; }
    public DateTime Datum { get; set; }
    public DateTime ZacetekUra { get; set; }
    public DateTime KonecUra { get; set; }
    public int Trajanje { get; set; }
    public bool Pregledan { get; set; }
    public string? Potnik { get; set; }
    public string? NazivPotnika { get; set; }
    public string? Pogodbe { get; set; }
    public string? Opis { get; set; }
    public bool PolovicnaKilometrina { get; set; }
    public KajObracunam KajObracunam { get; set; }
    public double? Kilometri { get; set; }

    public string Key => $"{Stevilka}_{Leto}";
}
