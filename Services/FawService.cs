using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LinqToDB;
using ObracunDb.Data;
using ObracunDb.Data.Entities;

namespace ObracunDb.Services;

public class FawCache
{
    public Dictionary<int, ObracunOsnutek> Osnutki { get; set; } = new();
    public Dictionary<int, List<ObracunOsnutekPos>> Postavke { get; set; } = new();
    public Dictionary<int, Partner> Partnerji { get; set; } = new();
    public Dictionary<int, List<FaPogodbe>> Pogodbe { get; set; } = new();
    public Dictionary<(int Stevilka, int Leto), List<FaPogodbePoz>> PostavkePogodb { get; set; } = new();
    public Dictionary<int, List<(string StevilkaNaloga, int LetoNaloga)>> NalogiPoPartnerju { get; set; } = new();
    public Dictionary<(string Stevilka, int Leto), DateTime> DatumiNalogov { get; set; } = new();
    public Dictionary<int, List<ObracunLoceniRacun>> LoceniRacuni { get; set; } = new();
    public Dictionary<(string Stevilka, int Leto), FaDnNalog> NalogiEntity { get; set; } = new();
    public Dictionary<int, List<ObracunOsnutekRacun>> OsnutekRacuni { get; set; } = new();
}

public class FawStanje
{
    public int Mesec { get; set; }
    public int Leto { get; set; }
    public int SkupajRacunov { get; set; }
    public int ZePrenesenih { get; set; }
    public int ZaPrenosOstane => SkupajRacunov - ZePrenesenih;
}

public class FawRacunRezultat
{
    public int Partner { get; set; }
    public bool Uspeh { get; set; }
    public string Sporocilo { get; set; } = "";
    public int? RacunStevilka { get; set; }
    public int? RacunLeto { get; set; }
}

public class FawService
{
    private readonly FirebirdConnectionManager _connectionManager;
    private readonly ParametriService _parametri;

    public FawService(FirebirdConnectionManager connectionManager, ParametriService parametri)
    {
        _connectionManager = connectionManager;
        _parametri = parametri;
    }

    private ObracunLinqDb CreateDb()
    {
        return ObracunLinqDb.Create(_connectionManager.ConnectionString);
    }

    public ObracunLinqDb CreateSharedDb()
    {
        return ObracunLinqDb.Create(_connectionManager.ConnectionString);
    }

    public FawStanje PridobiStanje(int mesec, int leto)
    {
        using var db = CreateDb();

        var vsiPartnerji = db.ObracunOsnutekPos
            .Where(p => p.Mesec == mesec && p.Leto == leto)
            .Select(p => p.Partner)
            .Distinct()
            .ToList();

        // Partnerji z RacunStevilka v osnutku (navadni)
        var zePreneseniOsnutek = db.ObracunOsnutek
            .Where(o => o.Mesec == mesec && o.Leto == leto && o.RacunStevilka != null && o.RacunLeto != null)
            .Select(o => o.Partner)
            .ToHashSet();

        // Ločeni partnerji — prenesen je šele, ko imajo VSI njegovi računi RacunStevilka
        var loceniRacuni = db.ObracunOsnutekRacun
            .Where(r => r.Mesec == mesec && r.Leto == leto)
            .ToList();
        var loceniPoPartnerju = loceniRacuni.GroupBy(r => r.Partner).ToDictionary(g => g.Key, g => g.ToList());
        var loceniPartnerji = loceniPoPartnerju.Keys.ToHashSet();

        int zePrenesenih = 0;
        foreach (var partner in vsiPartnerji)
        {
            if (loceniPartnerji.Contains(partner))
            {
                // Ločen partner: prenesen šele ko vsi računi imajo stevilko
                var racuni = loceniPoPartnerju[partner];
                if (racuni.All(r => r.RacunStevilka != null && r.RacunLeto != null))
                    zePrenesenih++;
            }
            else if (zePreneseniOsnutek.Contains(partner))
            {
                zePrenesenih++;
            }
        }

        return new FawStanje
        {
            Mesec = mesec,
            Leto = leto,
            SkupajRacunov = vsiPartnerji.Count,
            ZePrenesenih = zePrenesenih
        };
    }

    /// <summary>
    /// Ponastavi (razveljavi) prenos v FAW za izbrani mesec/leto: počisti RacunStevilka in RacunLeto
    /// na osnutkih in ločenih računih. Vrne število posodobljenih zapisov.
    /// </summary>
    public int PonastaviPrenos(int mesec, int leto)
    {
        using var db = CreateDb();

        var osnutki = db.ObracunOsnutek
            .Where(o => o.Mesec == mesec && o.Leto == leto && (o.RacunStevilka != null || o.RacunLeto != null))
            .Set(o => o.RacunStevilka, (int?)null)
            .Set(o => o.RacunLeto, (int?)null)
            .Update();

        var loceni = db.ObracunOsnutekRacun
            .Where(r => r.Mesec == mesec && r.Leto == leto && (r.RacunStevilka != null || r.RacunLeto != null))
            .Set(r => r.RacunStevilka, (int?)null)
            .Set(r => r.RacunLeto, (int?)null)
            .Update();

        return osnutki + loceni;
    }

