

using ObracunDb.Data;
using ObracunDb.Data.Entities;

namespace ObracunDb.Services
{
    /// <summary>
    /// Kontekst z vsemi podatki potrebnimi za obračun.
    /// </summary>
    public class ObracunContext
    {
        public required ObracunLinqDb Db { get; init; }
        public required int Mesec { get; init; }
        public required int Leto { get; init; }
        public required string MesecStr { get; init; }
        public required List<string> Log { get; init; }

        // Prednaloženi podatki
        public required List<FaDnNalog> Nalogi { get; init; }
        public required List<FaDnNalogPoz> PostavkeNalogov { get; init; }
        public required List<FaPogodbe> AktivnePogodbe { get; init; }
        public required List<FaPogodbePoz> PostavkePogodb { get; init; }
        public required List<ObracunOsnutekPos> RocnePostavke { get; init; }
        public required List<FaPredracun> Predracuni { get; init; }
        public required List<FaPredracunKnjizba> PostavkePredracunov { get; init; }
        public required Dictionary<string, ArtikelInfo> Artikli { get; init; }
        public required Dictionary<string, int> MinuteArtiklov { get; init; }
        public required Dictionary<(string Stevilka, int Leto), ObracunDn> ObracunDnSlovar { get; init; }
        public required List<PartnerMinute> PartnerMinute { get; init; }

        // šifra artikla za kilometrino (iz parametrov)
        public required string SifraKilometrina { get; init; }

        // Servisne nastavitve za obračun
        public required ServisneNastavitve ServisneNastavitve { get; init; }
        public required ServisneNastavitve TerenServisneNastavitve { get; init; }

        // Popust za pogodbene stranke (v procentih)
        public required decimal PopustPogodbe { get; init; }

        // Toleranca minut - če je preostalih minut za obračun manj od te vrednosti, se ne obračunajo
        public required int TolerancaMinut { get; init; }

        // Prazniki za mesec obračuna
        public HashSet<DateTime> Prazniki { get; init; } = new();

        // Že porabljene minute iz prejšnjih mesecev (po ID_OBRACUN_MINUTE)
        public Dictionary<int, int> ZePorabljenePartnerMinute { get; init; } = new();

        // Že porabljene minute iz predračunov (po (Stevilka, Leto))
        public Dictionary<(string Stevilka, int Leto), int> ZePorabljenePredracuni { get; init; } = new();

        // Partnerji, ki nimajo aktivne pogodbe, a imajo pogodbo, ki začne veljati v prihodnosti
        public HashSet<int> PartnerjiSPrihodnjoPogodbo { get; init; } = new();

        // Povezave nalog → predračuni (iz OBRACUN_DN_PREDRACUN)
        // Ključ: (StevilkaNaloga, LetoNaloga), Vrednost: set povezanih predračunov (PredracunStevilka, PredracunLeto)
        public Dictionary<(string Stevilka, int Leto), HashSet<(string PredStevilka, int PredLeto)>> NalogPredracunPovezave { get; init; } = new();
    }

    /// <summary>
    /// Info o artiklu za hitrejši dostop.
    /// </summary>
    public class ArtikelInfo
    {
        public required string Sifra { get; init; }
        public required string Naziv { get; init; }
        public required string Enota { get; init; }
        public required decimal ProdajnaCena { get; init; }
    }

    /// <summary>
    /// Podatki za obračun enega partnerja.
    /// </summary>
    public class PartnerObracunData
    {
        public required int Partner { get; init; }
        public required List<FaPogodbe> Pogodbe { get; init; }
        public required List<FaDnNalog> Nalogi { get; init; }
        public required List<FaPredracun> Predracuni { get; init; }
        public required List<ObracunOsnutekPos> RocnePostavke { get; init; }
        public required int NaslednjZs { get; init; }
    }

    /// <summary>
    /// Rezultat obračuna za enega partnerja.
    /// </summary>
    public class PartnerObracunResult
    {
        public int Partner { get; set; }
        public string Opis { get; set; } = "";
        public int MinutePredracuni { get; set; }
        public int VseMinutePredracuni { get; set; }
        public int ZePorabljenePredracuni { get; set; }
        public int MinuteRocni { get; set; }
        public int MinutePogodbe { get; set; }
        public int MinutePartnerMinute { get; set; }
        public int VseMinutePartnerMinute { get; set; }
        public int ZePorabljenePartnerMinute { get; set; }
        public int MinuteVPlus => MinutePredracuni + MinuteRocni + MinutePogodbe + MinutePartnerMinute;
        public bool ImaPogodbo { get; set; }
        public bool ImaNaloge { get; set; }
        public bool LetnaPogodba { get; set; }

