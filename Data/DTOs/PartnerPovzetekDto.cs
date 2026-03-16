namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz povzetka partnerja na Info zavihku
/// </summary>
public class PartnerPovzetekDto
{
    public int MinuteObracunane { get; set; }
    public int MinuteNeobracunane { get; set; }
    public int SkupajMinute => MinuteObracunane + MinuteNeobracunane;

    public int SteviloPogodb { get; set; }

    public int PlusMinutePredracun { get; set; }
    public int PlusMinuteRocno { get; set; }
    public int PlusMinutePogodba { get; set; }
    public int PlusMinutePartnerMinute { get; set; }
    public int SkupajMinuteVPlus => PlusMinutePredracun + PlusMinuteRocno + PlusMinutePogodba + PlusMinutePartnerMinute;

    public int KoriscenoPartnerMinute { get; set; }
    public int KoriscenoPredracun { get; set; }
    public int KoriscenoRocno { get; set; }
    public int KoriscenoPogodba { get; set; }
}
