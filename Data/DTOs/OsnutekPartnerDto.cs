namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz partnerja v zgornjem gridu na strani Osnutki.
/// Vsebuje tudi podatke za Info panel, da se ne pridobivajo dvakrat.
/// Vir: OBRACUN_OSNUTEK + agregat iz OBRACUN_OSNUTEK_NALOG_OBRACUN + FA_POGODBE.
/// </summary>
public class OsnutekPartnerDto
{
    // === Grid stolpci ===

    /// <summary>Šifra partnerja.</summary>
    public int Sifra { get; set; }

    /// <summary>Naziv partnerja (iz tabele PARTNER).</summary>
    public string? NazivPartnerja { get; set; }

    /// <summary>Ali ima veljavno pogodbo ta mesec (IMA_POGODBO iz OBRACUN_OSNUTEK).</summary>
    public bool ImaPogodbo { get; set; }

    /// <summary>Ali ima predračun s statusom 5 ali plačane (IMA_PREDRACUN iz OBRACUN_OSNUTEK).</summary>
    public bool ImaPredracun { get; set; }

    /// <summary>Ali ima naloge (IMA_NALOGE iz OBRACUN_OSNUTEK).</summary>
    public bool ImaNaloge { get; set; }

    /// <summary>Skupaj minute (obračunane bruto + neobračunane). Izračunano: MinuteObracunaneBruto + MinNeobr.</summary>
    public int Minute => MinuteObracunaneBruto + MinNeobr;

    /// <summary>Minute, ki se NE obračunajo (MINUTE_NEOBRACUNANE iz OBRACUN_OSNUTEK).</summary>
    public int MinNeobr { get; set; }

    /// <summary>Koriščene minute iz pogodbe (SUM MINUTE_ODSTETE_POGODBA iz NALOG_OBRACUN).</summary>
    public int KorPog { get; set; }

    /// <summary>Koriščene minute iz predračunov (SUM MINUTE_ODSTETE_PREDRACUN iz NALOG_OBRACUN).</summary>
    public int KorPre { get; set; }

    /// <summary>Koriščene minute ročno / paketni artikli (SUM MINUTE_ODSTETE_ROCNO iz NALOG_OBRACUN).</summary>
    public int KorRoc { get; set; }

    /// <summary>Koriščene minute partner minute / projektni listi (SUM MINUTE_ODSTETE_PARTNER_MINUTE iz NALOG_OBRACUN).</summary>
    public int KorPar { get; set; }

    /// <summary>Zaračunane minute (MINUTE_OBRACUNANE iz OBRACUN_OSNUTEK = bruto obračunane - koriščene).</summary>
    public int ZaracMin { get; set; }

    // === Info panel podatki ===

    /// <summary>Bruto obračunane minute (pred odštevanjem koriščenih). Izračunano: ZaracMin + MinuteKoriscene.</summary>
    public int MinuteObracunaneBruto { get; set; }

    /// <summary>Skupaj koriščene minute (MINUTE_KORISCENE iz OBRACUN_OSNUTEK).</summary>
    public int MinuteKoriscene { get; set; }

    /// <summary>Plus minute iz predračunov (PLUS_MINUTE_PREDRACUN iz OBRACUN_OSNUTEK).</summary>
    public int PlusMinutePredracun { get; set; }

    /// <summary>Plus minute ročno (PLUS_MINUTE_ROCNO iz OBRACUN_OSNUTEK).</summary>
    public int PlusMinuteRocno { get; set; }

    /// <summary>Plus minute iz pogodb (PLUS_MINUTE_POGODBA iz OBRACUN_OSNUTEK).</summary>
    public int PlusMinutePogodba { get; set; }

    /// <summary>Plus minute iz partner minute (PLUS_MINUTE_PARTNER_MINUTE iz OBRACUN_OSNUTEK).</summary>
    public int PlusMinutePartnerMinute { get; set; }

    /// <summary>Vse minute iz predračunov (pred odštevanjem porab).</summary>
    public int VseMinutePredracun { get; set; }

    /// <summary>Že porabljene minute predračunov v preteklih mesecih.</summary>
    public int ZePorabljenePredracun { get; set; }

    /// <summary>Vse minute iz partner minute (pred odštevanjem porab).</summary>
    public int VseMinutePartnerMinute { get; set; }

    /// <summary>Že porabljene minute partner minute v preteklih mesecih.</summary>
    public int ZePorabljenePartnerMinute { get; set; }

    /// <summary>Skupaj minute v plus.</summary>
    public int SkupajMinuteVPlus => PlusMinutePredracun + PlusMinuteRocno + PlusMinutePogodba + PlusMinutePartnerMinute;

    /// <summary>Število veljavnih pogodb (iz FA_POGODBE).</summary>
    public int SteviloPogodb { get; set; }

    /// <summary>Opis obračuna (OPIS iz OBRACUN_OSNUTEK, za Info panel).</summary>
    public string? Opis { get; set; }

    /// <summary>Kdo je potrdil osnutek (iz OBRACUN_OSNUTEK_POTRDITEV).</summary>
    public string? PotrdilKdo { get; set; }

    /// <summary>Kdaj je bil osnutek potrjen (iz OBRACUN_OSNUTEK_POTRDITEV).</summary>
    public DateTime? PotrdilKdaj { get; set; }
}
