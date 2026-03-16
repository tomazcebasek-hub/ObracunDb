namespace ObracunDb.Data;

/// <summary>
/// Bralnik INI datotek (podpira sekcije [Section] in kljuèe Key=Value)
/// </summary>
public static class IniConfigReader
{
    /// <summary>
    /// Prebere INI datoteko in vrne slovar sekcij s kljuèi/vrednostmi
    /// </summary>
    public static Dictionary<string, Dictionary<string, string>> Read(string filePath)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var currentSection = "";

        foreach (var rawLine in File.ReadAllLines(filePath))
        {
            var line = rawLine.Trim();

            // Preskoèi prazne vrstice in komentarje
            if (string.IsNullOrEmpty(line) || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            // Sekcija [Database]
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                if (!result.ContainsKey(currentSection))
                    result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            // Kljuè=Vrednost
            var eqIndex = line.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = line[..eqIndex].Trim();
                var value = line[(eqIndex + 1)..].Trim();

                if (!result.ContainsKey(currentSection))
                    result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                result[currentSection][key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Prebere vrednost iz doloèene sekcije
    /// </summary>
    public static string GetValue(Dictionary<string, Dictionary<string, string>> config,
        string section, string key, string defaultValue = "")
    {
        if (config.TryGetValue(section, out var sectionData) &&
            sectionData.TryGetValue(key, out var value))
        {
            return value;
        }
        return defaultValue;
    }
}
