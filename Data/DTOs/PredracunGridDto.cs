namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz predraèuna v gridu
/// </summary>
public class PredracunGridDto
{
    public string Stevilka { get; set; } = string.Empty;
    public int Leto { get; set; }
    public DateTime? Datum { get; set; }
    public int SifraKupca { get; set; }
    public string? NazivPartnerja { get; set; }
    public string? SifraKomercialista { get; set; }
    public string? NazivKomercialista { get; set; }
    public int? Stanje { get; set; }
    public decimal? ZnesekKoncni { get; set; }
    public decimal? Placano { get; set; }
    public decimal? PlacanoIzRacunov { get; set; }
    
    /// <summary>
    /// Prikaz stanja: "Potrjen", "Plaèano", "Delno", "Porabljeno" ali prazen
    /// </summary>
    public string StanjePrikaz
    {
        get
        {
            // Minute porabljene
            if (Minute > 0 && MinutePreostalo <= 0)
                return "Porabljeno";

            // Preveri plaèila
            if (Placano.HasValue && Placano.Value >= 1 && ZnesekKoncni.HasValue && ZnesekKoncni.Value > 0)
            {
                if (Placano.Value >= ZnesekKoncni.Value)
                    return "Plaèano";
                else
                    return "Delno";
            }

            // Stanje = 2 ali 5 je potrjen
            if (Stanje == 5)
                return "Potrjen";
            if (Stanje == 2)
                return "Plaèan vpisan2";

            return "";
        }
    }

    /// <summary>
    /// Prikaz statusa raèuna: "Raèun", "Delno" ali prazen
    /// </summary>
    public string RacunStatus
    {
        get
        {
            if (PlacanoIzRacunov.HasValue && PlacanoIzRacunov.Value >= 1 && ZnesekKoncni.HasValue && ZnesekKoncni.Value > 0)
            {
                if (PlacanoIzRacunov.Value >= ZnesekKoncni.Value)
                    return "Raèun";
                else
                    return $"Delno ({PlacanoIzRacunov.Value:N2})";
            }

            return "";
        }
    }

    /// <summary>
    /// Znesek brez DDV (22%).
    /// </summary>
    public decimal? ZnesekBrezDdv => ZnesekKoncni.HasValue ? Math.Round(ZnesekKoncni.Value / 1.22m, 2) : null;

    /// <summary>
    /// Minute iz predraèuna (iz postavk, ki imajo artikel v OBRACUN_PAKET_MINUTE).
    /// </summary>
    public int Minute { get; set; }

    /// <summary>
    /// Preostale minute (Minute - poraba v preteklih mesecih).
    /// </summary>
    public int MinutePreostalo { get; set; }

    /// <summary>
    /// Številke povezanih raèunov (iz FA_RACUN).
    /// </summary>
    public string? PovezaniRacuni { get; set; }

    /// <summary>
    /// Unikaten kljuè za grid (Stevilka_Leto)
    /// </summary>
    public string Key => $"{Stevilka}_{Leto}";

    /// <summary>
    /// Ali je predraèun povezan z nalogom (iz OBRACUN_DN_PREDRACUN).
    /// </summary>
    public bool Povezan { get; set; }

    /// <summary>
    /// Originalno stanje ob nalaganju (za detekcijo sprememb).
    /// </summary>
    public bool PovezanOriginal { get; set; }
}
