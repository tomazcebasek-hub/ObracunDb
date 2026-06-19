namespace ObracunDb.Data.Menus;

/// <summary>
/// Definicija ene postavke menija (z morebitnimi podmeniji).
/// Kljuc je stabilen identifikator, ki se uporablja za dovoljenja vidnosti.
/// </summary>
public class MenuItemDef
{
    public MenuItemDef(string kljuc, string naziv, string? url, string ikona, List<MenuItemDef>? otroci = null)
    {
        Kljuc = kljuc;
        Naziv = naziv;
        Url = url;
        Ikona = ikona;
        Otroci = otroci ?? new List<MenuItemDef>();
    }

    /// <summary>Stabilen kljuc menija (uporabljen za shranjevanje dovoljenj).</summary>
    public string Kljuc { get; }

    /// <summary>Prikazni naziv.</summary>
    public string Naziv { get; }

    /// <summary>Navigacijski URL; null za postavke, ki so zgolj vsebnik podmenijev.</summary>
    public string? Url { get; }

    /// <summary>CSS razred ikone.</summary>
    public string Ikona { get; }

    /// <summary>Podmeniji.</summary>
    public List<MenuItemDef> Otroci { get; }

    public bool ImaOtroke => Otroci.Count > 0;
}

/// <summary>
/// Centralna definicija drevesa menijev aplikacije.
/// Uporablja jo NavMenu (za prikaz) in forma "Dovoljenja do menujev".
/// Ce dodas nov meni, ga dodaj sem; privzeto ga ne vidi nihce, dokler ga
/// nekdo ne vklopi v formi "Dovoljenja do menujev".
/// </summary>
public static class MenuDefinition
{
    /// <summary>
    /// Korenske postavke menija v vrstnem redu prikaza.
    /// </summary>
    public static readonly IReadOnlyList<MenuItemDef> Vse = new List<MenuItemDef>
    {
        new("potrjevanje", "Potrjevanje nalogov", "/potrjevanje", "icon icon-calendar"),
        new("pregled-nalogov", "Pregled nalogov", "/pregled-nalogov", "icon icon-pivot-table"),
        new("obracun", "Obračun", null, "icon icon-scheduler", new()
        {
            new("izvedi-obracun", "Izvedi obračun", "/izvedi-obracun", "icon icon-scheduler"),
            new("spremembe-kolicin", "Spremembe količin", "/spremembe-kolicin", "icon icon-counter"),
            new("koriscenje-predracuni", "Koriščenje predračunov", "/koriscenje-predracuni", "icon icon-chart"),
            new("sestevek", "Seštevek", "/sestevek", "icon icon-counter"),
            new("sestevek-dela", "Seštevek dela", "/sestevek-dela", "icon icon-counter"),
            new("osnutki", "Osnutki", "/osnutki", "icon icon-pivot-table"),
            new("zapis-v-faw", "Zapis v FAW", "/zapis-v-faw", "icon icon-scheduler"),
            new("zakljucek", "Zaključek meseca", "/zakljucek", "icon icon-counter"),
        }),
        new("pregledi", "Pregledi", null, "icon icon-pivot-table", new()
        {
            new("pregled-ur", "Pregled ur serviserji", "/pregled-ur", "icon icon-counter"),
            new("predracuni", "Predračuni", "/predracuni", "icon icon-scheduler"),
        }),
        new("koriscenje-pogodb", "Koriščenje pogodb", "/koriscenje-pogodb", "icon icon-chart"),
        new("partnerji", "Partnerji", "/partnerji", "icon icon-tree-list"),
        new("paketi", "Paketi", "/paketi", "icon icon-counter"),
        new("partner-minute", "Partner minute", "/partner-minute", "icon icon-chart"),
        new("reklamacije", "Reklamacije", "/reklamacije", "icon icon-pivot-table"),
        new("nastavitve", "Nastavitve", "/nastavitve", "icon icon-settings"),
        new("loceni-racuni", "Ločeni računi", "/loceni-racuni", "icon icon-pivot-table"),
        new("parametri", "Barva ozadja", "/parametri", "icon icon-settings"),
        new("uporabniki", "Uporabniki", "/uporabniki", "icon icon-tree-list"),
        new("revizijska-sled", "Revizijska sled", "/revizijska-sled", "icon icon-pivot-table"),
    };

    /// <summary>
    /// Vrne vse postavke (vključno s podmeniji) v ravni (flat) obliki.
    /// </summary>
    public static IEnumerable<MenuItemDef> VseRavno()
    {
        foreach (var item in Vse)
        {
            yield return item;
            foreach (var child in item.Otroci)
                yield return child;
        }
    }

    /// <summary>
    /// Vrne ključe vseh postavk menija (vključno s podmeniji).
    /// </summary>
    public static IEnumerable<string> VsiKljuci() => VseRavno().Select(m => m.Kljuc);

    /// <summary>
    /// Vrne ključ nadrejenega (glavnega) menija za dani podmeni, ali null če je postavka korenska.
    /// </summary>
    public static string? NajdiStarsaKljuc(string kljuc)
    {
        foreach (var item in Vse)
        {
            if (item.Otroci.Any(c => string.Equals(c.Kljuc, kljuc, StringComparison.OrdinalIgnoreCase)))
                return item.Kljuc;
        }
        return null;
    }
}
