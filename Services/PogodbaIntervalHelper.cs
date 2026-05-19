namespace ObracunDb.Services;

public static class PogodbaIntervalHelper
{
    public static bool JeMesecVkljucen(string? meseci, int mesec)
    {
        if (string.IsNullOrWhiteSpace(meseci))
            return true;

        var mesecStr = mesec.ToString("D2");
        return meseci
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(m => m.Trim())
            .Any(m => m == mesecStr || (int.TryParse(m, out var parsed) && parsed == mesec));
    }

    public static string DobiInterval(string? meseci)
    {
        if (string.IsNullOrWhiteSpace(meseci))
            return "M";

        var steviloMesecev = meseci
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(m => m.Trim())
            .Where(m => !string.IsNullOrEmpty(m))
            .Distinct()
            .Count();

        return steviloMesecev switch
        {
            1 => "L",
            12 => "M",
            _ => "X"
        };
    }
}