    public List<int> PridobiPartnerjeZaPrenos(int mesec, int leto)
    {
        using var db = CreateDb();

        var vsiPartnerji = db.ObracunOsnutekPos
            .Where(p => p.Mesec == mesec && p.Leto == leto)
            .Select(p => p.Partner)
            .Distinct()
            .ToList();

        // Navadni partnerji (brez ločenih računov) — preneseni ko imajo RacunStevilka v osnutku
        var zePreneseniOsnutek = db.ObracunOsnutek
            .Where(o => o.Mesec == mesec && o.Leto == leto && o.RacunStevilka != null && o.RacunLeto != null)
            .Select(o => o.Partner)
            .ToHashSet();

        // Ločeni partnerji — preneseni šele ko VSI računi imajo RacunStevilka
        var loceniRacuni = db.ObracunOsnutekRacun
            .Where(r => r.Mesec == mesec && r.Leto == leto)
            .ToList();
        var loceniPoPartnerju = loceniRacuni.GroupBy(r => r.Partner).ToDictionary(g => g.Key, g => g.ToList());
        var loceniPartnerji = loceniPoPartnerju.Keys.ToHashSet();

        var zePreneseni = new HashSet<int>();
        foreach (var partner in vsiPartnerji)
        {
            if (loceniPartnerji.Contains(partner))
            {
                var racuni = loceniPoPartnerju[partner];
                if (racuni.All(r => r.RacunStevilka != null && r.RacunLeto != null))
                    zePreneseni.Add(partner);
            }
            else if (zePreneseniOsnutek.Contains(partner))
            {
                zePreneseni.Add(partner);
            }
        }

        return vsiPartnerji
            .Where(p => !zePreneseni.Contains(p))
            .OrderBy(p => p)
            .ToList();
    }

    public FawCache NaloziCache(int mesec, int leto)
    {
        using var db = CreateDb();
        var cache = new FawCache();

        var datumStoritveOd = new DateTime(leto, mesec, 1);
        var datumStoritveDo = datumStoritveOd.AddMonths(1).AddDays(-1);

        // Osnutki
        cache.Osnutki = db.ObracunOsnutek
            .Where(o => o.Mesec == mesec && o.Leto == leto)
            .ToList()
            .ToDictionary(o => o.Partner);

        // Postavke
        cache.Postavke = db.ObracunOsnutekPos
            .Where(p => p.Mesec == mesec && p.Leto == leto)
            .OrderBy(p => p.Partner).ThenBy(p => p.Zs)
            .ToList()
            .GroupBy(p => p.Partner)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Partnerji
        var partnerSifre = cache.Osnutki.Keys.ToList();
        cache.Partnerji = db.Partner
            .Where(p => partnerSifre.Contains(p.Sifra))
            .ToList()
            .ToDictionary(p => p.Sifra);

        // Pogodbe (aktivne + prihodnje za rok plačila)
        cache.Pogodbe = db.FaPogodbe
            .Where(p => partnerSifre.Contains(p.Partner)
                && (p.VeljaDo == null || p.VeljaDo >= datumStoritveOd))
            .OrderBy(p => p.Partner).ThenBy(p => p.Stevilka)
            .ToList()
            .GroupBy(p => p.Partner)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Postavke pogodb (za filtriranje po mesecih)
        var pogodbeKljuci = cache.Pogodbe.Values.SelectMany(l => l).Select(p => (p.Stevilka, p.Leto)).ToHashSet();
        cache.PostavkePogodb = db.FaPogodbePoz.ToList()
            .Where(pz => pogodbeKljuci.Contains((pz.Stevilka, pz.Leto)))
            .GroupBy(pz => (pz.Stevilka, pz.Leto))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Nalogi (obračunani)
        var vsiNalogi = db.ObracunOsnutekNalogObracun
            .Where(n => n.Mesec == mesec && n.Leto == leto && n.Obracunam == 1)
            .Select(n => new { n.Partner, n.StevilkaNaloga, n.LetoNaloga })
            .Distinct()
            .ToList();

        cache.NalogiPoPartnerju = vsiNalogi
            .GroupBy(n => n.Partner)
            .ToDictionary(g => g.Key, g => g.Select(n => (n.StevilkaNaloga, n.LetoNaloga)).ToList());

        // Datumi nalogov
        var stevilkeNalogov = vsiNalogi.Select(n => n.StevilkaNaloga).Distinct().ToList();
        var letaNalogov = vsiNalogi.Select(n => n.LetoNaloga).Distinct().ToList();
        if (stevilkeNalogov.Count > 0)
        {
            var nalogiEntities = db.FaDnNalog
                .Where(n => stevilkeNalogov.Contains(n.Stevilka) && letaNalogov.Contains(n.Leto))
                .ToList();

            cache.DatumiNalogov = nalogiEntities
                .ToDictionary(n => (n.Stevilka, n.Leto), n => n.Datum);

            cache.NalogiEntity = nalogiEntities
                .ToDictionary(n => (n.Stevilka, n.Leto));
        }

        // Ločeni računi
        cache.LoceniRacuni = db.ObracunLoceniRacun.ToList()
            .GroupBy(lr => lr.Partner)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Obstoječi zapisani računi za ločene partnerje
        cache.OsnutekRacuni = db.ObracunOsnutekRacun
            .Where(r => r.Mesec == mesec && r.Leto == leto)
            .ToList()
            .GroupBy(r => r.Partner)
            .ToDictionary(g => g.Key, g => g.ToList());

        return cache;
    }

    public List<string> NajdiManjkajoceArtikle(FawCache cache, IEnumerable<int> partnerji)
    {
        var sifre = partnerji
            .Distinct()
            .SelectMany(partner => cache.Postavke.TryGetValue(partner, out var postavke)
                ? postavke
                : Enumerable.Empty<ObracunOsnutekPos>())
            .Select(p => p.Artikel?.Trim())
            .Where(sifra => !string.IsNullOrWhiteSpace(sifra) && sifra != "-")
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sifre.Count == 0)
            return new();

