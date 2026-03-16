namespace ObracunDb.Data.Entities;

/// <summary>
/// Kaj se obraèunava za delovni nalog.
/// </summary>
public enum KajObracunam
{
    Nedefinirano = 0,
    KmMin = 1,
    Nic = 2,
    Km = 3,
    Min = 4,
    ObveznoZaracunaj = 5
}

public static class KajObracunamExtensions
{
    public static string ToText(this KajObracunam value) => value switch
    {
        KajObracunam.Nedefinirano => "Nedefinirano",
        KajObracunam.KmMin => "Km + min",
        KajObracunam.Nic => "Niè",
        KajObracunam.Km => "Samo km",
        KajObracunam.Min => "Samo min",
        KajObracunam.ObveznoZaracunaj => "Obvezno zaraèunaj",
        _ => value.ToString()
    };
}
