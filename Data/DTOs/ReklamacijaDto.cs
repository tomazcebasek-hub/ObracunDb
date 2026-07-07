namespace ObracunDb.Data.DTOs;

public class ReklamacijaGridDto
{
    public int Id { get; set; }
    public ObracunDb.Data.Entities.TipReklamacije TipReklamacije { get; set; }
    public string TipReklamacijeNaziv => TipReklamacije switch
    {
        ObracunDb.Data.Entities.TipReklamacije.PrekinitevPogodbe => "Prekinitev pogodbe",
        ObracunDb.Data.Entities.TipReklamacije.Reklamacija => "Reklamacija",
        _ => TipReklamacije.ToString()
    };
    public int Partner { get; set; }
    public string NazivPartnerja { get; set; } = string.Empty;
    public string PartnerDisplay => $"{Partner} - {NazivPartnerja}";
    public DateTime DatumZahteve { get; set; }
    public string? StevilkePogodb { get; set; }
    public string? Kontakt { get; set; }
    public string? TipPrekinitve { get; set; }
    public DateTime? RacuniDoDne { get; set; }
    public string? Opis { get; set; }
    public string? KdoNajObdela { get; set; }
    public DateTime? DatumPosredovanja { get; set; }
    public int SteviloVnosov { get; set; }
    public int? ZadnjiStatusId { get; set; }
    public string? ZadnjiStatusBarva { get; set; }
}

public class ReklamacijaPostavkaDto
{
    public DateTime Datum { get; set; }
    public string Uporabnik { get; set; } = string.Empty;
    public string? Komentar { get; set; }
    public string? StatusNaziv { get; set; }
}

public class ReklamacijaStatusSifrantDto
{
    public int Id { get; set; }
    public string Naziv { get; set; } = string.Empty;
    public string Barva { get; set; } = "#F5F5F5";
}

public class ReklamacijaFormDto
{
    public ObracunDb.Data.Entities.TipReklamacije TipReklamacije { get; set; } = ObracunDb.Data.Entities.TipReklamacije.PrekinitevPogodbe;
    public int Partner { get; set; }
    public string? Kontakt { get; set; }
    public string? TipPrekinitve { get; set; }
    public DateTime? RacuniDoDne { get; set; }
    public string? Opis { get; set; }
    public string? Komentar { get; set; }
    public string? KdoNajObdela { get; set; }
    public int? StatusId { get; set; }
    public List<PogodbaZaReklamacijoDto> Pogodbe { get; set; } = new();
}

public class PogodbaZaReklamacijoDto
{
    public int Stevilka { get; set; }
    public int Leto { get; set; }
    public string? StPogodbe { get; set; }
    public decimal Znesek { get; set; }
    public DateTime? VeljaDo { get; set; }
    public bool Prekini { get; set; }
    public string DisplayStevilka => string.IsNullOrWhiteSpace(StPogodbe) ? $"{Stevilka}/{Leto}" : StPogodbe;
}

public class PartnerReklamacijaDto
{
    public int Sifra { get; set; }
    public string Naziv { get; set; } = string.Empty;
    public string? EPosta { get; set; }
    public string DisplayName => $"{Sifra} - {Naziv}";
}

public class ReklamacijaPrilogaDto
{
    public int Id { get; set; }
    public int IdReklamacija { get; set; }
    public string ImeDatoteke { get; set; } = string.Empty;
    public string TipVsebine { get; set; } = string.Empty;
    public int Velikost { get; set; }
    public DateTime Datum { get; set; }
    public string Uporabnik { get; set; } = string.Empty;
    public string VelikostText => Velikost >= 1024 * 1024
        ? $"{Velikost / 1024m / 1024m:N1} MB"
        : $"{Velikost / 1024m:N0} KB";
}

public class ReklamacijaPrilogaVsebinaDto : ReklamacijaPrilogaDto
{
    public byte[] Vsebina { get; set; } = Array.Empty<byte>();
    public string DataUrl => $"data:{TipVsebine};base64,{Convert.ToBase64String(Vsebina)}";
    public bool JeSlika => TipVsebine.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    public bool JePdf => string.Equals(TipVsebine, "application/pdf", StringComparison.OrdinalIgnoreCase);
}

public class ReklamacijaPrilogaVnosDto
{
    public string ImeDatoteke { get; set; } = string.Empty;
    public string TipVsebine { get; set; } = "application/octet-stream";
    public byte[] Vsebina { get; set; } = Array.Empty<byte>();
    public int Velikost => Vsebina.Length;
}

public class ReklamacijaFawPogodbaDto
{
    public int Stevilka { get; set; }
    public int Leto { get; set; }
    public string? StPogodbe { get; set; }
    public string DisplayStevilka => string.IsNullOrWhiteSpace(StPogodbe) ? $"{Stevilka}/{Leto}" : StPogodbe;
    public DateTime? StariDatumVeljavnosti { get; set; }
    public DateTime NoviDatumVeljavnosti { get; set; }
}

public class ReklamacijaFawPreviewDto
{
    public int IdReklamacija { get; set; }
    public int Partner { get; set; }
    public string NazivPartnerja { get; set; } = string.Empty;
    public DateTime RacuniDoDne { get; set; }
    public List<ReklamacijaFawPogodbaDto> Pogodbe { get; set; } = new();
}