        using var db = CreateDb();
        var obstojeceSifre = db.FaArtikel
            .Where(a => sifre.Contains(a.Sifra))
            .Select(a => a.Sifra)
            .ToList()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return sifre
            .Where(sifra => !obstojeceSifre.Contains(sifra))
            .OrderBy(sifra => sifra)
            .ToList();
    }

    public async Task<(bool Uspeh, string? Token, string Sporocilo)> Avtenticiraj(
        HttpClient httpClient, string apiUrl, string uporabnik, string geslo, string davcna)
    {
        var baseUrl = apiUrl.TrimEnd('/');
        var authUrl = baseUrl.EndsWith("/Avtentikacija", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : baseUrl + "/Avtentikacija";

        var authRequestBody = new
        {
            username = uporabnik,
            password = geslo,
            taxNumber = davcna,
            year = 0,
            returnPastYears = true,
            @params = new { }
        };

        var authJson = JsonSerializer.Serialize(authRequestBody);
        var authContent = new StringContent(authJson, Encoding.UTF8, "application/json");

        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var authResponse = await httpClient.PostAsync(authUrl, authContent);
        var authResponseContent = await authResponse.Content.ReadAsStringAsync();

        if (!authResponse.IsSuccessStatusCode)
            return (false, null, $"Avtentikacija ni uspela! Status: {authResponse.StatusCode}. Odgovor: {authResponseContent}");

        string? token = null;
        try
        {
            var authJsonDoc = JsonDocument.Parse(authResponseContent);
            string[] tokenNames = { "apiKey", "ApiKey", "token", "Token", "access_token", "accessToken", "jwt", "JWT", "jwtToken", "authToken" };
            foreach (var name in tokenNames)
            {
                if (authJsonDoc.RootElement.TryGetProperty(name, out var tokenElement) && tokenElement.ValueKind == JsonValueKind.String)
                {
                    token = tokenElement.GetString();
                    if (!string.IsNullOrWhiteSpace(token))
                        break;
                }
            }
        }
        catch { }

        if (string.IsNullOrWhiteSpace(token))
            return (false, null, $"Token ni bil najden v odgovoru! Odgovor: {authResponseContent}");

        return (true, token, "Avtentikacija uspešna.");
    }

    public async Task<FawRacunRezultat> ZapisiRacun(
        HttpClient httpClient, string apiUrl, string token,
        int mesec, int leto, int partner,
        DateTime datumRacuna, string komercialist,
        FawCache cache, ObracunLinqDb db,
        Action<string>? log = null)
    {
        var rezultat = new FawRacunRezultat { Partner = partner };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            log?.Invoke($"[{sw.ElapsedMilliseconds}ms] Začetek priprave za partnerja {partner}");

            if (!cache.Osnutki.TryGetValue(partner, out var osnutek))
            {
                rezultat.Sporocilo = "Osnutek za tega partnerja ne obstaja.";
                return rezultat;
            }

            if (!cache.Postavke.TryGetValue(partner, out var postavke) || postavke.Count == 0)
            {
                rezultat.Sporocilo = "Ni postavk za tega partnerja.";
                return rezultat;
            }

            // Ali je ločen partner?
            var jeLocen = cache.LoceniRacuni.TryGetValue(partner, out var loceniZapisi) && loceniZapisi.Count > 0;

            if (jeLocen)
            {
                // Preveri ali so vsi računi že zapisani
                if (cache.OsnutekRacuni.TryGetValue(partner, out var obstojeciRacuni) && obstojeciRacuni.Count > 0)
                {
                    if (obstojeciRacuni.All(r => r.RacunStevilka != null && r.RacunLeto != null))
                    {
                        var racuniStr = string.Join(", ", obstojeciRacuni.Select(r => $"{r.RacunStevilka}/{r.RacunLeto}"));
                        rezultat.Uspeh = true;
                        rezultat.RacunStevilka = obstojeciRacuni.First().RacunStevilka;
                        rezultat.RacunLeto = obstojeciRacuni.First().RacunLeto;
                        rezultat.Sporocilo = $"Ločeni računi že zapisani: {racuniStr}";
                        return rezultat;
                    }
                }

                return await ZapisiLoceneRacune(httpClient, apiUrl, token, mesec, leto, partner,
                    datumRacuna, komercialist, cache, db, osnutek, postavke, loceniZapisi!, log, sw);
            }
            else
            {
                // Navadna logika: en račun
                if (osnutek.RacunStevilka != null && osnutek.RacunLeto != null)
                {
                    rezultat.Uspeh = true;
                    rezultat.RacunStevilka = osnutek.RacunStevilka;
                    rezultat.RacunLeto = osnutek.RacunLeto;
                    rezultat.Sporocilo = $"Račun že zapisan: {osnutek.RacunStevilka}/{osnutek.RacunLeto}";
                    return rezultat;
                }

                return await ZapisiNavadniRacun(httpClient, apiUrl, token, mesec, leto, partner,
                    datumRacuna, komercialist, cache, db, osnutek, postavke, log, sw);
            }
        }
        catch (Exception ex)
        {
            rezultat.Sporocilo = $"Napaka: {ex.Message}";
            return rezultat;
        }
    }

    private async Task<FawRacunRezultat> ZapisiNavadniRacun(
        HttpClient httpClient, string apiUrl, string token,
        int mesec, int leto, int partner,
        DateTime datumRacuna, string komercialist,
        FawCache cache, ObracunLinqDb db,
        ObracunOsnutek osnutek, List<ObracunOsnutekPos> postavke,
        Action<string>? log, System.Diagnostics.Stopwatch sw)
    {
        var rezultat = new FawRacunRezultat { Partner = partner };

        cache.Partnerji.TryGetValue(partner, out var partnerData);
        var rokPlacilaPartner = (partnerData?.RokPlacila ?? 0) > 0 ? partnerData!.RokPlacila!.Value : 8;
        var datumStoritveOd = new DateTime(leto, mesec, 1);
        var jeLetnaPogodba = osnutek.LetnaPogodba == 1;
        var datumStoritveDo = jeLetnaPogodba
            ? datumStoritveOd.AddYears(1).AddDays(-1)
            : datumStoritveOd.AddMonths(1).AddDays(-1);

        // Pogodbe iz cache
        cache.Pogodbe.TryGetValue(partner, out var pogodbe);
        pogodbe ??= new();

        // Rok plačila: če ima partner veljavno pogodbo, vzemi najdaljši rok iz pogodb
        var rokPlacila = rokPlacilaPartner;
        var pogodbeZRokom = pogodbe.Where(p => (p.RokPlacila ?? 0) > 0).ToList();
        if (pogodbeZRokom.Count > 0)
        {
            var maxRokPogodba = pogodbeZRokom.Max(p => p.RokPlacila!.Value);
            rokPlacila = maxRokPogodba;
        }
        var datumValute = datumRacuna.AddDays(rokPlacila);

        // Sestavi besedilo
        var vrstice = new List<string>();
        var mesecStr = mesec.ToString("D2");
        var pogodbeZaMesec = FiltrirajPogodbeZaMesec(pogodbe, mesecStr, cache);
        if (pogodbeZaMesec.Count > 0)
        {
            var stevilke = pogodbeZaMesec.Select(p =>
                !string.IsNullOrWhiteSpace(p.StPogodbe) ? p.StPogodbe.Trim() : $"{p.Stevilka}/{p.Leto}").ToList();
            var prefix = pogodbeZaMesec.Count switch
            {
                1 => "Številka pogodbe",
                2 => "Številki pogodb",
                _ => "Številke pogodb"
            };
            vrstice.Add($"{prefix}: {string.Join(", ", stevilke)}");
        }

        // Nalogi iz cache
        cache.NalogiPoPartnerju.TryGetValue(partner, out var stevilkeNalogov);
        stevilkeNalogov ??= new();
        if (stevilkeNalogov.Count > 0)
            vrstice.Add(SestaviBesediloNalogov(stevilkeNalogov, cache));

        var besedilo = string.Join("\r\n", vrstice);

        log?.Invoke($"[{sw.ElapsedMilliseconds}ms] Pripravljam JSON");
        var sifraKilometrina = _parametri.GetString(ObracunParam.SifraKilometrina) ?? "";
        var apiPostavke = SestaviApiPostavke(postavke, sifraKilometrina);

        var fakturaData = new
        {
            dodatnaPolja = new[]
            {
                new { naziv = "BESEDILO", vrednost = besedilo }
            },
            datum = datumRacuna.ToString("yyyy-MM-dd"),
            datumValute = datumValute.ToString("yyyy-MM-dd"),
            partner = partner,
            komercialist = komercialist,
            datumStoritveOd = datumStoritveOd.ToString("yyyy-MM-dd"),
            datumStoritveDo = datumStoritveDo.ToString("yyyy-MM-dd"),
            postavke = apiPostavke
        };

        var (uspeh, racunStevilka, racunLeto, sporocilo) = await PosljiNaApi(httpClient, apiUrl, token, fakturaData, log, sw);
        if (!uspeh)
        {
            LogNapakoApi(log, partner, sporocilo, postavke, sifraKilometrina);
            rezultat.Sporocilo = sporocilo;
            return rezultat;
        }

        log?.Invoke($"[{sw.ElapsedMilliseconds}ms] Posodabljam osnutek v bazi");
        db.ObracunOsnutek
            .Where(o => o.Mesec == mesec && o.Leto == leto && o.Partner == partner)
            .Set(o => o.RacunStevilka, racunStevilka)
            .Set(o => o.RacunLeto, racunLeto)
            .Update();

        log?.Invoke($"[{sw.ElapsedMilliseconds}ms] Končano");

        rezultat.Uspeh = true;
        rezultat.RacunStevilka = racunStevilka;
        rezultat.RacunLeto = racunLeto;
        rezultat.Sporocilo = racunStevilka != null
            ? $"Račun uspešno zapisan: {racunStevilka}/{racunLeto}"
            : "Račun uspešno poslan, ni vrnil številke.";

        return rezultat;
    }

    private async Task<FawRacunRezultat> ZapisiLoceneRacune(
        HttpClient httpClient, string apiUrl, string token,
        int mesec, int leto, int partner,
        DateTime datumRacuna, string komercialist,
        FawCache cache, ObracunLinqDb db,
        ObracunOsnutek osnutek, List<ObracunOsnutekPos> postavke,
        List<ObracunLoceniRacun> loceniZapisi,
        Action<string>? log, System.Diagnostics.Stopwatch sw)
    {
        var rezultat = new FawRacunRezultat { Partner = partner };
        var sifraKilometrina = _parametri.GetString(ObracunParam.SifraKilometrina) ?? "";

        cache.Partnerji.TryGetValue(partner, out var partnerData);
        var rokPlacilaPartner = (partnerData?.RokPlacila ?? 0) > 0 ? partnerData!.RokPlacila!.Value : 8;
        var datumStoritveOd = new DateTime(leto, mesec, 1);
        var jeLetnaPogodba = osnutek.LetnaPogodba == 1;
        var datumStoritveDo = jeLetnaPogodba
            ? datumStoritveOd.AddYears(1).AddDays(-1)
            : datumStoritveOd.AddMonths(1).AddDays(-1);

        cache.Pogodbe.TryGetValue(partner, out var pogodbe);
        pogodbe ??= new();

        // Pogodba → prodajalna mapping
        var pogodbaProdajalna = loceniZapisi.ToDictionary(
            lr => (lr.PogodbaStevilka, lr.PogodbaLeto), lr => lr.Prodajalna);

        // Prodajalna → pogodba mapping (za razporeditev po prodajalni)
        var prodajalnaPogodba = loceniZapisi.ToDictionary(
            lr => lr.Prodajalna, lr => (lr.PogodbaStevilka, lr.PogodbaLeto));

        var prodajalne = loceniZapisi.Select(lr => lr.Prodajalna).Distinct().ToList();

        // Nalogi iz cache
        cache.NalogiPoPartnerju.TryGetValue(partner, out var stevilkeNalogov);
        stevilkeNalogov ??= new();

        // Razdeli postavke po pogodbah/prodajalnah
        var postavkeZaPogodbo = new Dictionary<(int Stevilka, int Leto), List<ObracunOsnutekPos>>();
        foreach (var lr in loceniZapisi)
            postavkeZaPogodbo[(lr.PogodbaStevilka, lr.PogodbaLeto)] = new();

        // Izračunaj znesek vsake pogodbe (za proporcionalno razdelitev)
        var zneskiPogodb = new Dictionary<(int Stevilka, int Leto), decimal>();
        foreach (var p in postavke.Where(p => p.TipPostavke == TipPostavke.POGODBA && p.PogodbaStevilka.HasValue && p.PogodbaLeto.HasValue))
        {
            var key = (p.PogodbaStevilka!.Value, p.PogodbaLeto!.Value);
            if (!zneskiPogodb.ContainsKey(key))
                zneskiPogodb[key] = 0;
            zneskiPogodb[key] += (p.Kolicina ?? 0) * (p.Cena ?? 0) * (1 - (p.Rabat ?? 0) / 100);
        }

        var skupniZnesek = zneskiPogodb.Values.Sum();

        foreach (var pos in postavke)
        {
            if (pos.TipPostavke == TipPostavke.POGODBA && pos.PogodbaStevilka.HasValue && pos.PogodbaLeto.HasValue)
            {
                // POGODBA → na svojo pogodbo
                var key = (pos.PogodbaStevilka.Value, pos.PogodbaLeto.Value);
                if (postavkeZaPogodbo.ContainsKey(key))
                    postavkeZaPogodbo[key].Add(pos);
                else
                    postavkeZaPogodbo.Values.First().Add(pos); // fallback
            }
            else if (!string.IsNullOrWhiteSpace(pos.NalogStevilka) && pos.NalogLeto.HasValue)
            {
                // NALOG ali ROCNI z NalogStevilka → na prodajalno naloga
                var nalogKey = (pos.NalogStevilka, pos.NalogLeto.Value);
                if (cache.NalogiEntity.TryGetValue(nalogKey, out var nalog) && prodajalnaPogodba.TryGetValue(nalog.Prodajalna, out var pog))
                {
                    postavkeZaPogodbo[pog].Add(pos);
                }
                else
                {
                    // Prodajalna naloga ni med ločenimi → daj na prvo pogodbo
                    postavkeZaPogodbo.Values.First().Add(pos);
                }
            }
            else
            {
                // Servisne storitve (NALOG brez NalogStevilka) → proporcionalno ali na edino prodajalno
                if (prodajalne.Count == 1)
                {
                    postavkeZaPogodbo.Values.First().Add(pos);
                }
                else if (skupniZnesek > 0)
                {
                    // Proporcionalno po znesku pogodb
                    var originalKolicina = pos.Kolicina ?? 0;
                    bool first = true;
                    decimal dodeljeno = 0;
                    foreach (var kvp in postavkeZaPogodbo)
                    {
                        zneskiPogodb.TryGetValue(kvp.Key, out var znesekPogodbe);
                        var delez = znesekPogodbe / skupniZnesek;
                        var kolicinaDel = Math.Round(originalKolicina * delez, 2);

                        if (first)
                        {
                            // Na zadnjo damo ostanek
                            first = false;
                            continue;
                        }

                        dodeljeno += kolicinaDel;
                        kvp.Value.Add(new ObracunOsnutekPos
                        {
                            Mesec = pos.Mesec, Leto = pos.Leto, Partner = pos.Partner, Zs = pos.Zs,
                            Artikel = pos.Artikel, Naziv = pos.Naziv, Kolicina = kolicinaDel,
                            Cena = pos.Cena, Rabat = pos.Rabat, TipPostavke = pos.TipPostavke,
                            NalogStevilka = pos.NalogStevilka, NalogLeto = pos.NalogLeto
                        });
                    }
                    // Prva pogodba dobi ostanek
                    var ostanek = originalKolicina - dodeljeno;
                    if (ostanek != 0)
                    {
                        var prvaKey = postavkeZaPogodbo.Keys.First();
                        postavkeZaPogodbo[prvaKey].Add(new ObracunOsnutekPos
                        {
                            Mesec = pos.Mesec, Leto = pos.Leto, Partner = pos.Partner, Zs = pos.Zs,
                            Artikel = pos.Artikel, Naziv = pos.Naziv, Kolicina = ostanek,
                            Cena = pos.Cena, Rabat = pos.Rabat, TipPostavke = pos.TipPostavke,
                            NalogStevilka = pos.NalogStevilka, NalogLeto = pos.NalogLeto
                        });
                    }
                }
                else
                {
                    // Ni znanih zneskov pogodb → na prvo
                    postavkeZaPogodbo.Values.First().Add(pos);
                }
            }
        }

        // Obstoječi racuni (za preskakovanje že zapisanih)
        cache.OsnutekRacuni.TryGetValue(partner, out var obstojeciRacuni);
        obstojeciRacuni ??= new();
        var zeZapisani = obstojeciRacuni
            .Where(r => r.RacunStevilka != null && r.RacunLeto != null)
            .Select(r => (r.PogodbaStevilka, r.PogodbaLeto))
            .ToHashSet();

        // Zapiši račun za vsako pogodbo/prodajalno
        var mesecStr = mesec.ToString("D2");
        var stRacunov = postavkeZaPogodbo.Count;
        int zap = 0;
        var zapisaniRacuni = new List<string>();

        foreach (var (pogodbaKey, pogodbaPostavke) in postavkeZaPogodbo)
        {
            zap++;

            // Preskoči že zapisane
            if (zeZapisani.Contains(pogodbaKey))
            {
                var obs = obstojeciRacuni.First(r => r.PogodbaStevilka == pogodbaKey.Stevilka && r.PogodbaLeto == pogodbaKey.Leto);
                zapisaniRacuni.Add($"{obs.RacunStevilka}/{obs.RacunLeto}");
                log?.Invoke($"[{sw.ElapsedMilliseconds}ms] Račun {zap}/{stRacunov} (pogodba {pogodbaKey.Stevilka}/{pogodbaKey.Leto}) že zapisan: {obs.RacunStevilka}/{obs.RacunLeto}");
                continue;
            }

            if (pogodbaPostavke.Count == 0)
            {
                log?.Invoke($"[{sw.ElapsedMilliseconds}ms] Račun {zap}/{stRacunov} (pogodba {pogodbaKey.Stevilka}/{pogodbaKey.Leto}) nima postavk, preskočeno");
                continue;
            }

            // Pogodba za ta račun
            var pogodba = pogodbe.FirstOrDefault(p => p.Stevilka == pogodbaKey.Stevilka && p.Leto == pogodbaKey.Leto);
            pogodbaProdajalna.TryGetValue(pogodbaKey, out var prodajalna);

            // Rok plačila iz pogodbe (fallback na partner)
            var rokPlacila = (pogodba?.RokPlacila ?? 0) > 0 ? pogodba!.RokPlacila!.Value : rokPlacilaPartner;
            var datumValute = datumRacuna.AddDays(rokPlacila);

            // Besedilo: samo ta pogodba
            var vrstice = new List<string>();
            if (pogodba != null)
            {
                var stPogodbe = !string.IsNullOrWhiteSpace(pogodba.StPogodbe) ? pogodba.StPogodbe.Trim() : $"{pogodba.Stevilka}/{pogodba.Leto}";
                vrstice.Add($"Številka pogodbe: {stPogodbe}");
            }

            // Nalogi ki spadajo na to prodajalno
            var nalogiZaToProdajalno = stevilkeNalogov.Where(n =>
            {
                if (cache.NalogiEntity.TryGetValue((n.StevilkaNaloga, n.LetoNaloga), out var nalog))
                    return nalog.Prodajalna == prodajalna;
                return false;
            }).ToList();

            if (nalogiZaToProdajalno.Count > 0)
                vrstice.Add(SestaviBesediloNalogov(nalogiZaToProdajalno, cache));

            var besedilo = string.Join("\r\n", vrstice);

            log?.Invoke($"[{sw.ElapsedMilliseconds}ms] Ločen račun {zap}/{stRacunov} (pogodba {pogodbaKey.Stevilka}/{pogodbaKey.Leto}, prodajalna {prodajalna}): {pogodbaPostavke.Count} postavk");

            var apiPostavke = SestaviApiPostavke(pogodbaPostavke, sifraKilometrina);

            var fakturaData = new
            {
                dodatnaPolja = new[]
                {
                    new { naziv = "BESEDILO", vrednost = besedilo }
                },
                datum = datumRacuna.ToString("yyyy-MM-dd"),
                datumValute = datumValute.ToString("yyyy-MM-dd"),
                partner = partner,
                komercialist = komercialist,
                datumStoritveOd = datumStoritveOd.ToString("yyyy-MM-dd"),
                datumStoritveDo = datumStoritveDo.ToString("yyyy-MM-dd"),
                prodajalna = prodajalna,
                postavke = apiPostavke
            };

            var (uspeh, racunStevilka, racunLeto, sporocilo) = await PosljiNaApi(httpClient, apiUrl, token, fakturaData, log, sw);
            if (!uspeh)
            {
                LogNapakoApi(log, partner, sporocilo, pogodbaPostavke, sifraKilometrina,
                    $"ločen račun {zap}/{stRacunov} (pogodba {pogodbaKey.Stevilka}/{pogodbaKey.Leto}, prodajalna {prodajalna})");
                rezultat.Sporocilo = $"Napaka pri ločenem računu {zap}/{stRacunov} (pogodba {pogodbaKey.Stevilka}/{pogodbaKey.Leto}): {sporocilo}";
                return rezultat;
            }

            // Zapiši v OBRACUN_OSNUTEK_RACUN
            log?.Invoke($"[{sw.ElapsedMilliseconds}ms] Zapisujem v OBRACUN_OSNUTEK_RACUN");
            var obstaja = obstojeciRacuni.FirstOrDefault(r => r.PogodbaStevilka == pogodbaKey.Stevilka && r.PogodbaLeto == pogodbaKey.Leto);
            if (obstaja != null)
            {
                db.ObracunOsnutekRacun
                    .Where(r => r.Mesec == mesec && r.Leto == leto && r.Partner == partner
                        && r.PogodbaStevilka == pogodbaKey.Stevilka && r.PogodbaLeto == pogodbaKey.Leto)
                    .Set(r => r.RacunStevilka, racunStevilka)
                    .Set(r => r.RacunLeto, racunLeto)
                    .Update();
            }
            else
            {
                db.Insert(new ObracunOsnutekRacun
                {
                    Mesec = mesec,
                    Leto = leto,
                    Partner = partner,
                    PogodbaStevilka = pogodbaKey.Stevilka,
                    PogodbaLeto = pogodbaKey.Leto,
                    Prodajalna = prodajalna,
                    RacunStevilka = racunStevilka,
                    RacunLeto = racunLeto
                });
            }

            zapisaniRacuni.Add($"{racunStevilka}/{racunLeto}");
        }

        // Ko so vsi zapisani, označi partnerja v osnutku
        var prviRacun = zapisaniRacuni.FirstOrDefault();
        if (zapisaniRacuni.Count > 0)
        {
            // Razberi prvega za shranitev v osnutek
            var parts = prviRacun?.Split('/');
            int? prviSt = null, prvoLeto = null;
            if (parts?.Length == 2)
            {
                int.TryParse(parts[0], out var s);
                int.TryParse(parts[1], out var l);
                prviSt = s;
                prvoLeto = l;
            }

            db.ObracunOsnutek
                .Where(o => o.Mesec == mesec && o.Leto == leto && o.Partner == partner)
                .Set(o => o.RacunStevilka, prviSt)
                .Set(o => o.RacunLeto, prvoLeto)
                .Update();

            rezultat.Uspeh = true;
            rezultat.RacunStevilka = prviSt;
            rezultat.RacunLeto = prvoLeto;
            rezultat.Sporocilo = $"Ločeni računi ({zapisaniRacuni.Count}): {string.Join(", ", zapisaniRacuni)}";
        }
        else
        {
            rezultat.Sporocilo = "Ni bilo postavk za zapis ločenih računov.";
        }

        log?.Invoke($"[{sw.ElapsedMilliseconds}ms] Končano ({zapisaniRacuni.Count} ločenih računov)");
        return rezultat;
    }

    private static List<FaPogodbe> FiltrirajPogodbeZaMesec(List<FaPogodbe> pogodbe, string mesecStr, FawCache cache)
    {
        return pogodbe.Where(p =>
        {
            cache.PostavkePogodb.TryGetValue((p.Stevilka, p.Leto), out var pozicije);
            if (pozicije == null || pozicije.Count == 0) return true;
            return pozicije.Any(pz =>
                string.IsNullOrWhiteSpace(pz.Meseci) ||
                pz.Meseci.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim()).Contains(mesecStr));
        }).ToList();
    }

    private static string SestaviBesediloNalogov(List<(string StevilkaNaloga, int LetoNaloga)> nalogi, FawCache cache)
    {
        var nalogiSortirani = nalogi
            .OrderBy(n => cache.DatumiNalogov.TryGetValue((n.StevilkaNaloga, n.LetoNaloga), out var d) ? d : DateTime.MaxValue)
            .ThenBy(n => n.StevilkaNaloga)
            .ToList();

        var prefix = nalogiSortirani.Count switch
        {
            1 => "Delovni nalog",
            2 => "Delovna naloga",
            _ => "Delovni nalogi"
        };

        var nalogItems = nalogiSortirani.Select(n =>
        {
            var datumStr = cache.DatumiNalogov.TryGetValue((n.StevilkaNaloga, n.LetoNaloga), out var d) ? d.ToString("dd.MM.yyyy") : "?";
            return $"{n.StevilkaNaloga} z dne {datumStr}";
        }).ToList();

        var padLen = prefix.Length + 2;
        var pad = new string(' ', padLen + 8);
        var sb = new System.Text.StringBuilder();
        sb.Append($"{prefix}: ");
        for (int i = 0; i < nalogItems.Count; i++)
        {
            if (i > 0 && i % 3 == 0)
            {
                sb.Append("\r\n");
                sb.Append(pad);
            }
            else if (i > 0)
            {
                sb.Append(", ");
            }
            sb.Append(nalogItems[i]);
        }
        return sb.ToString();
    }

    private static void LogNapakoApi(Action<string>? log, int partner, string sporocilo,
        List<ObracunOsnutekPos> postavke, string sifraKilometrina, string? kontekst = null)
    {
        if (log == null)
            return;

        var kontekstStr = string.IsNullOrEmpty(kontekst) ? "" : $" [{kontekst}]";
        log.Invoke("============================================");
        log.Invoke($"!!! NAPAKA PRI ZAPISU V FAW API — partner {partner}{kontekstStr}");
        log.Invoke($"!!! Sporočilo: {sporocilo}");
        log.Invoke($"!!! Postavke računa ({postavke.Count}):");
        foreach (var p in postavke)
        {
            var jeKilometrina = p.Artikel == sifraKilometrina ? " [kilometrina]" : "";
            log.Invoke($"    Zs={p.Zs}, Artikel='{p.Artikel}', Naziv='{p.Naziv}', Kolicina={p.Kolicina}, Cena={p.Cena}, Rabat={p.Rabat}, Tip={p.TipPostavke}{jeKilometrina}");
        }
        log.Invoke("============================================");
    }

    private static object[] SestaviApiPostavke(List<ObracunOsnutekPos> postavke, string sifraKilometrina)
    {
        return postavke.Select(p => new
        {
            sifra = p.Artikel ?? "",
            naziv = p.Artikel == "-" || p.Artikel == sifraKilometrina ? (p.Naziv ?? "") : (string?)null,
            kolicina = p.Kolicina ?? 0,
            prodajnaCena = p.Cena ?? 0,
            rabat1 = p.Rabat ?? 0,
            stopnjaDdv = 0
        }).ToArray();
    }

    private async Task<(bool Uspeh, int? RacunStevilka, int? RacunLeto, string Sporocilo)> PosljiNaApi(
        HttpClient httpClient, string apiUrl, string token, object fakturaData,
        Action<string>? log, System.Diagnostics.Stopwatch sw)
    {
        var fakturaJson = JsonSerializer.Serialize(fakturaData, new JsonSerializerOptions { WriteIndented = true });

        var baseUrl = apiUrl.TrimEnd('/');
        if (baseUrl.EndsWith("/Avtentikacija", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^"/Avtentikacija".Length];

        var fakturaUrl = baseUrl.Contains("/api/v1", StringComparison.OrdinalIgnoreCase)
            ? $"{baseUrl}/FA/racun"
            : $"{baseUrl}/api/v1/FA/racun";

        log?.Invoke($"[{sw.ElapsedMilliseconds}ms] POST {fakturaUrl} PAYLOAD: {fakturaJson}");

        var fakturaContent = new StringContent(fakturaJson, Encoding.UTF8, "application/json");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var fakturaResponse = await httpClient.PostAsync(fakturaUrl, fakturaContent);
        var fakturaResponseContent = await fakturaResponse.Content.ReadAsStringAsync();

        log?.Invoke($"[{sw.ElapsedMilliseconds}ms] RESPONSE ({(int)fakturaResponse.StatusCode}): {fakturaResponseContent}");

        if (!fakturaResponse.IsSuccessStatusCode)
            return (false, null, null, $"Napaka API ({(int)fakturaResponse.StatusCode}): {fakturaResponseContent}");

        int? racunStevilka = null;
        int? racunLeto = null;
        try
        {
            var responseDoc = JsonDocument.Parse(fakturaResponseContent);
            if (responseDoc.RootElement.TryGetProperty("stevilka", out var stEl))
                racunStevilka = stEl.GetInt32();
            if (responseDoc.RootElement.TryGetProperty("leto", out var ltEl))
                racunLeto = ltEl.GetInt32();
        }
        catch { }

        return (true, racunStevilka, racunLeto, "OK");
    }

    public async Task<(bool Uspeh, int? IdDokumenta, string Sporocilo)> ZapisiDokumentAsync(
        HttpClient httpClient, string apiUrl, string token, string nazivDatoteke, byte[] vsebina)
    {
        var baseUrl = apiUrl.TrimEnd('/');
        if (baseUrl.EndsWith("/Avtentikacija", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^"/Avtentikacija".Length];

        var dokumentUrl = baseUrl.Contains("/api/v1", StringComparison.OrdinalIgnoreCase)
            ? $"{baseUrl}/Priloge/dokument"
            : $"{baseUrl}/api/v1/Priloge/dokument";

        var dokument = new
        {
            zaporedna = 1,
            tmpFlag = string.Empty,
            datumMape = string.Empty,
            rZaporedje = true,
            shranjen = true,
            vezaMape = "FA",
            sifraMape = 0,
            dokumentVsebina = new[]
            {
                new
                {
                    stran = 0,
                    nazivDatoteke,
                    vsebina = Convert.ToBase64String(vsebina),
                    izvornaDatoteka = string.Empty,
                    externalId = string.Empty,
                    crtknaKoda = string.Empty,
                    eRacunTip = 0,
                    eRacunSlog = 0
                }
            }
        };

        var json = JsonSerializer.Serialize(dokument);
        var requestSummary = $"POST {dokumentUrl}{Environment.NewLine}Payload: vezaMape=FA, sifraMape=0, zaporedna=1, rZaporedje=true, shranjen=true, nazivDatoteke={nazivDatoteke}, vsebina=Base64 ({vsebina.Length} B).";

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(dokumentUrl, content);
        var responseContent = await response.Content.ReadAsStringAsync();
        var summary = $"{requestSummary}{Environment.NewLine}Odgovor: {(int)response.StatusCode} {response.ReasonPhrase}; {responseContent}";

        if (!response.IsSuccessStatusCode)
            return (false, null, summary);

        int? idDokumenta = null;
        try
        {
            var responseJson = JsonDocument.Parse(responseContent);
            foreach (var propertyName in new[] { "id", "Id", "iD_DOK", "idDok" })
            {
                if (responseJson.RootElement.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var id))
                {
                    idDokumenta = id;
                    break;
                }
            }
        }
        catch { }

        return idDokumenta.HasValue
            ? (true, idDokumenta, $"{summary}{Environment.NewLine}ID dokumenta: {idDokumenta.Value}")
            : (false, null, $"{summary}{Environment.NewLine}Napaka: ID dokumenta ni bil najden v odgovoru API-ja.");
    }

    public async Task<(bool Uspeh, string Sporocilo)> PoveziDokumentAsync(
        HttpClient httpClient, string apiUrl, string token, int idDokumenta, int stevilka, int leto)
    {
        var baseUrl = apiUrl.TrimEnd('/');
        if (baseUrl.EndsWith("/Avtentikacija", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^"/Avtentikacija".Length];

        var povezavaUrl = baseUrl.Contains("/api/v1", StringComparison.OrdinalIgnoreCase)
            ? $"{baseUrl}/Priloge/dokument/link"
            : $"{baseUrl}/api/v1/Priloge/dokument/link";

        var povezava = new
        {
            iD_DOK = idDokumenta,
            program = "FA",
            tip = 55,
            stevilka,
            stevilka2 = 0,
            cStevilka = string.Empty,
            leto,
            mesec = 0,
            crtknaKoda = string.Empty,
            opis = string.Empty,
            datum = string.Empty,
            progMapa = 55,
            partner = 0,
            sm1 = 0,
            sm2 = 0,
            sm3 = 0
        };

        var json = JsonSerializer.Serialize(povezava, new JsonSerializerOptions { WriteIndented = true });
        var requestSummary = $"HTTP metoda: POST{Environment.NewLine}URL: {povezavaUrl}{Environment.NewLine}Pogodba: številka={stevilka}, leto={leto}{Environment.NewLine}JSON telo:{Environment.NewLine}{json}";
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(povezavaUrl, content);
        var responseContent = await response.Content.ReadAsStringAsync();

        return (response.IsSuccessStatusCode,
            $"{requestSummary}{Environment.NewLine}Odgovor: {(int)response.StatusCode} {response.ReasonPhrase}; {responseContent}");
    }

    public List<(string Sifra, string Naziv)> NaloziKomercialiste()
    {
        using var db = CreateDb();
        return db.FaKomercialist
            .OrderBy(k => k.Priimek)
            .ToList()
            .Select(k => (k.Sifra, k.PolnoIme))
            .ToList();
    }
}
