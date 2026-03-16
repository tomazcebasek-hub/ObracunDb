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

        var zePreneseni = db.ObracunOsnutek
            .Where(o => o.Mesec == mesec && o.Leto == leto && o.RacunStevilka != null && o.RacunLeto != null)
            .Select(o => o.Partner)
            .Distinct()
            .Count();

        return new FawStanje
        {
            Mesec = mesec,
            Leto = leto,
            SkupajRacunov = vsiPartnerji.Count,
            ZePrenesenih = zePreneseni
        };
    }

    public List<int> PridobiPartnerjeZaPrenos(int mesec, int leto)
    {
        using var db = CreateDb();

        var vsiPartnerji = db.ObracunOsnutekPos
            .Where(p => p.Mesec == mesec && p.Leto == leto)
            .Select(p => p.Partner)
            .Distinct()
            .ToList();

        var zePreneseni = db.ObracunOsnutek
            .Where(o => o.Mesec == mesec && o.Leto == leto && o.RacunStevilka != null && o.RacunLeto != null)
            .Select(o => o.Partner)
            .ToHashSet();

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
            cache.DatumiNalogov = db.FaDnNalog
                .Where(n => stevilkeNalogov.Contains(n.Stevilka) && letaNalogov.Contains(n.Leto))
                .Select(n => new { n.Stevilka, n.Leto, n.Datum })
                .ToList()
                .ToDictionary(n => (n.Stevilka, n.Leto), n => n.Datum);
        }

        return cache;
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

            if (osnutek.RacunStevilka != null && osnutek.RacunLeto != null)
            {
                rezultat.Uspeh = true;
                rezultat.RacunStevilka = osnutek.RacunStevilka;
                rezultat.RacunLeto = osnutek.RacunLeto;
                rezultat.Sporocilo = $"Račun že zapisan: {osnutek.RacunStevilka}/{osnutek.RacunLeto}";
                return rezultat;
            }

            if (!cache.Postavke.TryGetValue(partner, out var postavke) || postavke.Count == 0)
            {
                rezultat.Sporocilo = "Ni postavk za tega partnerja.";
                return rezultat;
            }

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

            // Rok plačila: če ima partner veljavno pogodbo (tudi v prihodnosti), vzemi najdaljši rok iz pogodb
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
            var pogodbeZaMesec = pogodbe.Where(p =>
            {
                cache.PostavkePogodb.TryGetValue((p.Stevilka, p.Leto), out var pozicije);
                if (pozicije == null || pozicije.Count == 0) return true;
                return pozicije.Any(pz =>
                    string.IsNullOrWhiteSpace(pz.Meseci) ||
                    pz.Meseci.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(m => m.Trim()).Contains(mesecStr));
            }).ToList();
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
            {
                var nalogiSortirani = stevilkeNalogov
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

                // Po 3 naloge v vrstico, z zamikom za nadaljevanje
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
                vrstice.Add(sb.ToString());
            }

            var besedilo = string.Join("\r\n", vrstice);

            log?.Invoke($"[{sw.ElapsedMilliseconds}ms] Pripravljam JSON");
            var sifraKilometrina = _parametri.GetString(ObracunParam.SifraKilometrina) ?? "";
            var apiPostavke = postavke.Select(p => new
            {
                sifra = p.Artikel ?? "",
                naziv = p.Artikel == "-" || p.Artikel == sifraKilometrina ? (p.Naziv ?? "") : (string?)null,
                kolicina = p.Kolicina ?? 0,
                prodajnaCena = p.Cena ?? 0,
                rabat1 = p.Rabat ?? 0,
                stopnjaDdv = 0
            }).ToArray();

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
            {
                rezultat.Sporocilo = $"Napaka API ({(int)fakturaResponse.StatusCode}): {fakturaResponseContent}";
                return rezultat;
            }

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
        catch (Exception ex)
        {
            rezultat.Sporocilo = $"Napaka: {ex.Message}";
            return rezultat;
        }
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
