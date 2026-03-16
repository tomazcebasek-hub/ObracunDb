namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz postavke naloga v gridu Potrjevanje nalogov.
/// </summary>
public class PotrjevanjeNalogPozDto
{
    public int Zs { get; set; }
    public string? Sifra { get; set; }
    public string? NazivArtikla { get; set; }
    public string? Enota { get; set; }
    public decimal Kolicina { get; set; }
    public decimal Cena { get; set; }
    public decimal Rabat { get; set; }
    public decimal Vrednost => Kolicina * Cena * (1 - Rabat / 100);

    /// <summary>
    /// Ali je postavka ročni vnos (iz OBRACUN_OSNUTEK_POS, TIP_POSTAVKE=1).
    /// </summary>
    public bool JeRocni { get; set; }

    // PK za brisanje ročne postavke iz OBRACUN_OSNUTEK_POS
    internal int RocniMesec { get; set; }
    internal int RocniLeto { get; set; }
    internal int RocniPartner { get; set; }
    internal int RocniZs { get; set; }
}
