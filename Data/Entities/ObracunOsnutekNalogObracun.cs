using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

/// <summary>
/// Tabela za shranjevanje podrobnosti obračuna nalogov.
/// </summary>
[Table("OBRACUN_OSNUTEK_NALOG_OBRACUN")]
public class ObracunOsnutekNalogObracun
{
    [Column("MESEC")]
    public int Mesec { get; set; }

    [Column("LETO")]
    public int Leto { get; set; }

    [Column("PARTNER")]
    public int Partner { get; set; }

    [Column("STEVILKA_NALOGA")]
    public string StevilkaNaloga { get; set; } = string.Empty;

    [Column("LETO_NALOGA")]
    public int LetoNaloga { get; set; }

    [Column("OBRACUNAM")]
    public int Obracunam { get; set; }

    [Column("SIFRA_ARTIKLA")]
    public string? SifraArtikla { get; set; }

    [Column("SIFRA_KOMERCIALISTA")]
    public string? SifraKomercialista { get; set; }

    [Column("KOLICINA")]
    public decimal? Kolicina { get; set; }

    [Column("PRODAJNA_CENA")]
    public decimal? ProdajnaCena { get; set; }

    [Column("MINUTE_ODSTETE_PARTNER_MINUTE")]
    public int? MinuteOdstetePartnerMinute { get; set; }

    [Column("MINUTE_ODSTETE_PREDRACUN")]
    public int? MinuteOdstetePredracun { get; set; }

    [Column("MINUTE_ODSTETE_ROCNO")]
    public int? MinuteOdsteteRocno { get; set; }

    [Column("MINUTE_ODSTETE_POGODBA")]
    public int? MinuteOdstetePogodba { get; set; }

    [Column("MINUTE_NALOG")]
    public int? MinuteNalog { get; set; }

    [Column("KOLICINA_FAKTURIRANA")]
    public decimal? KolicinaFakturirana { get; set; }
}
