namespace ObracunDb.Data.DTOs;

/// <summary>
/// Tip obdobja za filter obracuna (period filter type).
/// </summary>
public enum ObdobjeTip
{
    /// <summary>Trenutni mesec.</summary>
    TrenutniMesec,
    /// <summary>Pretekli mesec.</summary>
    PretekliMesec,
    /// <summary>Trenutno leto.</summary>
    TrenutnoLeto,
    /// <summary>Preteklo leto.</summary>
    PretekloLeto,
    /// <summary>Po meri - uporabnik vnese datum od in do.</summary>
    PoMeri
}

/// <summary>
/// Predstavlja izbrano obdobje filtra. Podatki obracuna so shranjeni po LETO/MESEC,
/// zato obdobje izpostavi mejni mesec/leto (od-do) in kljuc za mesecno filtriranje.
/// Model je namenjen za ponovno uporabo na vec formah.
/// </summary>
public class ObdobjeRange
{
    /// <summary>Izbrani tip obdobja.</summary>
    public ObdobjeTip Tip { get; set; }

    /// <summary>Zacetni datum obdobja (prvi dan zacetnega meseca).</summary>
    public DateTime Od { get; set; }

    /// <summary>Koncni datum obdobja (zadnji dan koncnega meseca).</summary>
    public DateTime Do { get; set; }

    /// <summary>Leto zacetnega meseca.</summary>
    public int LetoOd => Od.Year;

    /// <summary>Zacetni mesec.</summary>
    public int MesecOd => Od.Month;

    /// <summary>Leto koncnega meseca.</summary>
    public int LetoDo => Do.Year;

    /// <summary>Koncni mesec.</summary>
    public int MesecDo => Do.Month;

    /// <summary>Kljuc zacetnega meseca za poizvedbe: LETO*100 + MESEC.</summary>
    public int KljucOd => LetoOd * 100 + MesecOd;

    /// <summary>Kljuc koncnega meseca za poizvedbe: LETO*100 + MESEC.</summary>
    public int KljucDo => LetoDo * 100 + MesecDo;

    /// <summary>True, ce obdobje obsega en sam mesec.</summary>
    public bool JeEnMesec => KljucOd == KljucDo;

    /// <summary>
    /// Ustvari obdobje iz prednastavljenega tipa. Za PoMeri privzeto vrne trenutni mesec
    /// (uporabnik nato izbere datum od/do prek <see cref="Custom"/>).
    /// </summary>
    public static ObdobjeRange Create(ObdobjeTip tip, DateTime? danesOverride = null)
    {
        var danes = danesOverride ?? DateTime.Today;
        return tip switch
        {
            ObdobjeTip.TrenutniMesec => FromMonth(danes.Year, danes.Month, tip),
            ObdobjeTip.PretekliMesec => FromMonth(danes.AddMonths(-1).Year, danes.AddMonths(-1).Month, tip),
            ObdobjeTip.TrenutnoLeto => FromYear(danes.Year, tip),
            ObdobjeTip.PretekloLeto => FromYear(danes.Year - 1, tip),
            ObdobjeTip.PoMeri => FromMonth(danes.Year, danes.Month, tip),
            _ => FromMonth(danes.Year, danes.Month, tip)
        };
    }

    /// <summary>
    /// Ustvari obdobje po meri iz dveh datumov. Datuma se preslikata na cele mesece
    /// (od = prvi dan meseca datuma od, do = zadnji dan meseca datuma do).
    /// </summary>
    public static ObdobjeRange Custom(DateTime od, DateTime doDatum)
    {
        if (od > doDatum)
            (od, doDatum) = (doDatum, od);

        return new ObdobjeRange
        {
            Tip = ObdobjeTip.PoMeri,
            Od = new DateTime(od.Year, od.Month, 1),
            Do = LastDayOfMonth(doDatum.Year, doDatum.Month)
        };
    }

    private static ObdobjeRange FromMonth(int leto, int mesec, ObdobjeTip tip) => new()
    {
        Tip = tip,
        Od = new DateTime(leto, mesec, 1),
        Do = LastDayOfMonth(leto, mesec)
    };

    private static ObdobjeRange FromYear(int leto, ObdobjeTip tip) => new()
    {
        Tip = tip,
        Od = new DateTime(leto, 1, 1),
        Do = new DateTime(leto, 12, 31)
    };

    private static DateTime LastDayOfMonth(int leto, int mesec) =>
        new(leto, mesec, DateTime.DaysInMonth(leto, mesec));
}
