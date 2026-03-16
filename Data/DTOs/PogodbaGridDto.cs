namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz pogodbe partnerja v zavihku Pogodbe.
/// </summary>
public class PogodbaGridDto
{
    /// <summary>Ključ za grid (Stevilka/Leto).</summary>
    public string Key => $"{Stevilka}/{Leto}";

    public int Stevilka { get; set; }
    public int Leto { get; set; }

    /// <summary>Številka pogodbe (prikazna).</summary>
    public string? StPogodbe { get; set; }

    /// <summary>Datum podpisa.</summary>
    public DateTime? Datum { get; set; }

    /// <summary>Veljavnost od (prvi račun).</summary>
    public DateTime? PrviRacunOd { get; set; }

    /// <summary>Veljavnost do.</summary>
    public DateTime? VeljaDo { get; set; }

    /// <summary>Na koliko mesecev se izstavlja.</summary>
    public int? NaKolikoMesecev { get; set; }

    /// <summary>Vključene minute.</summary>
    public int? StMinut { get; set; }

    /// <summary>Opomba.</summary>
    public string? Opomba { get; set; }

    /// <summary>Tip pogodbe (SIF_NAPREJ_NAZAJ).</summary>
    public int? SifNaprejNazaj { get; set; }
}

/// <summary>
/// DTO za prikaz pozicije pogodbe (desni panel).
/// </summary>
public class PogodbaPozGridDto
{
    public int Pozicija { get; set; }

    /// <summary>Šifra artikla.</summary>
    public string? Sifra { get; set; }

    /// <summary>Naziv artikla (FullName).</summary>
    public string? Naziv { get; set; }

    /// <summary>Količina.</summary>
    public decimal Kolicina { get; set; }

    /// <summary>Prodajna cena.</summary>
    public decimal Cena { get; set; }

    /// <summary>Rabat.</summary>
    public decimal Rabat { get; set; }

    /// <summary>Meseci v katerih se izstavi (surovi string, npr. "01,02,03").</summary>
    public string? MeseciRaw { get; set; }

    /// <summary>Meseci prevedeni v berljivo obliko (npr. "jan, feb, mar").</summary>
    public string? Meseci { get; set; }
}