        /// <summary>
        /// Razdelitev minut iz nalogov po tarifah.
        /// </summary>
        public MinuteRazdelitev MinuteNalogov { get; set; } = new();

        /// <summary>
        /// Minute nalogov ki se obračunajo in zaračunajo (po odštetu dobroimetju).
        /// </summary>
        public int MinuteObracunane { get; set; }

        /// <summary>
        /// Minute nalogov ki se NE obračunajo.
        /// </summary>
        public int MinuteNeobracunane { get; set; }

        /// <summary>
        /// Minute, ki so bile koriščene iz dobroimetja (pogodbe, predračuni, ročno) in ne bodo zaračunane.
        /// </summary>
        public int MinuteKoriscene { get; set; }
    }


    /// <summary>
    /// šifre artiklov za obračun servisnih storitev.
    /// </summary>
    public class ServisneNastavitve
    {
        // === BREZ POGODBE ===
        // Delavnik
        public string BrezPogodbeDel7_16 { get; set; } = "";
        public string BrezPogodbeDel16_22 { get; set; } = "";
        public string BrezPogodbeDel22_7 { get; set; } = "";

        // Vikend
        public string BrezPogodbeVik7_16 { get; set; } = "";
        public string BrezPogodbeVik16_22 { get; set; } = "";
        public string BrezPogodbeVik22_7 { get; set; } = "";

        // Praznik
        public string BrezPogodbeP7_16 { get; set; } = "";
        public string BrezPogodbeP16_22 { get; set; } = "";
        public string BrezPogodbeP22_7 { get; set; } = "";

        // === S POGODBO ===
        // Delavnik
        public string PogodbaDel7_16 { get; set; } = "";
        public string PogodbaDel16_22 { get; set; } = "";
        public string PogodbaDel22_7 { get; set; } = "";

        // Vikend
        public string PogodbaVik7_16 { get; set; } = "";
        public string PogodbaVik16_22 { get; set; } = "";
        public string PogodbaVik22_7 { get; set; } = "";

        // Praznik
        public string PogodbaP7_16 { get; set; } = "";
        public string PogodbaP16_22 { get; set; } = "";
        public string PogodbaP22_7 { get; set; } = "";

        /// <summary>
        /// Vrne šifro artikla za dano obdobje (brez pogodbe).
        /// </summary>
        public string? GetSifraBrezPogodbe(TipDneva tipDneva, CasovnaTarifa tarifa)
        {
            return (tipDneva, tarifa) switch
            {
                (TipDneva.Delavnik, CasovnaTarifa.Dnevna) => BrezPogodbeDel7_16,
                (TipDneva.Delavnik, CasovnaTarifa.Popoldanska) => BrezPogodbeDel16_22,
                (TipDneva.Delavnik, CasovnaTarifa.Nocna) => BrezPogodbeDel22_7,
                (TipDneva.Vikend, CasovnaTarifa.Dnevna) => BrezPogodbeVik7_16,
                (TipDneva.Vikend, CasovnaTarifa.Popoldanska) => BrezPogodbeVik16_22,
                (TipDneva.Vikend, CasovnaTarifa.Nocna) => BrezPogodbeVik22_7,
                (TipDneva.Praznik, CasovnaTarifa.Dnevna) => BrezPogodbeP7_16,
                (TipDneva.Praznik, CasovnaTarifa.Popoldanska) => BrezPogodbeP16_22,
                (TipDneva.Praznik, CasovnaTarifa.Nocna) => BrezPogodbeP22_7,
                _ => null
            };
        }

        /// <summary>
        /// Vrne šifro artikla za dano obdobje (s pogodbo).
        /// </summary>
        public string? GetSifraPogodba(TipDneva tipDneva, CasovnaTarifa tarifa)
        {
            return (tipDneva, tarifa) switch
            {
                (TipDneva.Delavnik, CasovnaTarifa.Dnevna) => PogodbaDel7_16,
                (TipDneva.Delavnik, CasovnaTarifa.Popoldanska) => PogodbaDel16_22,
                (TipDneva.Delavnik, CasovnaTarifa.Nocna) => PogodbaDel22_7,
                (TipDneva.Vikend, CasovnaTarifa.Dnevna) => PogodbaVik7_16,
                (TipDneva.Vikend, CasovnaTarifa.Popoldanska) => PogodbaVik16_22,
                (TipDneva.Vikend, CasovnaTarifa.Nocna) => PogodbaVik22_7,
                (TipDneva.Praznik, CasovnaTarifa.Dnevna) => PogodbaP7_16,
                (TipDneva.Praznik, CasovnaTarifa.Popoldanska) => PogodbaP16_22,
                (TipDneva.Praznik, CasovnaTarifa.Nocna) => PogodbaP22_7,
                _ => null
            };
        }
    }
}
