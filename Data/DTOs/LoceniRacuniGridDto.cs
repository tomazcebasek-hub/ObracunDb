namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz ločenih računov v gridu
/// </summary>
public class LoceniRacuniGridDto
{
    public int Id { get; set; }
    public int Partner { get; set; }
    public string NazivPartnerja { get; set; } = string.Empty;
    public int Prodajalna { get; set; }
    public string NazivProdajalne { get; set; } = string.Empty;
    public int PogodbaStevilka { get; set; }
    public int PogodbaLeto { get; set; }
    public string? StPogodbe { get; set; }
    public DateTime DatumVnosa { get; set; }
    public string Uporabnik { get; set; } = string.Empty;

    public string PogodbaDisplay => StPogodbe ?? $"{PogodbaStevilka}/{PogodbaLeto}";
}

/// <summary>
/// DTO za prodajalno (za pickup dialog)
/// </summary>
public class ProdajalnaDto
{
    public int Sifra { get; set; }
    public string Naziv { get; set; } = string.Empty;
    public string DisplayName => $"{Sifra} - {Naziv}";
}

/// <summary>
/// DTO za dodelitev prodajalne pogodbi
/// </summary>
public class PogodbaDodelitevDto
{
    public int Stevilka { get; set; }
    public int Leto { get; set; }
    public string? StPogodbe { get; set; }
    public DateTime? VeljaDo { get; set; }
    public int IzbranaProdajalna { get; set; }

    public string Display => StPogodbe ?? $"{Stevilka}/{Leto}";
}
