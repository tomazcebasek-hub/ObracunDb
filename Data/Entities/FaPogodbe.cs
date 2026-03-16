using LinqToDB.Mapping;

namespace ObracunDb.Data.Entities;

[Table("FA_POGODBE")]
public class FaPogodbe
{
    [Column("STEVILKA"), PrimaryKey]
    public int Stevilka { get; set; }

    [Column("LETO"), PrimaryKey]
    public int Leto { get; set; }

    [Column("PARTNER")]
    public int Partner { get; set; }

    [Column("DATUM")]
    public DateTime? Datum { get; set; }

    [Column("ST_POGODBE")]
    public string? StPogodbe { get; set; }

    [Column("PRVI_RACUN_OD")]
    public DateTime? PrviRacunOd { get; set; }

    [Column("VELJA_DO")]
    public DateTime? VeljaDo { get; set; }

    [Column("NA_KOLIKO_MESECEV")]
    public int? NaKolikoMesecev { get; set; }

    [Column("ST_MINUT")]
    public int? StMinut { get; set; }

    [Column("SIF_NAPREJ_NAZAJ")]
    public int? SifNaprejNazaj { get; set; }

    [Column("OPOMBA")]
    public string? Opomba { get; set; }

    [Column("ROK_PLACILA")]
    public int? RokPlacila { get; set; }
}
