namespace ObracunDb.Data.DTOs;

/// <summary>
/// DTO za primerjavo zneska računov partnerja med dvema letoma.
/// Znesek je vsota računov (SUM(ZNESEK_KONCNI / 1.22), brez TIP_RACUNA = 4),
/// enako kot stolpec "Znesek računov" v glavnem gridu Partnerji.
/// </summary>
public class PartnerPrimerjavaDto
{
    public int Sifra { get; set; }
    public string Naziv { get; set; } = "";

    /// <summary>Znesek za prvo (prejšnje) leto.</summary>
    public decimal Znesek1 { get; set; }

    /// <summary>Znesek za drugo (tekoče) leto.</summary>
    public decimal Znesek2 { get; set; }

    /// <summary>Znesek za prejšnje leto do istega dneva v letu.</summary>
    public decimal Znesek1DoDanes { get; set; }

    /// <summary>Razlika med tekočim in prejšnjim letom.</summary>
    public decimal Razlika => Znesek2 - Znesek1;

    /// <summary>Razlika med tekočim letom in prejšnjim letom do istega dneva v letu.</summary>
    public decimal RazlikaDoDanes => Znesek2 - Znesek1DoDanes;

    /// <summary>Odstotek povečanja prometa med prejšnjim in tekočim letom.</summary>
    public decimal Procent
    {
        get
        {
            if (Znesek1 == 0)
                return Znesek2 == 0 ? 0 : 100;

            return Razlika / Znesek1 * 100;
        }
    }

    /// <summary>Odstotek povečanja prometa glede na prejšnje leto do istega dneva v letu.</summary>
    public decimal ProcentDoDanes
    {
        get
        {
            if (Znesek1DoDanes == 0)
                return Znesek2 == 0 ? 0 : 100;

            return RazlikaDoDanes / Znesek1DoDanes * 100;
        }
    }
}
