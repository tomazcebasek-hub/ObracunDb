namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za artikel - uporablja se v UI
/// </summary>
public class ArtikelDto
{
    public string Sifra { get; set; } = string.Empty;
    public string Naziv { get; set; } = string.Empty;
    public string? Naziv2 { get; set; }

    /// <summary>
    /// Združeno ime za prikaz v Pivot Table (ŠIFRA - NAZIV)
    /// </summary>
    public string DisplayName => $"{Sifra} - {Naziv}";

    /// <summary>
    /// Polno ime z vsemi podatki (ŠIFRA - NAZIV / NAZIV2)
    /// </summary>
    public string FullDisplayName => string.IsNullOrWhiteSpace(Naziv2) 
        ? DisplayName 
        : $"{Sifra} - {Naziv} / {Naziv2}";
}
