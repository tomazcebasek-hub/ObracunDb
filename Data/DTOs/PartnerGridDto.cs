namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za prikaz partnerja v gridu
/// </summary>
public class PartnerGridDto
{
    public int Sifra { get; set; }
    public string Naziv { get; set; } = "";
    public decimal SkupniZnesekRacunov { get; set; }
    public decimal Blago { get; set; }
    public decimal BlagoNab { get; set; }
    public decimal Storitve { get; set; }
    public decimal Skupaj => Storitve + BlagoNab;
    public int SteviloNalogov { get; set; }
    public decimal UreObr { get; set; }
    public decimal UreNeobr { get; set; }
    public decimal NaUro => (UreObr + UreNeobr) == 0 ? 999999 : Skupaj / (UreObr + UreNeobr) * 60;
    public int SteviloPogodb { get; set; }
    public int? PogodbeneMinute { get; set; }
}
