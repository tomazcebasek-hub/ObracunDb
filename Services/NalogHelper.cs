using LinqToDB;
using ObracunDb.Data;
using ObracunDb.Data.Entities;

namespace ObracunDb.Services;

/// <summary>
/// Pomožne funkcije za delo z nalogi.
/// </summary>
public static class NalogHelper
{
    /// <summary>
    /// Preveri, ali se za nalog obračuna kilometrina.
    /// Helpdesk nalogi (1000000-1999999) se ne obračunajo.
    /// </summary>
    public static bool SeObracunaKilometrina(string stevilkaNaloga)
    {
        if (int.TryParse(stevilkaNaloga, out var stevilka) && stevilka >= 1_000_000 && stevilka < 2_000_000)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Določi KajObracunam iz SIF28.
    /// </summary>
    public static KajObracunam DolocKajObracunam(int sif28)
    {
        return sif28 switch
        {
            0 => KajObracunam.KmMin,
            1 => KajObracunam.Nic,
            _ => KajObracunam.Nedefinirano
        };
    }

    /// <summary>
    /// Preveri in ustvari manjkajoče OBRACUN_DN zapise za vse naloge v seznamu.
    /// </summary>
    public static int UstvariManjkajoceObracunDn(ObracunLinqDb db, IEnumerable<FaDnNalog> nalogi)
    {
        var nalogiList = nalogi.ToList();
        var kljuci = nalogiList.Select(n => (n.Stevilka, n.Leto)).ToHashSet();
        var obstojeci = db.ObracunDn
            .ToList()
            .Where(o => kljuci.Contains((o.Stevilka, o.Leto)))
            .Select(o => (o.Stevilka, o.Leto))
            .ToHashSet();

        int ustvarjenih = 0;

        foreach (var nalog in nalogiList)
        {
            if (!obstojeci.Contains((nalog.Stevilka, nalog.Leto)))
            {
                var kajObracunam = DolocKajObracunam(nalog.Sif28);

                db.Insert(new ObracunDn
                {
                    Stevilka = nalog.Stevilka,
                    Leto = nalog.Leto,
                    KajObracunam = kajObracunam,
                    MinuteKiSeNeObracunajo = 0
                });

                ustvarjenih++;
            }
        }

        return ustvarjenih;
    }
}
