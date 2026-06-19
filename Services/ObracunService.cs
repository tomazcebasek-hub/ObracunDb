using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data.DTOs;
using ObracunDb.Data.Entities;

namespace ObracunDb.Services;

/// <summary>
/// Servis za delo z opravljenim delom iz tabele OBRACUN_OSNUTEK_NALOG_OBRACUN
/// </summary>
public class ObracunService
{
    private readonly Data.FirebirdConnectionManager _connectionManager;
    private readonly AuthService _authService;
    private readonly ParametriService _parametri;

    public ObracunService(Data.FirebirdConnectionManager connectionManager, AuthService authService, ParametriService parametri)
    {
        _connectionManager = connectionManager;
        _authService = authService;
        _parametri = parametri;
    }

    public string TrenutniUporabnik => _authService.CurrentUser?.UporabniskoIme ?? "?";

    private static async Task ZapisiRevizijo(FbConnection connection, string uporabnik,
        string tabela, string polje, string? staraVrednost, string? novaVrednost, string? kontekst,
        string? stevilka = null, int? leto = null)
    {
        await using var cmd = new FbCommand(@"
            INSERT INTO OBRACUN_REVIZIJA (DATUM, UPORABNIK, TABELA, POLJE, STARA_VREDNOST, NOVA_VREDNOST, KONTEKST, STEVILKA, LETO)
            VALUES (@Datum, @Uporabnik, @Tabela, @Polje, @StaraVrednost, @NovaVrednost, @Kontekst, @Stevilka, @Leto)", connection);

        cmd.Parameters.AddWithValue("@Datum", DateTime.Now);
        cmd.Parameters.AddWithValue("@Uporabnik", uporabnik);
        cmd.Parameters.AddWithValue("@Tabela", tabela);
        cmd.Parameters.AddWithValue("@Polje", polje);
        cmd.Parameters.AddWithValue("@StaraVrednost", (object?)staraVrednost ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NovaVrednost", (object?)novaVrednost ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Kontekst", (object?)kontekst ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Stevilka", (object?)stevilka ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Leto", (object?)leto ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Pridobi vse zapise iz baze
    /// </summary>
    public async Task<List<ObracunOsnutekNalogObracunDto>> GetAllAsync()
    {
        var result = new List<ObracunOsnutekNalogObracunDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT 
                MESEC, LETO, PARTNER, STEVILKA_NALOGA, LETO_NALOGA,
                OBRACUNAM, SIFRA_ARTIKLA, SIFRA_KOMERCIALISTA,
                KOLICINA, PRODAJNA_CENA,
                MINUTE_ODSTETE_PARTNER_MINUTE, MINUTE_ODSTETE_PREDRACUN,
                MINUTE_ODSTETE_ROCNO, MINUTE_ODSTETE_POGODBA,
                MINUTE_NALOG, KOLICINA_FAKTURIRANA
            FROM OBRACUN_OSNUTEK_NALOG_OBRACUN
            ORDER BY LETO DESC, MESEC DESC", connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var entity = ReadEntity(reader);
            result.Add(MapToDto(entity));
        }

        return result;
    }

    /// <summary>
    /// Pridobi zapise za doloceno obdobje (leto, mesec)
    /// </summary>
    public async Task<List<ObracunOsnutekNalogObracunDto>> GetByObdobjeAsync(int leto, int mesec)
    {
        var result = new List<ObracunOsnutekNalogObracunDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT 
                MESEC, LETO, PARTNER, STEVILKA_NALOGA, LETO_NALOGA,
                OBRACUNAM, SIFRA_ARTIKLA, SIFRA_KOMERCIALISTA,
                KOLICINA, PRODAJNA_CENA,
                MINUTE_ODSTETE_PARTNER_MINUTE, MINUTE_ODSTETE_PREDRACUN,
                MINUTE_ODSTETE_ROCNO, MINUTE_ODSTETE_POGODBA,
                MINUTE_NALOG, KOLICINA_FAKTURIRANA
            FROM OBRACUN_OSNUTEK_NALOG_OBRACUN
            WHERE LETO = @Leto AND MESEC = @Mesec
            ORDER BY PARTNER, STEVILKA_NALOGA", connection);
        
        command.Parameters.AddWithValue("@Leto", leto);
        command.Parameters.AddWithValue("@Mesec", mesec);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var entity = ReadEntity(reader);
            result.Add(MapToDto(entity));
        }

        return result;
    }

    /// <summary>
    /// Pridobi zapise za dolocenega partnerja
    /// </summary>
    public async Task<List<ObracunOsnutekNalogObracunDto>> GetByPartnerAsync(int partner)
    {
        var result = new List<ObracunOsnutekNalogObracunDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT 
                MESEC, LETO, PARTNER, STEVILKA_NALOGA, LETO_NALOGA,
                OBRACUNAM, SIFRA_ARTIKLA, SIFRA_KOMERCIALISTA,
                KOLICINA, PRODAJNA_CENA,
                MINUTE_ODSTETE_PARTNER_MINUTE, MINUTE_ODSTETE_PREDRACUN,
                MINUTE_ODSTETE_ROCNO, MINUTE_ODSTETE_POGODBA,
                MINUTE_NALOG, KOLICINA_FAKTURIRANA
            FROM OBRACUN_OSNUTEK_NALOG_OBRACUN
            WHERE PARTNER = @Partner
            ORDER BY LETO DESC, MESEC DESC", connection);
        
        command.Parameters.AddWithValue("@Partner", partner);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var entity = ReadEntity(reader);
            result.Add(MapToDto(entity));
        }

        return result;
    }

    /// <summary>
    /// Pridobi seštevke po partnerju za doloceno obdobje
    /// </summary>
    public async Task<List<OpravljenoPartnerGridDto>> GetSestevkiPoPartnerju(int leto, int mesec)
    {
        var result = new List<OpravljenoPartnerGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT 
                o.PARTNER,
                p.NAZIV,
                COUNT(*) AS STEVILO_NALOGOV,
                SUM(CASE WHEN o.OBRACUNAM = 1 THEN 1 ELSE 0 END) AS STEVILO_OBR,
                SUM(CASE WHEN o.OBRACUNAM = 0 THEN 1 ELSE 0 END) AS STEVILO_NEO,
                COALESCE(SUM(o.KOLICINA), 0) AS KOLICINA,
                COALESCE(SUM(o.KOLICINA * o.PRODAJNA_CENA), 0) AS VREDNOST
            FROM OBRACUN_OSNUTEK_NALOG_OBRACUN o
            LEFT JOIN PARTNER p ON o.PARTNER = p.SIFRA
            WHERE o.LETO = @Leto AND o.MESEC = @Mesec
            GROUP BY o.PARTNER, p.NAZIV
            ORDER BY p.NAZIV, o.PARTNER", connection);

        command.Parameters.AddWithValue("@Leto", leto);
        command.Parameters.AddWithValue("@Mesec", mesec);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new OpravljenoPartnerGridDto
            {
                Partner = reader.GetInt32(0),
                NazivPartnerja = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                SteviloNalogov = reader.GetInt32(2),
                SteviloObracunanih = reader.GetInt32(3),
                SteviloNeobracunanih = reader.GetInt32(4),
                Kolicina = reader.GetDecimal(5),
                Vrednost = reader.GetDecimal(6)
            });
        }

        return result;
    }

    /// <summary>
    /// Pridobi seštevke iz OBRACUN_OSNUTEK_POS po partnerju za doloceno obdobje
    /// </summary>
    public async Task<Dictionary<int, (decimal Kolicina, decimal Vrednost)>> GetSestevkiPosPoPartnerju(int leto, int mesec)
    {
        var result = new Dictionary<int, (decimal Kolicina, decimal Vrednost)>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT PARTNER, KOLICINA, CENA, RABAT
            FROM OBRACUN_OSNUTEK_POS
            WHERE LETO = @Leto AND MESEC = @Mesec", connection);

        command.Parameters.AddWithValue("@Leto", leto);
        command.Parameters.AddWithValue("@Mesec", mesec);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var partner = reader.GetInt32(0);
            var kolicina = reader.IsDBNull(1) ? 0m : reader.GetDecimal(1);
            var cena = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2);
            var rabat = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3);
            var vrednost = kolicina * cena * (1 - rabat / 100m);

            if (result.TryGetValue(partner, out var existing))
                result[partner] = (existing.Kolicina + kolicina, existing.Vrednost + vrednost);
            else
                result[partner] = (kolicina, vrednost);
        }

        return result;
    }

    /// <summary>
    /// Pridobi seštevek postavk po artiklu za določeno obdobje.
    /// </summary>
    public async Task<(List<SestevekGridDto> Postavke, int SteviloPartnerjev)> GetSestevekPoArtikluAsync(ObdobjeRange obdobje)
    {
        var postavke = new List<SestevekGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT 
                p.ARTIKEL,
                a.NAZIV,
                a.NAZIV2,
                a.ENOTA,
                COALESCE(SUM(p.KOLICINA), 0) AS KOLICINA,
                COALESCE(SUM(p.KOLICINA * p.CENA), 0) AS VREDNOST,
                COALESCE(SUM(p.KOLICINA * p.CENA * p.RABAT / 100), 0) AS POPUST,
                COALESCE(SUM(p.KOLICINA * p.CENA * (1 - p.RABAT / 100)), 0) AS NETO_VREDNOST,
                COUNT(DISTINCT p.PARTNER) AS ST_PARTNERJEV
            FROM OBRACUN_OSNUTEK_POS p
            LEFT JOIN FA_ARTIKEL a ON p.ARTIKEL = a.SIFRA
            WHERE (p.LETO * 100 + p.MESEC) BETWEEN @Od AND @Do
            GROUP BY p.ARTIKEL, a.NAZIV, a.NAZIV2, a.ENOTA
            ORDER BY p.ARTIKEL", connection);

        cmd.Parameters.AddWithValue("@Od", obdobje.KljucOd);
        cmd.Parameters.AddWithValue("@Do", obdobje.KljucDo);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var naziv = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
            var naziv2 = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();

            postavke.Add(new SestevekGridDto
            {
                Sifra = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim(),
                Naziv = ArtikelHelper.GetFullName(naziv, naziv2),
                Enota = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim(),
                Kolicina = reader.GetDecimal(4),
                Vrednost = reader.GetDecimal(5),
                Popust = reader.GetDecimal(6),
                NetoVrednost = reader.GetDecimal(7),
                StPartnerjev = reader.GetInt32(8)
            });
        }

        // Skupno število partnerjev
        int steviloPartnerjev = 0;
        await using var cmd2 = new FbCommand(@"
            SELECT COUNT(DISTINCT PARTNER) FROM OBRACUN_OSNUTEK_POS
            WHERE (LETO * 100 + MESEC) BETWEEN @Od AND @Do", connection);
        cmd2.Parameters.AddWithValue("@Od", obdobje.KljucOd);
        cmd2.Parameters.AddWithValue("@Do", obdobje.KljucDo);
        var obj = await cmd2.ExecuteScalarAsync();
        if (obj != null && obj != DBNull.Value)
            steviloPartnerjev = Convert.ToInt32(obj);

        return (postavke, steviloPartnerjev);
    }

    /// <summary>
    /// Pridobi distinct mesece/leta iz OBRACUN_OSNUTEK_POS, razvrščene padajoče.
    /// </summary>
    public async Task<List<(int Leto, int Mesec)>> GetSestevekMeseciAsync()
    {
        var rezultat = new List<(int Leto, int Mesec)>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT DISTINCT LETO, MESEC
            FROM OBRACUN_OSNUTEK_POS
            ORDER BY LETO DESC, MESEC DESC", connection);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rezultat.Add((reader.GetInt32(0), reader.GetInt32(1)));
        }

        return rezultat;
    }

    /// <summary>
    /// Pridobi seštevek postavk po partnerjih za določen artikel.
    /// </summary>
    public async Task<List<SestevekDetailDto>> GetSestevekDetailAsync(ObdobjeRange obdobje, string artikel)
    {
        var rezultat = new List<SestevekDetailDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT 
                p.PARTNER,
                pa.NAZIV,
                COALESCE(SUM(p.KOLICINA), 0) AS KOLICINA,
                COALESCE(SUM(p.KOLICINA * p.CENA), 0) AS VREDNOST,
                COALESCE(SUM(p.KOLICINA * p.CENA * p.RABAT / 100), 0) AS POPUST,
                COALESCE(SUM(p.KOLICINA * p.CENA * (1 - p.RABAT / 100)), 0) AS NETO_VREDNOST
            FROM OBRACUN_OSNUTEK_POS p
            LEFT JOIN PARTNER pa ON p.PARTNER = pa.SIFRA
            WHERE (p.LETO * 100 + p.MESEC) BETWEEN @Od AND @Do AND p.ARTIKEL = @Artikel
            GROUP BY p.PARTNER, pa.NAZIV
            ORDER BY pa.NAZIV, p.PARTNER", connection);

        cmd.Parameters.AddWithValue("@Od", obdobje.KljucOd);
        cmd.Parameters.AddWithValue("@Do", obdobje.KljucDo);
        cmd.Parameters.AddWithValue("@Artikel", artikel);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var sifra = reader.GetInt32(0);
            var naziv = reader.IsDBNull(1) ? sifra.ToString() : reader.GetString(1).Trim();
            if (string.IsNullOrEmpty(naziv)) naziv = sifra.ToString();

            rezultat.Add(new SestevekDetailDto
            {
                SifraPartnerja = sifra,
                NazivPartnerja = naziv,
                Kolicina = reader.GetDecimal(2),
                Vrednost = reader.GetDecimal(3),
                Popust = reader.GetDecimal(4),
                NetoVrednost = reader.GetDecimal(5)
            });
        }

        return rezultat;
    }

    /// <summary>
    /// Pridobi vse osnutke po partnerjih za gornji grid na strani Osnutki.
    /// Vrne podatke iz OBRACUN_OSNUTEK + agregat iz OBRACUN_OSNUTEK_NALOG_OBRACUN + število pogodb.
    /// Podatki vključujejo tudi vse kar potrebuje Info panel, da ni potrebno dodatno pridobivanje.
    /// </summary>
    public async Task<List<OsnutekPartnerDto>> GetOsnutkiAsync(int leto, int mesec)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        var prviDan = new DateTime(leto, mesec, 1);

        // 1. Osnova iz OBRACUN_OSNUTEK + PARTNER naziv
        var partnerji = new Dictionary<int, OsnutekPartnerDto>();
        await using (var cmd = new FbCommand(@"
            SELECT o.PARTNER, p.NAZIV,
                   o.IMA_POGODBO, o.IMA_PREDRACUN, o.IMA_NALOGE,
                   o.MINUTE_OBRACUNANE, o.MINUTE_NEOBRACUNANE, o.MINUTE_KORISCENE,
                   o.PLUS_MINUTE_PARTNER_MINUTE, o.PLUS_MINUTE_PREDRACUN,
                   o.PLUS_MINUTE_ROCNO, o.PLUS_MINUTE_POGODBA,
                   o.OPIS
            FROM OBRACUN_OSNUTEK o
            LEFT JOIN PARTNER p ON o.PARTNER = p.SIFRA
            WHERE o.LETO = @Leto AND o.MESEC = @Mesec
            ORDER BY p.NAZIV, o.PARTNER", connection))
        {
            cmd.Parameters.AddWithValue("@Leto", leto);
            cmd.Parameters.AddWithValue("@Mesec", mesec);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var partner = reader.GetInt32(0);
                var minuteObracunane = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
                var minuteKoriscene = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);

                partnerji[partner] = new OsnutekPartnerDto
                {
                    Sifra = partner,
                    NazivPartnerja = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                    ImaPogodbo = !reader.IsDBNull(2) && reader.GetInt32(2) != 0,
                    ImaPredracun = !reader.IsDBNull(3) && reader.GetInt32(3) != 0,
                    ImaNaloge = !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                    ZaracMin = minuteObracunane,
                    MinNeobr = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                    MinuteKoriscene = minuteKoriscene,
                    MinuteObracunaneBruto = minuteObracunane + minuteKoriscene,
                    PlusMinutePartnerMinute = reader.IsDBNull(8) ? 0 : reader.GetInt32(8),
                    PlusMinutePredracun = reader.IsDBNull(9) ? 0 : reader.GetInt32(9),
                    PlusMinuteRocno = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                    PlusMinutePogodba = reader.IsDBNull(11) ? 0 : reader.GetInt32(11),
                    Opis = reader.IsDBNull(12) ? null : reader.GetString(12)
                };
            }
        }

        // 2. Koriščene minute po tipu iz OBRACUN_OSNUTEK_NALOG_OBRACUN
        await using (var cmd = new FbCommand(@"
            SELECT PARTNER,
                   COALESCE(SUM(MINUTE_ODSTETE_POGODBA), 0),
                   COALESCE(SUM(MINUTE_ODSTETE_PREDRACUN), 0),
                   COALESCE(SUM(MINUTE_ODSTETE_ROCNO), 0),
                   COALESCE(SUM(MINUTE_ODSTETE_PARTNER_MINUTE), 0)
            FROM OBRACUN_OSNUTEK_NALOG_OBRACUN
            WHERE LETO = @Leto AND MESEC = @Mesec
            GROUP BY PARTNER", connection))
        {
            cmd.Parameters.AddWithValue("@Leto", leto);
            cmd.Parameters.AddWithValue("@Mesec", mesec);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var partner = reader.GetInt32(0);
                if (partnerji.TryGetValue(partner, out var dto))
                {
                    dto.KorPog = reader.GetInt32(1);
                    dto.KorPre = reader.GetInt32(2);
                    dto.KorRoc = reader.GetInt32(3);
                    dto.KorPar = reader.GetInt32(4);
                }
            }
        }

        // 3. Število veljavnih pogodb iz FA_POGODBE
        await using (var cmd = new FbCommand(@"
            SELECT PARTNER, COUNT(*)
            FROM FA_POGODBE
            WHERE (VELJA_DO IS NULL OR VELJA_DO >= @PrviDan)
              AND (PRVI_RACUN_OD IS NULL OR PRVI_RACUN_OD <= @ZadnjiDan)
            GROUP BY PARTNER", connection))
        {
            cmd.Parameters.AddWithValue("@PrviDan", prviDan);
            cmd.Parameters.AddWithValue("@ZadnjiDan", prviDan.AddMonths(1).AddDays(-1));

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var partner = reader.GetInt32(0);
                if (partnerji.TryGetValue(partner, out var dto))
                {
                    dto.SteviloPogodb = reader.GetInt32(1);
                }
            }
        }

        // 4. Že porabljene minute iz OBRACUN_PORABA_MINUT (mesec/leto < param)
        //    VseMinute = PlusMinute (preostalo) + ZePorabljene
        await using (var cmd = new FbCommand(@"
            SELECT PARTNER, TIP, SUM(KOLICINA)
            FROM OBRACUN_PORABA_MINUT
            WHERE (LETO < @Leto OR (LETO = @Leto AND MESEC < @Mesec))
            GROUP BY PARTNER, TIP", connection))
        {
            cmd.Parameters.AddWithValue("@Leto", leto);
            cmd.Parameters.AddWithValue("@Mesec", mesec);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var partner = reader.GetInt32(0);
                var tip = reader.GetInt32(1);
                var kolicina = reader.GetInt32(2);
                if (partnerji.TryGetValue(partner, out var dto))
                {
                    if (tip == 1) // Predračun
                    {
                        dto.ZePorabljenePredracun = kolicina;
                        dto.VseMinutePredracun = dto.PlusMinutePredracun + kolicina;
                    }
                    else if (tip == 2) // PartnerMinute
                    {
                        dto.ZePorabljenePartnerMinute = kolicina;
                        dto.VseMinutePartnerMinute = dto.PlusMinutePartnerMinute + kolicina;
                    }
                }
            }
        }

        // Partnerji brez preteklih porab: vse = preostalo
        foreach (var dto in partnerji.Values)
        {
            if (dto.VseMinutePredracun == 0 && dto.PlusMinutePredracun > 0)
                dto.VseMinutePredracun = dto.PlusMinutePredracun;
            if (dto.VseMinutePartnerMinute == 0 && dto.PlusMinutePartnerMinute > 0)
                dto.VseMinutePartnerMinute = dto.PlusMinutePartnerMinute;
        }

        // 5. Potrditve iz OBRACUN_OSNUTEK_POTRDITEV
        await using (var cmd = new FbCommand(@"
            SELECT PARTNER, KDO, KDAJ
            FROM OBRACUN_OSNUTEK_POTRDITEV
            WHERE LETO = @Leto AND MESEC = @Mesec", connection))
        {
            cmd.Parameters.AddWithValue("@Leto", leto);
            cmd.Parameters.AddWithValue("@Mesec", mesec);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var partner = reader.GetInt32(0);
                if (partnerji.TryGetValue(partner, out var dto))
                {
                    dto.PotrdilKdo = reader.GetString(1).Trim();
                    dto.PotrdilKdaj = reader.GetDateTime(2);
                }
            }
        }

        return partnerji.Values.ToList();
    }

    /// <summary>
    /// Potrdi osnutek za partnerja (INSERT v OBRACUN_OSNUTEK_POTRDITEV).
    /// </summary>
    public async Task PotrdiOsnutekAsync(int leto, int mesec, int partner)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            INSERT INTO OBRACUN_OSNUTEK_POTRDITEV (PARTNER, MESEC, LETO, KDO, KDAJ)
            VALUES (@Partner, @Mesec, @Leto, @Kdo, @Kdaj)", connection);

        command.Parameters.AddWithValue("@Partner", partner);
        command.Parameters.AddWithValue("@Mesec", mesec);
        command.Parameters.AddWithValue("@Leto", leto);
        command.Parameters.AddWithValue("@Kdo", TrenutniUporabnik);
        command.Parameters.AddWithValue("@Kdaj", DateTime.Now);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Pridobi pogodbe za partnerja, ki so že veljavne ali bodo veljavne v prihodnosti.
    /// </summary>
    public async Task<List<PogodbaGridDto>> GetPogodbeZaPartnerjaAsync(int partner, int leto, int mesec)
    {
        var result = new List<PogodbaGridDto>();
        var prviDan = new DateTime(leto, mesec, 1);

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT STEVILKA, LETO, ST_POGODBE, DATUM, PRVI_RACUN_OD, VELJA_DO,
                   NA_KOLIKO_MESECEV, ST_MINUT, OPOMBA, SIF_NAPREJ_NAZAJ
            FROM FA_POGODBE
            WHERE PARTNER = @Partner
              AND (VELJA_DO IS NULL OR VELJA_DO >= @PrviDan OR PRVI_RACUN_OD > @PrviDan)
            ORDER BY LETO DESC, STEVILKA DESC", connection);

        cmd.Parameters.AddWithValue("@Partner", partner);
        cmd.Parameters.AddWithValue("@PrviDan", prviDan);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PogodbaGridDto
            {
                Stevilka = reader.GetInt32(0),
                Leto = reader.GetInt32(1),
                StPogodbe = reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                Datum = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                PrviRacunOd = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                VeljaDo = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                NaKolikoMesecev = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                StMinut = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                Opomba = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
                SifNaprejNazaj = reader.IsDBNull(9) ? null : reader.GetInt32(9)
            });
        }

        return result;
    }

    /// <summary>
    /// Pridobi pozicije pogodbe z nazivi artiklov in prevedenimi meseci.
    /// </summary>
    public async Task<List<PogodbaPozGridDto>> GetPogodbePozicijeAsync(int stevilka, int leto)
    {
        var result = new List<PogodbaPozGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT p.ZS, p.SIFRA, a.NAZIV, a.NAZIV2, p.KOLICINA, p.PRODAJNA_CENA, p.RABAT1, p.MESECI,
                   p.NAZIV AS POS_NAZIV
            FROM FA_POGODBE_POS p
            LEFT JOIN FA_ARTIKEL a ON p.SIFRA = a.SIFRA
            WHERE p.STEVILKA = @Stevilka AND p.LETO = @Leto
            ORDER BY p.ZS", connection);

        cmd.Parameters.AddWithValue("@Stevilka", stevilka);
        cmd.Parameters.AddWithValue("@Leto", leto);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var sifra = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
            var meseciRaw = reader.IsDBNull(7) ? null : reader.GetString(7).Trim();

            string nazivArtikla;
            if (sifra == "-")
            {
                nazivArtikla = reader.IsDBNull(8) ? "" : reader.GetString(8).Trim();
            }
            else
            {
                var naziv = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                var naziv2 = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim();
                nazivArtikla = ArtikelHelper.GetFullName(naziv, naziv2);
            }

            result.Add(new PogodbaPozGridDto
            {
                Pozicija = reader.GetInt32(0),
                Sifra = sifra,
                Naziv = nazivArtikla,
                Kolicina = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4),
                Cena = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5),
                Rabat = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6),
                MeseciRaw = meseciRaw,
                Meseci = PretvoriMesece(meseciRaw)
            });
        }

        return result;
    }

    /// <summary>
    /// Pretvori string mesecev "01,02,03" v berljivo obliko "jan, feb, mar".
    /// Če so vsi meseci (12), vrne "vsi".
    /// </summary>
    private static string? PretvoriMesece(string? meseciRaw)
    {
        if (string.IsNullOrWhiteSpace(meseciRaw))
            return null;

        var meseci = meseciRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (meseci.Length >= 12)
            return "vsi";

        var imena = new Dictionary<string, string>
        {
            ["01"] = "jan", ["02"] = "feb", ["03"] = "mar", ["04"] = "apr",
            ["05"] = "maj", ["06"] = "jun", ["07"] = "jul", ["08"] = "avg",
            ["09"] = "sep", ["10"] = "okt", ["11"] = "nov", ["12"] = "dec"
        };

        return string.Join(", ", meseci.Select(m => imena.TryGetValue(m, out var ime) ? ime : m));
    }

    /// <summary>
    /// Pridobi postavke osnutka za partnerja z nazivi artiklov
    /// </summary>
    public async Task<List<OsnutekPosGridDto>> GetPostavkeOsnutkaAsync(int leto, int mesec, int partner)
    {
        var result = new List<OsnutekPosGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT p.ZS, p.ARTIKEL, p.KOLICINA, p.CENA, p.RABAT,
                   a.NAZIV AS ARTIKEL_NAZIV, a.NAZIV2 AS ARTIKEL_NAZIV2, a.ENOTA,
                   p.NAZIV AS POS_NAZIV
            FROM OBRACUN_OSNUTEK_POS p
            LEFT JOIN FA_ARTIKEL a ON p.ARTIKEL = a.SIFRA
            WHERE p.LETO = @Leto AND p.MESEC = @Mesec AND p.PARTNER = @Partner
            ORDER BY p.ZS", connection);

        cmd.Parameters.AddWithValue("@Leto", leto);
        cmd.Parameters.AddWithValue("@Mesec", mesec);
        cmd.Parameters.AddWithValue("@Partner", partner);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var kolicina = reader.IsDBNull(2) ? 0m : reader.GetDecimal(2);
            var cena = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3);
            var rabat = reader.IsDBNull(4) ? 0m : reader.GetDecimal(4);
            var artikel = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
            var sifraKilometrina = _parametri.GetString(ObracunParam.SifraKilometrina) ?? "";

            string nazivArtikla;
            string? enotaArtikla;
            if (artikel == "-")
            {
                nazivArtikla = reader.IsDBNull(8) ? "" : reader.GetString(8).Trim();
                enotaArtikla = null;
            }
            else if (artikel == sifraKilometrina)
            {
                nazivArtikla = reader.IsDBNull(8) ? "" : reader.GetString(8).Trim();
                enotaArtikla = reader.IsDBNull(7) ? null : reader.GetString(7).Trim();
            }
            else
            {
                var naziv = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim();
                var naziv2 = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim();
                nazivArtikla = ArtikelHelper.GetFullName(naziv, naziv2);
                enotaArtikla = reader.IsDBNull(7) ? null : reader.GetString(7).Trim();
            }

            result.Add(new OsnutekPosGridDto
            {
                Zs = reader.GetInt32(0),
                Artikel = artikel,
                NazivArtikla = nazivArtikla,
                EnotaArtikla = enotaArtikla,
                Kolicina = kolicina,
                Cena = cena,
                Rabat = rabat,
                Vrednost = kolicina * cena * (1 - rabat / 100m)
            });
        }

        return result;
    }

    public async Task<List<RocnaPostavkaGridDto>> GetRocnePostavkeAsync(int leto, int mesec, int partner)
    {
        var result = new List<RocnaPostavkaGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT p.ZS, p.NALOG_STEVILKA, p.NALOG_LETO, p.ARTIKEL,
                   a.NAZIV AS ARTIKEL_NAZIV, a.NAZIV2 AS ARTIKEL_NAZIV2,
                   COALESCE(pm.MINUT, 0) AS MINUT, p.KOLICINA,
                   n.ZACETEK_DATUM, COALESCE(a.PRODAJNA_CENA, 0) AS PRODAJNA_CENA
            FROM OBRACUN_OSNUTEK_POS p
            LEFT JOIN FA_ARTIKEL a ON p.ARTIKEL = a.SIFRA
            LEFT JOIN OBRACUN_PAKET_MINUTE pm ON pm.ARTIKEL = p.ARTIKEL
            LEFT JOIN FA_DN_NALOG n ON n.STEVILKA = p.NALOG_STEVILKA AND n.LETO = p.NALOG_LETO
            WHERE p.LETO = @Leto AND p.MESEC = @Mesec AND p.PARTNER = @Partner
              AND p.TIP_POSTAVKE = 1
            ORDER BY p.ZS", connection);

        cmd.Parameters.AddWithValue("@Leto", leto);
        cmd.Parameters.AddWithValue("@Mesec", mesec);
        cmd.Parameters.AddWithValue("@Partner", partner);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var kolicina = reader.IsDBNull(7) ? 0 : (int)reader.GetDecimal(7);
            var minutNaArtikel = reader.GetInt32(6);
            var naziv = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim();
            var naziv2 = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim();
            var prodajnaCena = reader.GetDecimal(9);

            result.Add(new RocnaPostavkaGridDto
            {
                Zs = reader.GetInt32(0),
                NalogStevilka = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                NalogLeto = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Artikel = reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                NazivArtikla = ArtikelHelper.GetFullName(naziv, naziv2),
                Kolicina = kolicina,
                ProdajnaCena = prodajnaCena,
                DatumNaloga = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                Minute = kolicina * minutNaArtikel
            });
        }

        return result;
    }

    /// <summary>
    /// Pridobi račune za partnerja (od leta 2026 naprej). Če jeAvans=true, vrne samo tip_racuna=4, sicer vse razen tip_racuna=4.
    /// </summary>
    public async Task<List<RacunGridDto>> GetRacuniZaPartnerjaAsync(int partner, bool jeAvans)
    {
        var result = new List<RacunGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        var tipFilter = jeAvans ? "AND r.TIP_RACUNA = 4" : "AND (r.TIP_RACUNA IS NULL OR r.TIP_RACUNA <> 4)";
        await using var cmd = new FbCommand($@"
            SELECT r.STEVILKA, r.LETO, r.DATUM, r.ZNESEK_KONCNI, r.TIP_RACUNA
            FROM FA_RACUN r
            WHERE r.SIFRA_KUPCA = @Partner AND r.LETO >= 2026 {tipFilter}
            ORDER BY r.LETO DESC, r.STEVILKA DESC", connection);

        cmd.Parameters.AddWithValue("@Partner", partner);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new RacunGridDto
            {
                Stevilka = reader.IsDBNull(0) ? "" : reader.GetValue(0).ToString()!.Trim(),
                Leto = reader.GetInt32(1),
                Datum = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                ZnesekKoncni = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                TipRacuna = reader.IsDBNull(4) ? 0 : reader.GetInt32(4)
            });
        }

        return result;
    }

    /// <summary>
    /// Pridobi postavke računa glede na TIP_RACUNA (ista logika kot v Partnerji).
    /// </summary>
    public async Task<List<RacunKnjizbaDto>> GetPostavkeRacunaAsync(string stevilka, int leto, int tipRacuna)
    {
        var partnerService = new PartnerService(_connectionManager);
        return await partnerService.GetRacunKnjizbeAsync(stevilka, leto, tipRacuna);
    }

    /// <summary>
    public async Task<PartnerPovzetekDto> GetPartnerPovzetekAsync(int leto, int mesec, int partner)
    {
        var dto = new PartnerPovzetekDto();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // 1. Minute iz OBRACUN_OSNUTEK_NALOG_OBRACUN + plus minute iz OBRACUN_OSNUTEK
        await using (var cmd = new FbCommand(@"
            SELECT
                COALESCE(SUM(CASE WHEN OBRACUNAM = 1 THEN MINUTE_NALOG ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN OBRACUNAM = 0 THEN MINUTE_NALOG ELSE 0 END), 0)
            FROM OBRACUN_OSNUTEK_NALOG_OBRACUN
            WHERE LETO = @Leto AND MESEC = @Mesec AND PARTNER = @Partner", connection))
        {
            cmd.Parameters.AddWithValue("@Leto", leto);
            cmd.Parameters.AddWithValue("@Mesec", mesec);
            cmd.Parameters.AddWithValue("@Partner", partner);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                dto.MinuteObracunane = reader.GetInt32(0);
                dto.MinuteNeobracunane = reader.GetInt32(1);
            }
        }

        // Plus minute iz OBRACUN_OSNUTEK
        await using (var cmd = new FbCommand(@"
            SELECT PLUS_MINUTE_PARTNER_MINUTE, PLUS_MINUTE_PREDRACUN,
                   PLUS_MINUTE_ROCNO, PLUS_MINUTE_POGODBA
            FROM OBRACUN_OSNUTEK
            WHERE LETO = @Leto AND MESEC = @Mesec AND PARTNER = @Partner", connection))
        {
            cmd.Parameters.AddWithValue("@Leto", leto);
            cmd.Parameters.AddWithValue("@Mesec", mesec);
            cmd.Parameters.AddWithValue("@Partner", partner);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                dto.PlusMinutePartnerMinute = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                dto.PlusMinutePredracun = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                dto.PlusMinuteRocno = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                dto.PlusMinutePogodba = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            }
        }

        // 2. Koriščene minute iz OBRACUN_OSNUTEK_NALOG_OBRACUN (vsota po partnerju)
        await using (var cmd = new FbCommand(@"
            SELECT
                COALESCE(SUM(MINUTE_ODSTETE_PARTNER_MINUTE), 0),
                COALESCE(SUM(MINUTE_ODSTETE_PREDRACUN), 0),
                COALESCE(SUM(MINUTE_ODSTETE_ROCNO), 0),
                COALESCE(SUM(MINUTE_ODSTETE_POGODBA), 0)
            FROM OBRACUN_OSNUTEK_NALOG_OBRACUN
            WHERE LETO = @Leto AND MESEC = @Mesec AND PARTNER = @Partner", connection))
        {
            cmd.Parameters.AddWithValue("@Leto", leto);
            cmd.Parameters.AddWithValue("@Mesec", mesec);
            cmd.Parameters.AddWithValue("@Partner", partner);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                dto.KoriscenoPartnerMinute = reader.GetInt32(0);
                dto.KoriscenoPredracun = reader.GetInt32(1);
                dto.KoriscenoRocno = reader.GetInt32(2);
                dto.KoriscenoPogodba = reader.GetInt32(3);
            }
        }

        // 3. Število pogodb
        await using (var cmd = new FbCommand(@"
            SELECT COUNT(*) FROM FA_POGODBE
            WHERE PARTNER = @Partner
              AND (VELJA_DO IS NULL OR VELJA_DO >= CAST(@Datum AS DATE))", connection))
        {
            cmd.Parameters.AddWithValue("@Partner", partner);
            cmd.Parameters.AddWithValue("@Datum", new DateTime(leto, mesec, 1));

            var count = await cmd.ExecuteScalarAsync();
            dto.SteviloPogodb = Convert.ToInt32(count);
        }

        return dto;
    }

    /// <summary>
    /// Pridobi naloge za partnerja (obračunane ali neobračunane)
    /// </summary>
    public async Task<List<NalogGridDto>> GetNalogiZaPartnerja(int leto, int mesec, int partner, int obracunam)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // 1. Preberi osnovne podatke iz OBRACUN_OSNUTEK_NALOG_OBRACUN
        var nalogi = new Dictionary<string, NalogGridDto>();

        await using (var cmd = new FbCommand(@"
            SELECT STEVILKA_NALOGA, LETO_NALOGA, SIFRA_ARTIKLA, SIFRA_KOMERCIALISTA, MINUTE_NALOG
            FROM OBRACUN_OSNUTEK_NALOG_OBRACUN
            WHERE LETO = @Leto AND MESEC = @Mesec 
              AND PARTNER = @Partner AND OBRACUNAM = @Obracunam
            ORDER BY STEVILKA_NALOGA", connection))
        {
            cmd.Parameters.AddWithValue("@Leto", leto);
            cmd.Parameters.AddWithValue("@Mesec", mesec);
            cmd.Parameters.AddWithValue("@Partner", partner);
            cmd.Parameters.AddWithValue("@Obracunam", obracunam);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var stevilka = reader.GetString(0);
                if (nalogi.ContainsKey(stevilka)) continue;

                var sifraArtikla = reader.IsDBNull(2) ? null : reader.GetString(2).Trim();
                var sifraKomercialista = reader.IsDBNull(3) ? null : reader.GetString(3).Trim();

                nalogi[stevilka] = new NalogGridDto
                {
                    Stevilka = stevilka,
                    Artikel = sifraArtikla,
                    Minute = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    _LetoNaloga = reader.IsDBNull(1) ? null : reader.GetInt32(1) ,
                    _SifraPotnika = sifraKomercialista
                };
            }
        }

        if (nalogi.Count == 0) return new List<NalogGridDto>();

        // 2. Preberi nazive artiklov
        var sifre = nalogi.Values.Where(n => n.Artikel != null).Select(n => n.Artikel!).Distinct().ToList();
        if (sifre.Count > 0)
        {
            var inList = string.Join(",", sifre.Select(s => $"'{s.Replace("'", "''")}'"));
            await using var cmd = new FbCommand($"SELECT SIFRA, NAZIV, NAZIV2, ENOTA FROM FA_ARTIKEL WHERE SIFRA IN ({inList})", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            var artikli = new Dictionary<string, (string Naziv, string? Enota)>();
            while (await reader.ReadAsync())
            {
                var sifra = reader.GetString(0).Trim();
                var naziv = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                var naziv2 = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                var enota = reader.IsDBNull(3) ? null : reader.GetString(3).Trim();
                artikli[sifra] = (ArtikelHelper.GetFullName(naziv, naziv2), enota);
            }
            foreach (var n in nalogi.Values.Where(n => n.Artikel != null && artikli.ContainsKey(n.Artikel!)))
            {
                n.NazivArtikla = artikli[n.Artikel!].Naziv;
                n.EnotaArtikla = artikli[n.Artikel!].Enota;
            }
        }

        // 3. Preberi nazive komercialistov
        var komSifre = nalogi.Values.Where(n => n._SifraPotnika != null).Select(n => n._SifraPotnika!).Distinct().ToList();
        if (komSifre.Count > 0)
        {
            var inList = string.Join(",", komSifre.Select(s => $"'{s.Replace("'", "''")}'"));
            await using var cmd = new FbCommand($"SELECT SIFRA, PRIIMEK, IME FROM FA_KOMERCIALIST WHERE SIFRA IN ({inList})", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            var komerci = new Dictionary<string, string>();
            while (await reader.ReadAsync())
            {
                var sifra = reader.GetString(0).Trim();
                var priimek = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                var ime = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                komerci[sifra] = string.IsNullOrEmpty(ime) ? priimek : $"{priimek} {ime}".Trim();
            }
            foreach (var n in nalogi.Values.Where(n => n._SifraPotnika != null && komerci.ContainsKey(n._SifraPotnika!)))
                n.NazivPotnika = komerci[n._SifraPotnika!];
        }

        // 4. Preberi podatke iz FA_DN_NALOG (datum, ure, opis iz NAZIV1..NAZIV20)
        var stevilke = nalogi.Keys.ToList();
        var leta = nalogi.Values.Where(n => n._LetoNaloga.HasValue).Select(n => n._LetoNaloga!.Value).Distinct().ToList();
        if (stevilke.Count > 0 && leta.Count > 0)
        {
            var stevilkeIn = string.Join(",", stevilke.Select(s => $"'{s.Replace("'", "''")}'")); 
            var letaIn = string.Join(",", leta);
            await using var cmd = new FbCommand($@"
                SELECT STEVILKA, ZACETEK_DATUM, ZACETEK_URA, KONEC_URA,
                    NAZIV1, NAZIV2, NAZIV3, NAZIV4, NAZIV5,
                    NAZIV6, NAZIV7, NAZIV8, NAZIV9, NAZIV10,
                    NAZIV11, NAZIV12, NAZIV13, NAZIV14, NAZIV15,
                    NAZIV16, NAZIV17, NAZIV18, NAZIV19, NAZIV20
                FROM FA_DN_NALOG
                WHERE STEVILKA IN ({stevilkeIn}) AND LETO IN ({letaIn})", connection);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var stevilka = reader.GetString(0).Trim();
                if (!nalogi.TryGetValue(stevilka, out var nalog)) continue;

                nalog.Datum = reader.IsDBNull(1) ? null : reader.GetDateTime(1);
                nalog.ZacetekUra = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
                nalog.KonecUra = reader.IsDBNull(3) ? null : reader.GetDateTime(3);

                var nazivi = new string?[20];
                for (int i = 0; i < 20; i++)
                    nazivi[i] = reader.IsDBNull(4 + i) ? null : reader.GetString(4 + i);

                nalog.Naziv1 = nazivi[0]?.Trim();
                nalog.Opis = string.Join(Environment.NewLine,
                    nazivi.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()));
            }
        }

        return nalogi.Values.OrderBy(n => n.Stevilka).ToList();
    }

    private static ObracunOsnutekNalogObracun ReadEntity(FbDataReader reader)
    {
        return new ObracunOsnutekNalogObracun
        {
            Mesec = reader.GetInt32(0),
            Leto = reader.GetInt32(1),
            Partner = reader.GetInt32(2),
            StevilkaNaloga = reader.GetString(3),
            LetoNaloga = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            Obracunam = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
            SifraArtikla = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
            SifraKomercialista = reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
            Kolicina = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
            ProdajnaCena = reader.IsDBNull(9) ? null : reader.GetDecimal(9),
            MinuteOdstetePartnerMinute = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            MinuteOdstetePredracun = reader.IsDBNull(11) ? null : reader.GetInt32(11),
            MinuteOdsteteRocno = reader.IsDBNull(12) ? null : reader.GetInt32(12),
            MinuteOdstetePogodba = reader.IsDBNull(13) ? null : reader.GetInt32(13),
            MinuteNalog = reader.IsDBNull(14) ? null : reader.GetInt32(14),
            KolicinaFakturirana = reader.IsDBNull(15) ? null : reader.GetDecimal(15)
        };
    }

    private static ObracunOsnutekNalogObracunDto MapToDto(ObracunOsnutekNalogObracun entity)
    {
        return new ObracunOsnutekNalogObracunDto
        {
            Mesec = entity.Mesec,
            Leto = entity.Leto,
            Partner = entity.Partner,
            StevilkaNaloga = entity.StevilkaNaloga,
            LetoNaloga = entity.LetoNaloga,
            Obracunam = entity.Obracunam,
            SifraArtikla = entity.SifraArtikla,
            SifraKomercialista = entity.SifraKomercialista,
            Kolicina = entity.Kolicina,
            ProdajnaCena = entity.ProdajnaCena,
            MinuteOdstetePartnerMinute = entity.MinuteOdstetePartnerMinute,
            MinuteOdstetePredracun = entity.MinuteOdstetePredracun,
            MinuteOdsteteRocno = entity.MinuteOdsteteRocno,
            MinuteOdstetePogodba = entity.MinuteOdstetePogodba,
            MinuteNalog = entity.MinuteNalog,
            KolicinaFakturirana = entity.KolicinaFakturirana
        };
    }

    // ==================== Potrjevanje nalogov ====================

    /// <summary>
    /// Pridobi naloge za stran Potrjevanje nalogov.
    /// Filter: DATUM >= 1. dan meseca iz parametrov.
    /// </summary>
    public async Task<List<PotrjevanjeNalogDto>> GetNalogiZaPotrjevanjeAsync(DateTime datumOd, DateTime datumDo)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // 1. Preberi naloge
        var nalogi = new List<PotrjevanjeNalogDto>();
        await using (var cmd = new FbCommand(@"
            SELECT n.STEVILKA, n.LETO, n.PARTNER, n.ZACETEK_DATUM, n.ZACETEK_URA, n.KONEC_URA, n.SIF27, n.POTNIK,
                   n.NAZIV1, n.NAZIV2, n.NAZIV3, n.NAZIV4, n.NAZIV5,
                   n.NAZIV6, n.NAZIV7, n.NAZIV8, n.NAZIV9, n.NAZIV10,
                   n.NAZIV11, n.NAZIV12, n.NAZIV13, n.NAZIV14, n.NAZIV15,
                   n.NAZIV16, n.NAZIV17, n.NAZIV18, n.NAZIV19, n.NAZIV20,
                   n.SIF29, n.PRODAJALNA, n.SIF30
            FROM FA_DN_NALOG n
            WHERE n.ZACETEK_DATUM >= @DatumOd AND n.ZACETEK_DATUM <= @DatumDo
            ORDER BY n.PARTNER, n.ZACETEK_DATUM", connection))
        {
            cmd.Parameters.AddWithValue("@DatumOd", datumOd);
            cmd.Parameters.AddWithValue("@DatumDo", datumDo);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var stevilka = reader.GetString(0).Trim();
                var zacetekUra = reader.IsDBNull(4) ? DateTime.MinValue : reader.GetDateTime(4);
                var konecUra = reader.IsDBNull(5) ? DateTime.MinValue : reader.GetDateTime(5);

                var nazivi = new string?[20];
                for (int i = 0; i < 20; i++)
                    nazivi[i] = reader.IsDBNull(8 + i) ? null : reader.GetString(8 + i);

                var opis = string.Join(Environment.NewLine,
                    nazivi.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()));

                // Trajanje: default iz ur
                var trajanje = (int)(konecUra - zacetekUra).TotalMinutes;
                if (trajanje < 0) trajanje += 1440;

                nalogi.Add(new PotrjevanjeNalogDto
                {
                    Stevilka = stevilka,
                    Leto = reader.GetInt32(1),
                    Partner = reader.GetInt32(2),
                    Datum = reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3),
                    ZacetekUra = zacetekUra,
                    KonecUra = konecUra,
                    Trajanje = trajanje,
                    Pregledan = !reader.IsDBNull(6) && reader.GetInt32(6) == 1,
                    Potnik = reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
                    Opis = opis,
                    PolovicnaKilometrina = !reader.IsDBNull(28) && reader.GetInt32(28) == 1,
                    Prodajalna = reader.IsDBNull(29) ? 0 : reader.GetInt32(29),
                    Kilometri = reader.IsDBNull(30) ? null : (double)reader.GetInt32(30)
                });
            }
        }

        if (nalogi.Count == 0) return nalogi;

        // 2. Za naloge, ki se za
        var nalogiZPostavkami = nalogi
            .Where(n => n.Stevilka.Length == 7 && n.Stevilka.StartsWith("1"))
            .ToList();

        if (nalogiZPostavkami.Count > 0)
        {
            var stevilkeIn = string.Join(",", nalogiZPostavkami.Select(n => $"'{n.Stevilka.Replace("'", "''")}'"));
            var letaIn = string.Join(",", nalogiZPostavkami.Select(n => n.Leto).Distinct());

            await using var cmd = new FbCommand($@"
                SELECT STEVILKA, LETO, KOLICINA
                FROM FA_DN_NALOG_KNJ
                WHERE STEVILKA IN ({stevilkeIn}) AND LETO IN ({letaIn})
                  AND TRIM(SIFRA) = '047512'", connection);

            await using var reader = await cmd.ExecuteReaderAsync();
            var trajanjeSlovar = new Dictionary<(string, int), int>();
            while (await reader.ReadAsync())
            {
                var key = (reader.GetString(0).Trim(), reader.GetInt32(1));
                trajanjeSlovar[key] = (int)reader.GetDecimal(2);
            }

            foreach (var n in nalogiZPostavkami)
            {
                if (trajanjeSlovar.TryGetValue((n.Stevilka, n.Leto), out var traj))
                    n.Trajanje = traj;
            }
        }

        // 3. Preberi nazive partnerjev, naslove in pošte
        var partnerSifre = nalogi.Select(n => n.Partner).Distinct().ToList();
        var partnerSifIn = string.Join(",", partnerSifre);

        var partnerji = new Dictionary<int, (string? Naziv, string? Naslov, string? Posta)>();
        await using (var cmd = new FbCommand($"SELECT SIFRA, NAZIV, NASLOV, POSTA FROM PARTNER WHERE SIFRA IN ({partnerSifIn})", connection))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                partnerji[reader.GetInt32(0)] = (
                    reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                    reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                    reader.IsDBNull(3) ? null : reader.GetString(3).Trim()
                );
            }
        }

        foreach (var n in nalogi)
        {
            if (partnerji.TryGetValue(n.Partner, out var p))
            {
                n.NazivPartnerja = p.Naziv;
                n.NaslovPartnerja = p.Naslov;
                n.PostaPartnerja = p.Posta;
            }
        }

        // 4. Preberi nazive potnikov (serviserjev)
        var potnikSifre = nalogi.Where(n => n.Potnik != null).Select(n => n.Potnik!).Distinct().ToList();
        if (potnikSifre.Count > 0)
        {
            var potnikIn = string.Join(",", potnikSifre.Select(s => $"'{s.Replace("'", "''")}'"));
            await using var cmd = new FbCommand($"SELECT SIFRA, PRIIMEK, IME FROM FA_KOMERCIALIST WHERE SIFRA IN ({potnikIn})", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            var potniki = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync())
            {
                var sifra = reader.GetString(0).Trim();
                var priimek = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                var ime = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                potniki[sifra] = string.IsNullOrEmpty(ime) ? priimek : $"{priimek} {ime}".Trim();
            }

            foreach (var n in nalogi.Where(n => n.Potnik != null && potniki.ContainsKey(n.Potnik!)))
                n.NazivPotnika = potniki[n.Potnik!];
        }

        // 4b. Preberi nazive prodajaln
        var prodajalneSifre = nalogi.Where(n => n.Prodajalna > 0).Select(n => n.Prodajalna).Distinct().ToList();
        if (prodajalneSifre.Count > 0)
        {
            var prodajalnaIn = string.Join(",", prodajalneSifre);
            await using var cmd = new FbCommand($"SELECT SIFRA, NAZIV FROM FA_PRODAJALNA WHERE SIFRA IN ({prodajalnaIn})", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            var prodajalne = new Dictionary<int, string>();
            while (await reader.ReadAsync())
                prodajalne[reader.GetInt32(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();

            foreach (var n in nalogi.Where(n => n.Prodajalna > 0 && prodajalne.ContainsKey(n.Prodajalna)))
                n.NazivProdajalne = prodajalne[n.Prodajalna];
        }

        // 5. Preberi pogodbe
        if (partnerSifre.Count > 0)
        {
            var pogodbe = new Dictionary<int, List<string>>();
            await using var cmd = new FbCommand($@"
                SELECT PARTNER, ST_POGODBE
                FROM FA_POGODBE
                WHERE PARTNER IN ({partnerSifIn})
                  AND VELJA_DO >= @DatumOd
                ORDER BY PARTNER, STEVILKA", connection);
            cmd.Parameters.AddWithValue("@DatumOd", datumOd);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var partner = reader.GetInt32(0);
                var stPog = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
                if (!string.IsNullOrWhiteSpace(stPog))
                {
                    if (!pogodbe.ContainsKey(partner))
                        pogodbe[partner] = new();
                    if (!pogodbe[partner].Contains(stPog))
                        pogodbe[partner].Add(stPog);
                }
            }

            foreach (var n in nalogi)
            {
                if (pogodbe.TryGetValue(n.Partner, out var pog))
                    n.Pogodbe = string.Join(", ", pog);
            }
        }

        // 6. Preberi KAJ_OBRACUNAM iz OBRACUN_DN
        {
            var stevilkeIn = string.Join(",", nalogi.Select(n => $"'{n.Stevilka.Replace("'", "''")}'"));
            var letaIn = string.Join(",", nalogi.Select(n => n.Leto).Distinct());
            await using var cmd = new FbCommand($@"
                SELECT STEVILKA, LETO, KAJ_OBRACUNAM
                FROM OBRACUN_DN
                WHERE STEVILKA IN ({stevilkeIn}) AND LETO IN ({letaIn})", connection);

            await using var reader = await cmd.ExecuteReaderAsync();
            var obracunDn = new Dictionary<(string, int), KajObracunam>();
            while (await reader.ReadAsync())
            {
                var key = (reader.GetString(0).Trim(), reader.GetInt32(1));
                obracunDn[key] = (KajObracunam)reader.GetInt32(2);
            }

            foreach (var n in nalogi)
            {
                if (obracunDn.TryGetValue((n.Stevilka, n.Leto), out var kaj))
                    n.KajObracunam = kaj;
            }
        }

        return nalogi;
    }

    /// <summary>
    /// Pridobi postavke delovnega naloga (FA_DN_NALOG_KNJ) + ročne vnose iz OBRACUN_OSNUTEK_POS.
    /// </summary>
    public async Task<List<PotrjevanjeNalogPozDto>> GetPostavkeNalogaAsync(string stevilka, int leto)
    {
        var result = new List<PotrjevanjeNalogPozDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // 1. Postavke iz FA_DN_NALOG_KNJ
        var postavke = new List<(int Zs, string? Sifra, decimal Kolicina, decimal Cena, decimal Rabat)>();
        await using (var cmd = new FbCommand(@"
            SELECT ZS, SIFRA, KOLICINA, CENA, RABAT1
            FROM FA_DN_NALOG_KNJ
            WHERE STEVILKA = @Stevilka AND LETO = @Leto
            ORDER BY ZS", connection))
        {
            cmd.Parameters.AddWithValue("@Stevilka", stevilka);
            cmd.Parameters.AddWithValue("@Leto", leto);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                postavke.Add((
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                    reader.IsDBNull(2) ? 0 : reader.GetDecimal(2),
                    reader.IsDBNull(3) ? 0 : reader.GetDecimal(3),
                    reader.IsDBNull(4) ? 0 : reader.GetDecimal(4)
                ));
            }
        }

        // 2. Ročni vnosi iz OBRACUN_OSNUTEK_POS (vezani na ta nalog)
        var rocniPostavke = new List<(int Zs, string? Sifra, decimal Kolicina, decimal Cena, decimal Rabat, int Mesec, int Leto, int Partner, int RealZs)>();
        await using (var cmd = new FbCommand(@"
            SELECT ZS, ARTIKEL, COALESCE(KOLICINA, 0), COALESCE(CENA, 0), COALESCE(RABAT, 0), MESEC, LETO, PARTNER
            FROM OBRACUN_OSNUTEK_POS
            WHERE NALOG_STEVILKA = @Stevilka AND NALOG_LETO = @Leto
              AND TIP_POSTAVKE = 1
            ORDER BY ZS", connection))
        {
            cmd.Parameters.AddWithValue("@Stevilka", stevilka);
            cmd.Parameters.AddWithValue("@Leto", leto);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var realZs = reader.GetInt32(0);
                rocniPostavke.Add((
                    realZs + 10000,
                    reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                    reader.GetDecimal(2),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.GetInt32(7),
                    realZs
                ));
            }
        }

        // 3. Nazivi artiklov (za obe skupini)
        var vseSifre = postavke.Select(p => p.Sifra).Concat(rocniPostavke.Select(p => p.Sifra))
            .Where(s => s != null)
            .Select(s => s!)
            .Distinct()
            .ToList();
        var artikli = new Dictionary<string, (string Naziv, string? Enota)>(StringComparer.OrdinalIgnoreCase);
        if (vseSifre.Count > 0)
        {
            var sifIn = string.Join(",", vseSifre.Select(s => $"'{s.Replace("'", "''")}'"));
            await using var cmd = new FbCommand($"SELECT SIFRA, NAZIV, NAZIV2, ENOTA FROM FA_ARTIKEL WHERE SIFRA IN ({sifIn})", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var s = reader.GetString(0).Trim();
                var naziv = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                var naziv2 = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                var enota = reader.IsDBNull(3) ? null : reader.GetString(3).Trim();
                artikli[s] = (ArtikelHelper.GetFullName(naziv, naziv2), enota);
            }
        }

        // 4. Združi vse postavke
        foreach (var p in postavke)
        {
            var dto = new PotrjevanjeNalogPozDto
            {
                Zs = p.Zs,
                Sifra = p.Sifra,
                Kolicina = p.Kolicina,
                Cena = p.Cena,
                Rabat = p.Rabat
            };
            if (p.Sifra != null && artikli.TryGetValue(p.Sifra, out var a))
            {
                dto.NazivArtikla = a.Naziv;
                dto.Enota = a.Enota;
            }
            result.Add(dto);
        }

        foreach (var p in rocniPostavke)
        {
            var dto = new PotrjevanjeNalogPozDto
            {
                Zs = p.Zs,
                Sifra = p.Sifra,
                Kolicina = p.Kolicina,
                Cena = p.Cena,
                Rabat = p.Rabat,
                JeRocni = true,
                RocniMesec = p.Mesec,
                RocniLeto = p.Leto,
                RocniPartner = p.Partner,
                RocniZs = p.RealZs
            };
            if (p.Sifra != null && artikli.TryGetValue(p.Sifra, out var a))
            {
                dto.NazivArtikla = a.Naziv;
                dto.Enota = a.Enota;
            }
            result.Add(dto);
        }

        return result;
    }

    /// <summary>
    /// Pridobi predračune za partnerja (status 5 ali plačani).
    /// </summary>
    public async Task<List<PredracunGridDto>> GetPredracuniZaPartnerjaAsync(int partner, int? predMesec = null, int? predLeto = null)
    {
        var result = new List<PredracunGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // 1. Predračuni z vsoto plačil
        await using (var cmd = new FbCommand(@"
            SELECT 
                p.STEVILKA, p.LETO, p.DATUM, p.STANJE, p.ZNESEK_KONCNI,
                pl.VSOTA_PLACIL
            FROM FA_PREDRACUN p
            LEFT JOIN (
                SELECT PREDRACUN_STEVILKA, PREDRACUN_LETO, SUM(ZNESEK + COALESCE(SCONTO, 0)) AS VSOTA_PLACIL
                FROM FA_RACUN_PLACILO
                WHERE PREDRACUN_STEVILKA IS NOT NULL AND PREDRACUN_LETO IS NOT NULL
                GROUP BY PREDRACUN_STEVILKA, PREDRACUN_LETO
                HAVING SUM(ZNESEK + COALESCE(SCONTO, 0)) > 0
            ) pl ON p.STEVILKA = pl.PREDRACUN_STEVILKA AND p.LETO = pl.PREDRACUN_LETO
            WHERE p.SIFRA_KUPCA = @Partner AND p.DATUM >= '2026-01-01'
              AND (p.STANJE IN (2, 5) OR pl.VSOTA_PLACIL IS NOT NULL)
            ORDER BY p.LETO DESC, p.STEVILKA DESC", connection))
        {
            cmd.Parameters.AddWithValue("@Partner", partner);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                result.Add(new PredracunGridDto
                {
                    Stevilka = reader.GetString(0).Trim(),
                    Leto = reader.GetInt32(1),
                    Datum = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                    Stanje = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    ZnesekKoncni = reader.IsDBNull(4) ? 0m : reader.GetDecimal(4),
                    SifraKupca = partner,
                    Placano = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5),
                    PlacanoIzRacunov = 0
                });
            }
        }

        if (result.Count == 0) return result;

        // 2. Računi vezani na predračune tega partnerja
        var racunZneski = new Dictionary<string, decimal>();
        var racunObstaja = new HashSet<string>();
        var racunStevilke = new Dictionary<string, HashSet<string>>();

        await using (var cmd = new FbCommand(@"
            SELECT 
                PREDRAC1_STEVILKA, PREDRAC1_LETO, COALESCE(PREDRAC1_ZNESEK, 0),
                PREDRAC2_STEVILKA, PREDRAC2_LETO, COALESCE(PREDRAC2_ZNESEK, 0),
                POVEZAVA_STEVILKA, POVEZAVA_LETO,
                STEVILKA
            FROM FA_RACUN
            WHERE SIFRA_KUPCA = @Partner
              AND ((PREDRAC1_STEVILKA IS NOT NULL)
                OR (PREDRAC2_STEVILKA IS NOT NULL)
                OR (POVEZAVA_STEVILKA IS NOT NULL))", connection))
        {
            cmd.Parameters.AddWithValue("@Partner", partner);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var racunSt = reader.GetInt32(8).ToString();

                // PREDRAC1
                if (!reader.IsDBNull(0) && !reader.IsDBNull(1))
                {
                    var key = $"{reader.GetString(0).Trim()}_{reader.GetInt32(1)}";
                    racunZneski[key] = racunZneski.GetValueOrDefault(key) + reader.GetDecimal(2);
                    racunObstaja.Add(key);
                    if (!racunStevilke.TryGetValue(key, out var set1))
                        racunStevilke[key] = set1 = new HashSet<string>();
                    set1.Add(racunSt);
                }
                // PREDRAC2
                if (!reader.IsDBNull(3) && !reader.IsDBNull(4))
                {
                    var key = $"{reader.GetString(3).Trim()}_{reader.GetInt32(4)}";
                    racunZneski[key] = racunZneski.GetValueOrDefault(key) + reader.GetDecimal(5);
                    racunObstaja.Add(key);
                    if (!racunStevilke.TryGetValue(key, out var set2))
                        racunStevilke[key] = set2 = new HashSet<string>();
                    set2.Add(racunSt);
                }
                // POVEZAVA
                if (!reader.IsDBNull(6) && !reader.IsDBNull(7))
                {
                    var key = $"{reader.GetString(6).Trim()}_{reader.GetInt32(7)}";
                    racunObstaja.Add(key);
                    if (!racunStevilke.TryGetValue(key, out var set3))
                        racunStevilke[key] = set3 = new HashSet<string>();
                    set3.Add(racunSt);
                }
            }
        }

        // 3. Združi podatke
        foreach (var predracun in result)
        {
            var key = $"{predracun.Stevilka}_{predracun.Leto}";
            if (racunZneski.TryGetValue(key, out var znesek))
                predracun.PlacanoIzRacunov = znesek;
            else if (racunObstaja.Contains(key))
                predracun.PlacanoIzRacunov = predracun.ZnesekKoncni;

            if (racunStevilke.TryGetValue(key, out var stevilke))
                predracun.PovezaniRacuni = string.Join(", ", stevilke.OrderBy(s => s));
        }

        // 4. Minute iz predračunov (postavke × OBRACUN_PAKET_MINUTE)
        // Najprej preberi artikel?minut slovar (malo zapisov)
        var paketMinute = new Dictionary<string, int>();
        await using (var cmd = new FbCommand("SELECT TRIM(ARTIKEL), MINUT FROM OBRACUN_PAKET_MINUTE", connection))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                paketMinute[reader.GetString(0)] = reader.GetInt32(1);
        }

        var minuteSlovar = new Dictionary<string, int>();
        if (paketMinute.Count > 0)
        {
            var stevilkeIn = string.Join(",", result.Select(r => $"'{r.Stevilka.Replace("'", "''")}'"));
            var letaIn = string.Join(",", result.Select(r => r.Leto).Distinct());
            var sifraIn = string.Join(",", paketMinute.Keys.Select(s => $"'{s.Replace("'", "''")}'"));

            await using (var cmd = new FbCommand($@"
                SELECT STEVILKA, LETO, TRIM(SIFRA), CAST(KOLICINA AS INTEGER)
                FROM FA_PREDRACUN_KNJIZBA
                WHERE STEVILKA IN ({stevilkeIn}) AND LETO IN ({letaIn})
                  AND TRIM(SIFRA) IN ({sifraIn})", connection))
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var mKey = $"{reader.GetString(0).Trim()}_{reader.GetInt32(1)}";
                    var sifra = reader.GetString(2);
                    var kolicina = reader.GetInt32(3);
                    if (paketMinute.TryGetValue(sifra, out var minutNaArtikel))
                    {
                        minuteSlovar.TryGetValue(mKey, out var existing);
                        minuteSlovar[mKey] = existing + kolicina * minutNaArtikel;
                    }
                }
            }
        }

        foreach (var predracun in result)
        {
            var mKey = $"{predracun.Stevilka}_{predracun.Leto}";
            if (minuteSlovar.TryGetValue(mKey, out var min))
                predracun.Minute = min;
            predracun.MinutePreostalo = predracun.Minute;
        }

        // 5. Poraba minut iz OBRACUN_PORABA_MINUT (pred mesec/leto)
        if (predMesec != null && predLeto != null)
        {
            var porabaSlovar = new Dictionary<string, int>();
            await using (var cmd = new FbCommand(@"
                SELECT PREDRACUN_STEVILKA, PREDRACUN_LETO, SUM(KOLICINA)
                FROM OBRACUN_PORABA_MINUT
                WHERE TIP = 1 AND PARTNER = @Partner
                  AND (LETO < @PredLeto OR (LETO = @PredLeto AND MESEC < @PredMesec))
                GROUP BY PREDRACUN_STEVILKA, PREDRACUN_LETO", connection))
            {
                cmd.Parameters.AddWithValue("@Partner", partner);
                cmd.Parameters.AddWithValue("@PredLeto", predLeto.Value);
                cmd.Parameters.AddWithValue("@PredMesec", predMesec.Value);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var prSt = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                    var prLeto = reader.GetInt32(1);
                    porabaSlovar[$"{prSt}_{prLeto}"] = reader.GetInt32(2);
                }
            }

            foreach (var predracun in result)
            {
                var pKey = $"{predracun.Stevilka}_{predracun.Leto}";
                var porabljeno = 0;
                if (porabaSlovar.TryGetValue(pKey, out var p1))
                    porabljeno = p1;

                predracun.MinutePreostalo = Math.Max(0, predracun.Minute - porabljeno);
            }
        }

        return result;
    }

    /// <summary>
    /// Pridobi postavke predračuna (FA_PREDRACUN_KNJIZBA) z nazivi artiklov.
    /// </summary>
    public async Task<List<PredracunKnjizbaGridDto>> GetPostavkePredracunaAsync(string stevilka, int leto)
    {
        var result = new List<PredracunKnjizbaGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // Preberi paket minute slovar
        var paketMinute = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using (var cmdPm = new FbCommand("SELECT TRIM(ARTIKEL), MINUT FROM OBRACUN_PAKET_MINUTE", connection))
        {
            await using var rdrPm = await cmdPm.ExecuteReaderAsync();
            while (await rdrPm.ReadAsync())
                paketMinute[rdrPm.GetString(0)] = rdrPm.GetInt32(1);
        }

        await using var cmd = new FbCommand(@"
            SELECT k.ZS, k.SIFRA, k.KOLICINA, k.PRODAJNA_CENA, k.PRODAJNA_VREDNOST, k.RABAT1,
                   a.NAZIV, a.NAZIV2
            FROM FA_PREDRACUN_KNJIZBA k
            LEFT JOIN FA_ARTIKEL a ON k.SIFRA = a.SIFRA
            WHERE k.STEVILKA = @Stevilka AND k.LETO = @Leto
            ORDER BY k.ZS", connection);

        cmd.Parameters.AddWithValue("@Stevilka", stevilka);
        cmd.Parameters.AddWithValue("@Leto", leto);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var naziv = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim();
            var naziv2 = reader.IsDBNull(7) ? "" : reader.GetString(7).Trim();
            var sifra = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
            var kolicina = reader.IsDBNull(2) ? (decimal?)null : reader.GetDecimal(2);

            int? minute = null;
            if (sifra != null && kolicina.HasValue && paketMinute.TryGetValue(sifra, out var minutNaArtikel))
                minute = (int)kolicina.Value * minutNaArtikel;

            result.Add(new PredracunKnjizbaGridDto
            {
                Stevilka = stevilka,
                Leto = leto,
                Zs = reader.GetInt32(0),
                SifraArtikla = sifra,
                NazivArtikla = ArtikelHelper.GetFullName(naziv, naziv2),
                Kolicina = kolicina,
                ProdajnaCena = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                ProdajnaVrednost = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                Rabat1 = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                Minute = minute
            });
        }

        return result;
    }

    /// <summary>
    /// Shrani polovično kilometrino (SIF29) v FA_DN_NALOG.
    /// </summary>
    public async Task SavePolovicnaKilometrinaAsync(string stevilka, int leto, bool polovicna)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            UPDATE FA_DN_NALOG SET SIF29 = @Sif29
            WHERE STEVILKA = @Stevilka AND LETO = @Leto", connection);

        cmd.Parameters.AddWithValue("@Sif29", polovicna ? 1 : 0);
        cmd.Parameters.AddWithValue("@Stevilka", stevilka);
        cmd.Parameters.AddWithValue("@Leto", leto);

        await cmd.ExecuteNonQueryAsync();

        await ZapisiRevizijo(connection, TrenutniUporabnik,
            "FA_DN_NALOG", "SIF29",
            polovicna ? "0" : "1", polovicna ? "1" : "0",
            $"Nalog {stevilka}/{leto}", stevilka, leto);
    }

    /// <summary>
    /// Shrani KAJ_OBRACUNAM v OBRACUN_DN (UPDATE OR INSERT).
    /// </summary>
    public async Task SaveKajObracunamAsync(string stevilka, int leto, KajObracunam staraVrednost, KajObracunam novaVrednost)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            UPDATE OR INSERT INTO OBRACUN_DN (STEVILKA, LETO, KAJ_OBRACUNAM)
            VALUES (@Stevilka, @Leto, @Kaj)
            MATCHING (STEVILKA, LETO)", connection);

        cmd.Parameters.AddWithValue("@Stevilka", stevilka);
        cmd.Parameters.AddWithValue("@Leto", leto);
        cmd.Parameters.AddWithValue("@Kaj", (int)novaVrednost);

        await cmd.ExecuteNonQueryAsync();

        await ZapisiRevizijo(connection, TrenutniUporabnik,
            "OBRACUN_DN", "KAJ_OBRACUNAM",
            staraVrednost.ToText(), novaVrednost.ToText(),
            $"Nalog {stevilka}/{leto}", stevilka, leto);
    }

    /// <summary>
    /// Preveri in ustvari manjkajoče OBRACUN_DN zapise za vse naloge od 1.MM.LLLL naprej.
    /// Za vsak nalog, ki nima zapisa v OBRACUN_DN, ustvari nov zapis z vrednostjo KajObracunam
    /// določeno iz SIF28 (0 = KmMin, 1 = Nič, ostalo = Nedefinirano).
    /// </summary>
    public async Task<int> UstvariManjkajoceObracunDnAsync(int leto, int mesec)
    {
        var datumOd = new DateTime(leto, mesec, 1);
        var datumDo = datumOd.AddMonths(1).AddDays(-1);

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // 1. Preberi naloge brez OBRACUN_DN zapisa (samo za izbrani mesec)
        var manjkajoci = new List<(string Stevilka, int Leto, int Sif28)>();
        await using (var cmd = new FbCommand(@"
            SELECT n.STEVILKA, n.LETO, n.SIF28
            FROM FA_DN_NALOG n
            LEFT JOIN OBRACUN_DN d ON n.STEVILKA = d.STEVILKA AND n.LETO = d.LETO
            WHERE n.ZACETEK_DATUM >= @DatumOd AND n.ZACETEK_DATUM <= @DatumDo
              AND d.STEVILKA IS NULL", connection))
        {
            cmd.Parameters.AddWithValue("@DatumOd", datumOd);
            cmd.Parameters.AddWithValue("@DatumDo", datumDo);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                manjkajoci.Add((
                    reader.GetString(0).Trim(),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? -1 : reader.GetInt32(2)
                ));
            }
        }

        if (manjkajoci.Count == 0) return 0;

        // 2. Vstavi manjkajoce zapise v eni transakciji s pripravljenim ukazom
        int ustvarjenih = 0;
        using var transaction = connection.BeginTransaction();
        try
        {
            using var cmd = new FbCommand(@"
                INSERT INTO OBRACUN_DN (STEVILKA, LETO, KAJ_OBRACUNAM, MINUTE_KI_SE_NE_OBRACUNAJO)
                VALUES (@Stevilka, @Leto, @Kaj, 0)", connection, transaction);

            var pStevilka = cmd.Parameters.Add("@Stevilka", FbDbType.VarChar);
            var pLeto = cmd.Parameters.Add("@Leto", FbDbType.Integer);
            var pKaj = cmd.Parameters.Add("@Kaj", FbDbType.Integer);
            cmd.Prepare();

            foreach (var nalog in manjkajoci)
            {
                var kajObracunam = nalog.Sif28 switch
                {
                    0 => KajObracunam.KmMin,
                    1 => KajObracunam.Nic,
                    _ => KajObracunam.Nedefinirano
                };

                pStevilka.Value = nalog.Stevilka;
                pLeto.Value = nalog.Leto;
                pKaj.Value = (int)kajObracunam;

                await cmd.ExecuteNonQueryAsync();
                ustvarjenih++;
            }

            transaction.Commit();
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }

        return ustvarjenih;
    }


    /// <summary>
    /// Potrdi nalog (SIF27 = 1) in zapiši revizijo.
    /// </summary>
    public async Task PotrdiNalogAsync(string stevilka, int leto)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            UPDATE FA_DN_NALOG SET SIF27 = 1
            WHERE STEVILKA = @Stevilka AND LETO = @Leto", connection);

        cmd.Parameters.AddWithValue("@Stevilka", stevilka);
        cmd.Parameters.AddWithValue("@Leto", leto);

        await cmd.ExecuteNonQueryAsync();

        await ZapisiRevizijo(connection, TrenutniUporabnik,
            "FA_DN_NALOG", "SIF27",
            "0", "1",
            $"Nalog {stevilka}/{leto}", stevilka, leto);
    }

    /// <summary>
    /// Pobri
    /// </summary>
    public async Task PobrisiPotrditevNalogaAsync(string stevilka, int leto)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            UPDATE FA_DN_NALOG SET SIF27 = 0
            WHERE STEVILKA = @Stevilka AND LETO = @Leto", connection);

        cmd.Parameters.AddWithValue("@Stevilka", stevilka);
        cmd.Parameters.AddWithValue("@Leto", leto);

        await cmd.ExecuteNonQueryAsync();

        await ZapisiRevizijo(connection, TrenutniUporabnik,
            "FA_DN_NALOG", "SIF27",
            "1", "0",
            $"Nalog {stevilka}/{leto}", stevilka, leto);
    }

    /// <summary>
    /// Shrani ro
    /// ZS se določi kot MAX(ZS)+1 za dani mesec/leto/partner.
    /// </summary>
    public async Task SaveRocniArtikelAsync(int leto, int mesec, int partner,
        string sifraArtikla, string nazivArtikla, decimal kolicina, decimal cena, decimal rabat,
        string? nalogStevilka, int? nalogLeto)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // Določi naslednji ZS
        int naslednjZs = 1;
        await using (var cmd = new FbCommand(@"
            SELECT MAX(ZS) FROM OBRACUN_OSNUTEK_POS
            WHERE LETO = @Leto AND MESEC = @Mesec AND PARTNER = @Partner", connection))
        {
            cmd.Parameters.AddWithValue("@Leto", leto);
            cmd.Parameters.AddWithValue("@Mesec", mesec);
            cmd.Parameters.AddWithValue("@Partner", partner);
            var obj = await cmd.ExecuteScalarAsync();
            if (obj != null && obj != DBNull.Value)
                naslednjZs = Convert.ToInt32(obj) + 1;
        }

        await using (var cmd = new FbCommand(@"
            INSERT INTO OBRACUN_OSNUTEK_POS (MESEC, LETO, PARTNER, ZS, ARTIKEL, NAZIV, KOLICINA, CENA, RABAT, TIP_POSTAVKE, NALOG_STEVILKA, NALOG_LETO, KDO, KDAJ)
            VALUES (@Mesec, @Leto, @Partner, @Zs, @Artikel, @Naziv, @Kolicina, @Cena, @Rabat, @TipPostavke, @NalogStevilka, @NalogLeto, @Kdo, @Kdaj)", connection))
        {
            cmd.Parameters.AddWithValue("@Mesec", mesec);
            cmd.Parameters.AddWithValue("@Leto", leto);
            cmd.Parameters.AddWithValue("@Partner", partner);
            cmd.Parameters.AddWithValue("@Zs", naslednjZs);
            cmd.Parameters.AddWithValue("@Artikel", sifraArtikla);
            cmd.Parameters.AddWithValue("@Naziv", nazivArtikla.Length > 40 ? nazivArtikla.Substring(0, 40) : nazivArtikla);
            cmd.Parameters.AddWithValue("@Kolicina", kolicina);
            cmd.Parameters.AddWithValue("@Cena", cena);
            cmd.Parameters.AddWithValue("@Rabat", rabat);
            cmd.Parameters.AddWithValue("@TipPostavke", (int)TipPostavke.ROCNI);
            cmd.Parameters.AddWithValue("@NalogStevilka", (object?)nalogStevilka ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@NalogLeto", (object?)nalogLeto ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Kdo", TrenutniUporabnik);
            cmd.Parameters.AddWithValue("@Kdaj", DateTime.Now);

            await cmd.ExecuteNonQueryAsync();
        }

        await ZapisiRevizijo(connection, TrenutniUporabnik,
            "OBRACUN_OSNUTEK_POS", "ROCNI_VNOS",
            null, $"{sifraArtikla} kol={kolicina} cena={cena} rabat={rabat}",
            $"Partner {partner}, {mesec}/{leto}");
    }

    /// <summary>
    /// Izbriši ročno vneseno postavko iz OBRACUN_OSNUTEK_POS.
    /// </summary>
    public async Task DeleteRocniArtikelAsync(int mesec, int leto, int partner, int zs)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // Preberi podatke za revizijo
        string? artikel = null;
        await using (var cmd = new FbCommand(@"
            SELECT ARTIKEL, KOLICINA, CENA, RABAT
            FROM OBRACUN_OSNUTEK_POS
            WHERE MESEC = @Mesec AND LETO = @Leto AND PARTNER = @Partner AND ZS = @Zs
              AND TIP_POSTAVKE = 1", connection))
        {
            cmd.Parameters.AddWithValue("@Mesec", mesec);
            cmd.Parameters.AddWithValue("@Leto", leto);
            cmd.Parameters.AddWithValue("@Partner", partner);
            cmd.Parameters.AddWithValue("@Zs", zs);
            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
                artikel = $"{(reader.IsDBNull(0) ? "?" : reader.GetString(0).Trim())} kol={reader.GetDecimal(1)} cena={reader.GetDecimal(2)} rabat={reader.GetDecimal(3)}";
        }

        await using (var cmd = new FbCommand(@"
            DELETE FROM OBRACUN_OSNUTEK_POS
            WHERE MESEC = @Mesec AND LETO = @Leto AND PARTNER = @Partner AND ZS = @Zs
              AND TIP_POSTAVKE = 1", connection))
        {
            cmd.Parameters.AddWithValue("@Mesec", mesec);
            cmd.Parameters.AddWithValue("@Leto", leto);
            cmd.Parameters.AddWithValue("@Partner", partner);
            cmd.Parameters.AddWithValue("@Zs", zs);
            await cmd.ExecuteNonQueryAsync();
        }

        await ZapisiRevizijo(connection, TrenutniUporabnik,
            "OBRACUN_OSNUTEK_POS", "ROCNI_BRISANJE",
            artikel, null,
            $"Partner {partner}, {mesec}/{leto}");
    }

    /// <summary>
    /// Pridobi statistiko nalogov (skupaj, potrjeni, nepotrjeni, minute) za dva meseca.
    /// </summary>
    public async Task<List<NalogStatistikaDto>> GetNalogStatistikaAsync()
    {
        var danes = DateTime.Today;
        var tekociMesec = new DateTime(danes.Year, danes.Month, 1);
        var pretekliMesec = tekociMesec.AddMonths(-1);

        var result = new List<NalogStatistikaDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT
                EXTRACT(MONTH FROM n.ZACETEK_DATUM) AS MES,
                EXTRACT(YEAR FROM n.ZACETEK_DATUM) AS LET,
                COUNT(*) AS SKUPAJ,
                SUM(CASE WHEN n.SIF27 = 1 THEN 1 ELSE 0 END) AS POTRJENI,
                SUM(CASE WHEN n.SIF27 IS NULL OR n.SIF27 = 0 THEN 1 ELSE 0 END) AS NEPOTRJENI
            FROM FA_DN_NALOG n
            WHERE n.ZACETEK_DATUM >= @DatumOd
            GROUP BY EXTRACT(MONTH FROM n.ZACETEK_DATUM), EXTRACT(YEAR FROM n.ZACETEK_DATUM)
            ORDER BY LET, MES", connection);

        cmd.Parameters.AddWithValue("@DatumOd", pretekliMesec);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new NalogStatistikaDto
            {
                Mesec = reader.GetInt32(0),
                Leto = reader.GetInt32(1),
                Skupaj = reader.GetInt32(2),
                Potrjeni = reader.GetInt32(3),
                Nepotrjeni = reader.GetInt32(4)
            });
        }

        return result;
    }

    // ==================== Pregled nalogov ====================

    /// <summary>
    /// Pridobi naloge za stran Pregled nalogov (po datumskem razponu).
    /// </summary>
    public async Task<List<PotrjevanjeNalogDto>> GetNalogiZaPregledAsync(DateTime datumOd, DateTime datumDo)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        var nalogi = new List<PotrjevanjeNalogDto>();
        await using (var cmd = new FbCommand(@"
            SELECT n.STEVILKA, n.LETO, n.PARTNER, n.ZACETEK_DATUM, n.ZACETEK_URA, n.KONEC_URA, n.SIF27, n.POTNIK,
                   n.NAZIV1, n.NAZIV2, n.NAZIV3, n.NAZIV4, n.NAZIV5,
                   n.NAZIV6, n.NAZIV7, n.NAZIV8, n.NAZIV9, n.NAZIV10,
                   n.NAZIV11, n.NAZIV12, n.NAZIV13, n.NAZIV14, n.NAZIV15,
                   n.NAZIV16, n.NAZIV17, n.NAZIV18, n.NAZIV19, n.NAZIV20,
                   n.SIF29, n.PRODAJALNA, n.SIF30
            FROM FA_DN_NALOG n
            WHERE n.ZACETEK_DATUM >= @DatumOd AND n.ZACETEK_DATUM <= @DatumDo
            ORDER BY n.PARTNER, n.ZACETEK_DATUM", connection))
        {
            cmd.Parameters.AddWithValue("@DatumOd", datumOd);
            cmd.Parameters.AddWithValue("@DatumDo", datumDo);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var stevilka = reader.GetString(0).Trim();
                var zacetekUra = reader.IsDBNull(4) ? DateTime.MinValue : reader.GetDateTime(4);
                var konecUra = reader.IsDBNull(5) ? DateTime.MinValue : reader.GetDateTime(5);

                var nazivi = new string?[20];
                for (int i = 0; i < 20; i++)
                    nazivi[i] = reader.IsDBNull(8 + i) ? null : reader.GetString(8 + i);

                var opis = string.Join(Environment.NewLine,
                    nazivi.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()));

                var trajanje = (int)(konecUra - zacetekUra).TotalMinutes;
                if (trajanje < 0) trajanje += 1440;

                nalogi.Add(new PotrjevanjeNalogDto
                {
                    Stevilka = stevilka,
                    Leto = reader.GetInt32(1),
                    Partner = reader.GetInt32(2),
                    Datum = reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3),
                    ZacetekUra = zacetekUra,
                    KonecUra = konecUra,
                    Trajanje = trajanje,
                    Pregledan = !reader.IsDBNull(6) && reader.GetInt32(6) == 1,
                    Potnik = reader.IsDBNull(7) ? null : reader.GetString(7).Trim(),
                    Opis = opis,
                    PolovicnaKilometrina = !reader.IsDBNull(28) && reader.GetInt32(28) == 1,
                    Prodajalna = reader.IsDBNull(29) ? 0 : reader.GetInt32(29),
                    Kilometri = reader.IsDBNull(30) ? null : (double)reader.GetInt32(30)
                });
            }
        }

        if (nalogi.Count == 0) return nalogi;

        // Trajanje za helpdesk naloge
        var nalogiZPostavkami = nalogi
            .Where(n => n.Stevilka.Length == 7 && n.Stevilka.StartsWith("1"))
            .ToList();

        if (nalogiZPostavkami.Count > 0)
        {
            var stevilkeIn = string.Join(",", nalogiZPostavkami.Select(n => $"'{n.Stevilka.Replace("'", "''")}'"));
            var letaIn = string.Join(",", nalogiZPostavkami.Select(n => n.Leto).Distinct());

            await using var cmd = new FbCommand($@"
                SELECT STEVILKA, LETO, KOLICINA
                FROM FA_DN_NALOG_KNJ
                WHERE STEVILKA IN ({stevilkeIn}) AND LETO IN ({letaIn})
                  AND TRIM(SIFRA) = '047512'", connection);

            await using var reader = await cmd.ExecuteReaderAsync();
            var trajanjeSlovar = new Dictionary<(string, int), int>();
            while (await reader.ReadAsync())
            {
                var key = (reader.GetString(0).Trim(), reader.GetInt32(1));
                trajanjeSlovar[key] = (int)reader.GetDecimal(2);
            }

            foreach (var n in nalogiZPostavkami)
            {
                if (trajanjeSlovar.TryGetValue((n.Stevilka, n.Leto), out var traj))
                    n.Trajanje = traj;
            }
        }

        // Nazivi partnerjev, naslovi, pošte
        var partnerSifre = nalogi.Select(n => n.Partner).Distinct().ToList();
        var partnerSifIn = string.Join(",", partnerSifre);

        var partnerji = new Dictionary<int, (string? Naziv, string? Naslov, string? Posta)>();
        await using (var cmd = new FbCommand($"SELECT SIFRA, NAZIV, NASLOV, POSTA FROM PARTNER WHERE SIFRA IN ({partnerSifIn})", connection))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                partnerji[reader.GetInt32(0)] = (
                    reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                    reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                    reader.IsDBNull(3) ? null : reader.GetString(3).Trim()
                );
            }
        }

        foreach (var n in nalogi)
        {
            if (partnerji.TryGetValue(n.Partner, out var p))
            {
                n.NazivPartnerja = p.Naziv;
                n.NaslovPartnerja = p.Naslov;
                n.PostaPartnerja = p.Posta;
            }
        }

        // Nazivi potnikov
        var potnikSifre = nalogi.Where(n => n.Potnik != null).Select(n => n.Potnik!).Distinct().ToList();
        if (potnikSifre.Count > 0)
        {
            var potnikIn = string.Join(",", potnikSifre.Select(s => $"'{s.Replace("'", "''")}'"));
            await using var cmd = new FbCommand($"SELECT SIFRA, PRIIMEK, IME FROM FA_KOMERCIALIST WHERE SIFRA IN ({potnikIn})", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            var potniki = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync())
            {
                var sifra = reader.GetString(0).Trim();
                var priimek = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                var ime = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                potniki[sifra] = string.IsNullOrEmpty(ime) ? priimek : $"{priimek} {ime}".Trim();
            }

            foreach (var n in nalogi.Where(n => n.Potnik != null && potniki.ContainsKey(n.Potnik!)))
                n.NazivPotnika = potniki[n.Potnik!];
        }

        // Nazivi prodajaln
        var prodajalneSifre = nalogi.Where(n => n.Prodajalna > 0).Select(n => n.Prodajalna).Distinct().ToList();
        if (prodajalneSifre.Count > 0)
        {
            var prodajalnaIn = string.Join(",", prodajalneSifre);
            await using var cmd = new FbCommand($"SELECT SIFRA, NAZIV FROM FA_PRODAJALNA WHERE SIFRA IN ({prodajalnaIn})", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            var prodajalne = new Dictionary<int, string>();
            while (await reader.ReadAsync())
                prodajalne[reader.GetInt32(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();

            foreach (var n in nalogi.Where(n => n.Prodajalna > 0 && prodajalne.ContainsKey(n.Prodajalna)))
                n.NazivProdajalne = prodajalne[n.Prodajalna];
        }

        // Pogodbe
        if (partnerSifre.Count > 0)
        {
            var pogodbe = new Dictionary<int, List<string>>();
            await using var cmd = new FbCommand($@"
                SELECT PARTNER, ST_POGODBE
                FROM FA_POGODBE
                WHERE PARTNER IN ({partnerSifIn})
                  AND VELJA_DO >= @DatumOd2
                ORDER BY PARTNER, STEVILKA", connection);
            cmd.Parameters.AddWithValue("@DatumOd2", datumOd);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var partner = reader.GetInt32(0);
                var stPog = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
                if (!string.IsNullOrWhiteSpace(stPog))
                {
                    if (!pogodbe.ContainsKey(partner))
                        pogodbe[partner] = new();
                    if (!pogodbe[partner].Contains(stPog))
                        pogodbe[partner].Add(stPog);
                }
            }

            foreach (var n in nalogi)
            {
                if (pogodbe.TryGetValue(n.Partner, out var pog))
                    n.Pogodbe = string.Join(", ", pog);
            }
        }

        // KAJ_OBRACUNAM iz OBRACUN_DN
        {
            var stevilkeIn = string.Join(",", nalogi.Select(n => $"'{n.Stevilka.Replace("'", "''")}'"));
            var letaIn = string.Join(",", nalogi.Select(n => n.Leto).Distinct());
            await using var cmd = new FbCommand($@"
                SELECT STEVILKA, LETO, KAJ_OBRACUNAM
                FROM OBRACUN_DN
                WHERE STEVILKA IN ({stevilkeIn}) AND LETO IN ({letaIn})", connection);

            await using var reader = await cmd.ExecuteReaderAsync();
            var obracunDn = new Dictionary<(string, int), KajObracunam>();
            while (await reader.ReadAsync())
            {
                var key = (reader.GetString(0).Trim(), reader.GetInt32(1));
                obracunDn[key] = (KajObracunam)reader.GetInt32(2);
            }

            foreach (var n in nalogi)
            {
                if (obracunDn.TryGetValue((n.Stevilka, n.Leto), out var kaj))
                    n.KajObracunam = kaj;
            }
        }

        return nalogi;
    }

    /// <summary>
    /// Vrne množico povezanih predračunov za nalog (iz OBRACUN_DN_PREDRACUN).
    /// </summary>
    public async Task<HashSet<(string Stevilka, int Leto)>> GetPovezaniPredracuniAsync(string nalogStevilka, int nalogLeto)
    {
        var result = new HashSet<(string, int)>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT TRIM(PREDRACUN_STEVILKA), PREDRACUN_LETO
            FROM OBRACUN_DN_PREDRACUN
            WHERE STEVILKA = @Stevilka AND LETO = @Leto", connection);
        cmd.Parameters.AddWithValue("@Stevilka", nalogStevilka);
        cmd.Parameters.AddWithValue("@Leto", nalogLeto);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add((reader.GetString(0), reader.GetInt32(1)));

        return result;
    }

    /// <summary>
    /// Shrani spremembe povezav predračunov z nalogom (dodaj/briši vrstice v OBRACUN_DN_PREDRACUN).
    /// </summary>
    public async Task SavePovezaniPredracuniAsync(string nalogStevilka, int nalogLeto,
        List<(string PredracunStevilka, int PredracunLeto)> dodaj,
        List<(string PredracunStevilka, int PredracunLeto)> brisi)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        foreach (var (st, leto) in brisi)
        {
            await using var cmd = new FbCommand(@"
                DELETE FROM OBRACUN_DN_PREDRACUN
                WHERE STEVILKA = @Stevilka AND LETO = @Leto
                  AND PREDRACUN_STEVILKA = @PredSt AND PREDRACUN_LETO = @PredLeto", connection, tx);
            cmd.Parameters.AddWithValue("@Stevilka", nalogStevilka);
            cmd.Parameters.AddWithValue("@Leto", nalogLeto);
            cmd.Parameters.AddWithValue("@PredSt", st);
            cmd.Parameters.AddWithValue("@PredLeto", leto);
            await cmd.ExecuteNonQueryAsync();
        }

        foreach (var (st, leto) in dodaj)
        {
            await using var cmd = new FbCommand(@"
                INSERT INTO OBRACUN_DN_PREDRACUN (STEVILKA, LETO, PREDRACUN_STEVILKA, PREDRACUN_LETO)
                VALUES (@Stevilka, @Leto, @PredSt, @PredLeto)", connection, tx);
            cmd.Parameters.AddWithValue("@Stevilka", nalogStevilka);
            cmd.Parameters.AddWithValue("@Leto", nalogLeto);
            cmd.Parameters.AddWithValue("@PredSt", st);
            cmd.Parameters.AddWithValue("@PredLeto", leto);
            await cmd.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    // ==================== Nalogi za partnerja (popup) ====================

    /// <summary>
    /// Pridobi naloge za partnerja za prikaz v popup-u z obračunskimi podrobnostmi.
    /// </summary>
    public async Task<List<NalogiPartnerNalogDto>> GetNalogiZaPartnerjaPopupAsync(int leto, int mesec, int partner)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        var prviDan = new DateTime(leto, mesec, 1);
        var zadnjiDan = prviDan.AddMonths(1).AddDays(-1);

        // 1. Preberi naloge iz FA_DN_NALOG
        var nalogi = new Dictionary<string, NalogiPartnerNalogDto>();
        await using (var cmd = new FbCommand(@"
            SELECT STEVILKA, LETO, ZACETEK_DATUM, ZACETEK_URA, KONEC_URA, POTNIK,
                   NAZIV1, NAZIV2, NAZIV3, NAZIV4, NAZIV5, NAZIV6, NAZIV7, NAZIV8, NAZIV9
            FROM FA_DN_NALOG
            WHERE PARTNER = @Partner AND LETO >= 2026
              AND ZACETEK_DATUM >= @DatumOd AND ZACETEK_DATUM <= @DatumDo
              AND FAKTURIRANA <> 1
            ORDER BY ZACETEK_DATUM DESC, ZACETEK_URA DESC", connection))
        {
            cmd.Parameters.AddWithValue("@Partner", partner);
            cmd.Parameters.AddWithValue("@DatumOd", prviDan);
            cmd.Parameters.AddWithValue("@DatumDo", zadnjiDan);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var stevilka = reader.GetString(0).Trim();
                var letoNaloga = reader.GetInt32(1);
                var datum = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2);
                var zacetekUra = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3);
                var konecUra = reader.IsDBNull(4) ? (DateTime?)null : reader.GetDateTime(4);
                var potnik = reader.IsDBNull(5) ? null : reader.GetString(5).Trim();

                // Opis iz NAZIV1..NAZIV9
                var nazivi = new List<string>();
                for (int i = 6; i <= 14; i++)
                {
                    if (!reader.IsDBNull(i))
                    {
                        var val = reader.GetString(i).Trim();
                        if (!string.IsNullOrEmpty(val))
                            nazivi.Add(val);
                    }
                }
                var opis = nazivi.Count > 0 ? string.Join(Environment.NewLine, nazivi) : null;

                // Izračunaj trajanje: terenski = KonecUra - ZacetekUra
                int trajanje = 0;
                if (zacetekUra.HasValue && konecUra.HasValue)
                {
                    var razlika = (konecUra.Value - zacetekUra.Value).TotalMinutes;
                    if (razlika < 0) razlika += 24 * 60;
                    trajanje = (int)razlika;
                }

                var key = $"{stevilka}/{letoNaloga}";
                nalogi[key] = new NalogiPartnerNalogDto
                {
                    Stevilka = stevilka,
                    Leto = letoNaloga,
                    Datum = datum,
                    ZacetekUra = zacetekUra,
                    KonecUra = konecUra,
                    Trajanje = trajanje,
                    Serviser = potnik, // začasno šifra, zamenjamo z imenom
                    Opis = opis
                };
            }
        }

        if (nalogi.Count == 0)
            return new List<NalogiPartnerNalogDto>();

        // 2. Za helpdesk naloge (1000000-1999999) preberi količino artikla 047512
        var helpdeskStevilke = nalogi.Values
            .Where(n => int.TryParse(n.Stevilka, out var s) && s >= 1000000 && s <= 1999999)
            .Select(n => n.Stevilka)
            .ToList();

        if (helpdeskStevilke.Count > 0)
        {
            var inList = string.Join(",", helpdeskStevilke.Select(s => $"'{s.Replace("'", "''")}'"));
            var letaIn = string.Join(",", nalogi.Values.Select(n => n.Leto).Distinct());
            await using var cmd = new FbCommand($@"
                SELECT STEVILKA, LETO, KOLICINA
                FROM FA_DN_NALOG_KNJ
                WHERE STEVILKA IN ({inList}) AND LETO IN ({letaIn}) AND SIFRA = '047512'", connection);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var stevilka = reader.GetString(0).Trim();
                var letoNaloga = reader.GetInt32(1);
                var kolicina = reader.GetDecimal(2);
                var key = $"{stevilka}/{letoNaloga}";
                if (nalogi.TryGetValue(key, out var nalog))
                    nalog.Trajanje = (int)kolicina;
            }
        }

        // 3. Preberi nazive komercialistov (serviserjev)
        var potnikSifre = nalogi.Values
            .Where(n => !string.IsNullOrEmpty(n.Serviser))
            .Select(n => n.Serviser!)
            .Distinct()
            .ToList();

        if (potnikSifre.Count > 0)
        {
            var inList = string.Join(",", potnikSifre.Select(s => $"'{s.Replace("'", "''")}'"));
            await using var cmd = new FbCommand($"SELECT SIFRA, PRIIMEK, IME FROM FA_KOMERCIALIST WHERE SIFRA IN ({inList})", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            var komerci = new Dictionary<string, string>();
            while (await reader.ReadAsync())
            {
                var sifra = reader.GetString(0).Trim();
                var priimek = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                var ime = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                komerci[sifra] = string.IsNullOrEmpty(ime) ? priimek : $"{priimek} {ime}".Trim();
            }
            foreach (var n in nalogi.Values.Where(n => !string.IsNullOrEmpty(n.Serviser) && komerci.ContainsKey(n.Serviser!)))
                n.Serviser = komerci[n.Serviser!];
        }

        // 4. Preberi podrobnosti obračuna iz OBRACUN_OSNUTEK_NALOG_OBRACUN
        var stevilkeIn = string.Join(",", nalogi.Values.Select(n => $"'{n.Stevilka.Replace("'", "''")}'").Distinct());
        var letaObracun = string.Join(",", nalogi.Values.Select(n => n.Leto).Distinct());

        var obracuni = new List<(string Key, NalogiPartnerObracunDto Dto)>();
        await using (var cmd = new FbCommand($@"
            SELECT STEVILKA_NALOGA, LETO_NALOGA, OBRACUNAM, SIFRA_ARTIKLA, KOLICINA, PRODAJNA_CENA,
                   MINUTE_ODSTETE_PARTNER_MINUTE, MINUTE_ODSTETE_PREDRACUN, MINUTE_ODSTETE_ROCNO, MINUTE_ODSTETE_POGODBA
            FROM OBRACUN_OSNUTEK_NALOG_OBRACUN
            WHERE MESEC = @Mesec AND LETO = @Leto AND PARTNER = @Partner
              AND STEVILKA_NALOGA IN ({stevilkeIn}) AND LETO_NALOGA IN ({letaObracun})", connection))
        {
            cmd.Parameters.AddWithValue("@Mesec", mesec);
            cmd.Parameters.AddWithValue("@Leto", leto);
            cmd.Parameters.AddWithValue("@Partner", partner);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var stevilka = reader.GetString(0).Trim();
                var letoNaloga = reader.GetInt32(1);
                var obracunam = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                var sifraArtikla = reader.IsDBNull(3) ? null : reader.GetString(3).Trim();
                var kolicina = reader.IsDBNull(4) ? 0m : reader.GetDecimal(4);
                var prodajnaCena = reader.IsDBNull(5) ? 0m : reader.GetDecimal(5);
                var partnerMin = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);
                var predracunMin = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
                var rocnoMin = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);
                var pogodbaMin = reader.IsDBNull(9) ? 0 : reader.GetInt32(9);

                var key = $"{stevilka}/{letoNaloga}";
                obracuni.Add((key, new NalogiPartnerObracunDto
                {
                    StevilkaNaloga = stevilka,
                    LetoNaloga = letoNaloga,
                    Obracunam = obracunam == 1,
                    SifraArtikla = sifraArtikla,
                    Kolicina = kolicina,
                    ProdajnaCena = prodajnaCena,
                    PartnerMinute = partnerMin,
                    PredracunMinute = predracunMin,
                    RocnoMinute = rocnoMin,
                    PogodbaMinute = pogodbaMin,
                    SkupajMinute = partnerMin + predracunMin + rocnoMin + pogodbaMin
                }));
            }
        }

        // 5. Obogati obračune z nazivi artiklov
        var artikelSifre = obracuni.Where(o => !string.IsNullOrEmpty(o.Dto.SifraArtikla))
            .Select(o => o.Dto.SifraArtikla!).Distinct().ToList();
        var artikelMap = new Dictionary<string, (string Naziv, string? Enota)>();
        if (artikelSifre.Count > 0)
        {
            var inList = string.Join(",", artikelSifre.Select(s => $"'{s.Replace("'", "''")}'"));
            await using var cmd = new FbCommand($"SELECT SIFRA, NAZIV, NAZIV2, ENOTA FROM FA_ARTIKEL WHERE SIFRA IN ({inList})", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sifra = reader.GetString(0).Trim();
                var naziv = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                var naziv2 = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                var enota = reader.IsDBNull(3) ? null : reader.GetString(3).Trim();
                artikelMap[sifra] = (ArtikelHelper.GetFullName(naziv, naziv2), enota);
            }
        }

        foreach (var (key, dto) in obracuni)
        {
            if (!string.IsNullOrEmpty(dto.SifraArtikla) && artikelMap.TryGetValue(dto.SifraArtikla, out var art))
            {
                dto.NazivArtikla = art.Naziv;
                dto.EnotaArtikla = art.Enota;
            }

            if (nalogi.TryGetValue(key, out var nalog))
                nalog.Obracuni.Add(dto);
        }

        return nalogi.Values.OrderByDescending(n => n.Datum).ThenByDescending(n => n.ZacetekUra).ToList();
    }

    /// <summary>
    /// Pridobi seštevek dela po šifri artikla — pregled količin iz OBRACUN_OSNUTEK_POS
    /// z razdelitvijo koriščenja iz OBRACUN_OSNUTEK_NALOG_OBRACUN.
    /// </summary>
    public async Task<List<SestevekDelaGridDto>> GetSestevekDelaAsync(int leto, int mesec)
    {
        var rezultat = new List<SestevekDelaGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT 
                pos.ARTIKEL,
                COALESCE(a.NAZIV, '') AS NAZIV_ARTIKLA,
                COALESCE(a.ENOTA, '') AS ENOTA,
                pos.KOLICINA_SKUPAJ,
                COALESCE(nob.KOLICINA_NEOBRACUNANA, 0) AS KOLICINA_NEOBRACUNANA,
                COALESCE(nob.KOLICINA_OBRACUNANA, 0) AS KOLICINA_OBRACUNANA,
                COALESCE(nob.ODSTETE_ROCNO, 0) AS ODSTETE_ROCNO,
                COALESCE(nob.ODSTETE_PARTNER_MINUTE, 0) AS ODSTETE_PARTNER_MINUTE,
                COALESCE(nob.ODSTETE_PREDRACUN, 0) AS ODSTETE_PREDRACUN,
                COALESCE(nob.ODSTETE_POGODBA, 0) AS ODSTETE_POGODBA,
                COALESCE(nob.KOLICINA_FAKTURIRANA, 0) AS KOLICINA_FAKTURIRANA
            FROM (
                SELECT ARTIKEL, SUM(COALESCE(KOLICINA, 0)) AS KOLICINA_SKUPAJ
                FROM OBRACUN_OSNUTEK_POS
                WHERE LETO = @Leto AND MESEC = @Mesec
                GROUP BY ARTIKEL
            ) pos
            LEFT JOIN FA_ARTIKEL a ON pos.ARTIKEL = a.SIFRA
            LEFT JOIN (
                SELECT 
                    SIFRA_ARTIKLA,
                    SUM(CASE WHEN OBRACUNAM = 0 THEN COALESCE(KOLICINA, 0) ELSE 0 END) AS KOLICINA_NEOBRACUNANA,
                    SUM(CASE WHEN OBRACUNAM = 1 THEN COALESCE(KOLICINA, 0) ELSE 0 END) AS KOLICINA_OBRACUNANA,
                    SUM(COALESCE(MINUTE_ODSTETE_ROCNO, 0)) AS ODSTETE_ROCNO,
                    SUM(COALESCE(MINUTE_ODSTETE_PARTNER_MINUTE, 0)) AS ODSTETE_PARTNER_MINUTE,
                    SUM(COALESCE(MINUTE_ODSTETE_PREDRACUN, 0)) AS ODSTETE_PREDRACUN,
                    SUM(COALESCE(MINUTE_ODSTETE_POGODBA, 0)) AS ODSTETE_POGODBA,
                    SUM(COALESCE(KOLICINA_FAKTURIRANA, 0)) AS KOLICINA_FAKTURIRANA
                FROM OBRACUN_OSNUTEK_NALOG_OBRACUN
                WHERE LETO = @Leto AND MESEC = @Mesec
                GROUP BY SIFRA_ARTIKLA
            ) nob ON pos.ARTIKEL = nob.SIFRA_ARTIKLA
            ORDER BY pos.ARTIKEL", connection);

        cmd.Parameters.AddWithValue("@Leto", leto);
        cmd.Parameters.AddWithValue("@Mesec", mesec);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var odsteteRocno = reader.IsDBNull(6) ? 0 : reader.GetInt32(6);
            var odstetePartnerMinute = reader.IsDBNull(7) ? 0 : reader.GetInt32(7);
            var odstetePredracun = reader.IsDBNull(8) ? 0 : reader.GetInt32(8);
            var odstetePogodba = reader.IsDBNull(9) ? 0 : reader.GetInt32(9);

            rezultat.Add(new SestevekDelaGridDto
            {
                SifraArtikla = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim(),
                NazivArtikla = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                Enota = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim(),
                KolicinaSkupaj = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3),
                KolicinaNeobracunana = reader.IsDBNull(4) ? 0 : reader.GetDecimal(4),
                KolicinaObracunana = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5),
                OdsteteRocno = odsteteRocno,
                OdstetePartnerMinute = odstetePartnerMinute,
                OdstetePredracun = odstetePredracun,
                OdstetePogodba = odstetePogodba,
                OdsteteSkupaj = odsteteRocno + odstetePartnerMinute + odstetePredracun + odstetePogodba,
                KolicinaFakturirana = reader.IsDBNull(10) ? 0 : reader.GetDecimal(10)
            });
        }

        return rezultat;
    }

    /// <summary>
    /// Pridobi pregled ur po serviserjih za izbrani mesec/leto.
    /// Stolpci: serviser, st. nalogov, skupaj ure, ure NOM (SIF28=1), ure partner=23900 brez NOM.
    /// </summary>
    public Task<List<PregledUrGridDto>> GetPregledUrAsync(int leto, int mesec) =>
        GetPregledUrAsync(leto, mesec, leto, mesec);

    public async Task<List<PregledUrGridDto>> GetPregledUrAsync(int letoOd, int mesecOd, int letoDo, int mesecDo)
    {
        var datumOd = new DateTime(letoOd, mesecOd, 1);
        var datumDo = new DateTime(letoDo, mesecDo, 1).AddMonths(1).AddDays(-1);
        if (datumOd > datumDo)
            (datumOd, datumDo) = (datumDo.Date.AddDays(1 - datumDo.Day), datumOd.AddMonths(1).AddDays(-1));

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // 1. Preberi naloge (potnik, partner, NOM, zacetek/konec - za trajanje)
        var nalogi = new List<(string Potnik, int Partner, int Nom, DateTime Datum, DateTime ZacetekUra, DateTime KonecUra, string Stevilka, int Leto)>();
        await using (var cmd = new FbCommand(@"
            SELECT POTNIK, PARTNER, SIF28, ZACETEK_DATUM, ZACETEK_URA, KONEC_URA, STEVILKA, LETO
            FROM FA_DN_NALOG
            WHERE ZACETEK_DATUM >= @DatumOd AND ZACETEK_DATUM <= @DatumDo", connection))
        {
            cmd.Parameters.AddWithValue("@DatumOd", datumOd);
            cmd.Parameters.AddWithValue("@DatumDo", datumDo);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var potnik = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                var partner = reader.GetInt32(1);
                var nom = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                var datum = reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3);
                var zacetek = reader.IsDBNull(4) ? DateTime.MinValue : reader.GetDateTime(4);
                var konec = reader.IsDBNull(5) ? DateTime.MinValue : reader.GetDateTime(5);
                var stevilka = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim();
                var letoN = reader.GetInt32(7);
                nalogi.Add((potnik, partner, nom, datum, zacetek, konec, stevilka, letoN));
            }
        }

        if (nalogi.Count == 0) return new();

        // 2. Za helpdesk naloge (7 znakov, zacne se z 1) preberi trajanje iz FA_DN_NALOG_KNJ (SIFRA='047512')
        var helpdeskNalogi = nalogi
            .Where(n => n.Stevilka.Length == 7 && n.Stevilka.StartsWith("1"))
            .Select(n => (n.Stevilka, n.Leto))
            .Distinct()
            .ToList();

        var trajanjeSlovar = new Dictionary<(string, int), int>();
        if (helpdeskNalogi.Count > 0)
        {
            var stevilkeIn = string.Join(",", helpdeskNalogi.Select(h => $"'{h.Stevilka.Replace("'", "''")}'"));
            var letaIn = string.Join(",", helpdeskNalogi.Select(h => h.Leto).Distinct());

            await using var cmd = new FbCommand($@"
                SELECT STEVILKA, LETO, KOLICINA
                FROM FA_DN_NALOG_KNJ
                WHERE STEVILKA IN ({stevilkeIn}) AND LETO IN ({letaIn})
                  AND TRIM(SIFRA) = '047512'", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var key = (reader.GetString(0).Trim(), reader.GetInt32(1));
                trajanjeSlovar[key] = (int)reader.GetDecimal(2);
            }
        }

        // 3. Preberi nazive serviserjev
        var potnikSifre = nalogi.Where(n => !string.IsNullOrEmpty(n.Potnik)).Select(n => n.Potnik).Distinct().ToList();
        var potniki = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (potnikSifre.Count > 0)
        {
            var potnikIn = string.Join(",", potnikSifre.Select(s => $"'{s.Replace("'", "''")}'"));
            await using var cmd = new FbCommand($"SELECT SIFRA, PRIIMEK, IME FROM FA_KOMERCIALIST WHERE SIFRA IN ({potnikIn})", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sifra = reader.GetString(0).Trim();
                var priimek = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                var ime = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                potniki[sifra] = string.IsNullOrEmpty(ime) ? priimek : $"{priimek} {ime}".Trim();
            }
        }

        // 4. Sestej po serviserju
        var grupirano = new Dictionary<string, PregledUrGridDto>();
        foreach (var n in nalogi)
        {
            decimal trajanjeMin;
            if (trajanjeSlovar.TryGetValue((n.Stevilka, n.Leto), out var trajHd))
            {
                trajanjeMin = trajHd;
            }
            else
            {
                var t = (int)(n.KonecUra - n.ZacetekUra).TotalMinutes;
                if (t < 0) t += 1440;
                trajanjeMin = t;
            }

            var ure = trajanjeMin / 60m;

            if (!grupirano.TryGetValue(n.Potnik, out var dto))
            {
                potniki.TryGetValue(n.Potnik, out var naziv);
                dto = new PregledUrGridDto
                {
                    Serviser = n.Potnik,
                    NazivServiserja = naziv ?? n.Potnik
                };
                grupirano[n.Potnik] = dto;
            }

            dto.SteviloNalogov++;
            dto.SkupajUre += ure;
            if (n.Nom == 1)
                dto.UreNom += ure;
            else if (n.Partner == 23900)
                dto.UrePartner23900 += ure;
            else
            {
                // Ure stranke - razčleni po tarifah (07-16 / 16-22 / 22-07)
                var (m7_16, m16_22, m22_7) = RazcleniTrajanje(n.ZacetekUra, (int)trajanjeMin);
                dto.UreStranke_7_16 += m7_16 / 60m;
                dto.UreStranke_16_22 += m16_22 / 60m;
                dto.UreStranke_22_7 += m22_7 / 60m;
            }
        }

        return grupirano.Values
            .OrderBy(d => d.NazivServiserja)
            .ToList();
    }

    /// <summary>
    /// Razčleni trajanje (v minutah) od podanega začetka na tri tarifne pasove:
    /// 07:00-16:00, 16:00-22:00, 22:00-07:00.
    /// </summary>
    private static (int M7_16, int M16_22, int M22_7) RazcleniTrajanje(DateTime zacetek, int trajanjeMin)
    {
        if (trajanjeMin <= 0) return (0, 0, 0);

        int m7_16 = 0, m16_22 = 0, m22_7 = 0;
        // Začetna minuta v dnevu
        int startMin = zacetek.Hour * 60 + zacetek.Minute;

        for (int i = 0; i < trajanjeMin; i++)
        {
            int h = ((startMin + i) % 1440) / 60;
            if (h >= 7 && h < 16) m7_16++;
            else if (h >= 16 && h < 22) m16_22++;
            else m22_7++;
        }
        return (m7_16, m16_22, m22_7);
    }

    /// <summary>
    /// Pridobi naloge serviserja za izbrani mesec/leto z razčlenitvijo ur,
    /// po želji filtrirano (NOM / partner 23900 / tarifni pas).
    /// </summary>
    public Task<List<PregledUrNalogDto>> GetPregledUrNalogiAsync(
        int leto, int mesec, string serviser, PregledUrFilter filter) =>
        GetPregledUrNalogiAsync(leto, mesec, leto, mesec, serviser, filter);

    public async Task<List<PregledUrNalogDto>> GetPregledUrNalogiAsync(
        int letoOd, int mesecOd, int letoDo, int mesecDo, string serviser, PregledUrFilter filter)
    {
        var datumOd = new DateTime(letoOd, mesecOd, 1);
        var datumDo = new DateTime(letoDo, mesecDo, 1).AddMonths(1).AddDays(-1);
        if (datumOd > datumDo)
            (datumOd, datumDo) = (datumDo.Date.AddDays(1 - datumDo.Day), datumOd.AddMonths(1).AddDays(-1));

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // 1. Naloge serviserja
        var nalogi = new List<(string Stevilka, int LetoN, int Partner, int Nom, DateTime Datum, DateTime Zac, DateTime Konec, string Opis)>();
        await using (var cmd = new FbCommand(@"
            SELECT STEVILKA, LETO, PARTNER, SIF28, ZACETEK_DATUM, ZACETEK_URA, KONEC_URA,
                   NAZIV1, NAZIV2, NAZIV3, NAZIV4, NAZIV5, NAZIV6, NAZIV7, NAZIV8, NAZIV9
            FROM FA_DN_NALOG
            WHERE ZACETEK_DATUM >= @DatumOd AND ZACETEK_DATUM <= @DatumDo
              AND POTNIK = @Potnik", connection))
        {
            cmd.Parameters.AddWithValue("@DatumOd", datumOd);
            cmd.Parameters.AddWithValue("@DatumDo", datumDo);
            cmd.Parameters.AddWithValue("@Potnik", serviser);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var stevilka = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                var letoN = reader.GetInt32(1);
                var partner = reader.GetInt32(2);
                var nom = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                var datum = reader.IsDBNull(4) ? DateTime.MinValue : reader.GetDateTime(4);
                var zac = reader.IsDBNull(5) ? DateTime.MinValue : reader.GetDateTime(5);
                var konec = reader.IsDBNull(6) ? DateTime.MinValue : reader.GetDateTime(6);

                var nazivi = new List<string>();
                for (int i = 7; i <= 15; i++)
                {
                    if (!reader.IsDBNull(i))
                    {
                        var v = reader.GetString(i).Trim();
                        if (!string.IsNullOrEmpty(v)) nazivi.Add(v);
                    }
                }
                var opis = nazivi.Count > 0 ? string.Join(Environment.NewLine, nazivi) : "";
                nalogi.Add((stevilka, letoN, partner, nom, datum, zac, konec, opis));
            }
        }

        if (nalogi.Count == 0) return new();

        // 2. Trajanje za helpdesk naloge iz FA_DN_NALOG_KNJ (SIFRA='047512')
        var helpdeskNalogi = nalogi
            .Where(n => n.Stevilka.Length == 7 && n.Stevilka.StartsWith("1"))
            .Select(n => (n.Stevilka, n.LetoN))
            .Distinct()
            .ToList();

        var trajanjeSlovar = new Dictionary<(string, int), int>();
        if (helpdeskNalogi.Count > 0)
        {
            var stevilkeIn = string.Join(",", helpdeskNalogi.Select(h => $"'{h.Stevilka.Replace("'", "''")}'"));
            var letaIn = string.Join(",", helpdeskNalogi.Select(h => h.LetoN).Distinct());

            await using var cmd = new FbCommand($@"
                SELECT STEVILKA, LETO, KOLICINA
                FROM FA_DN_NALOG_KNJ
                WHERE STEVILKA IN ({stevilkeIn}) AND LETO IN ({letaIn})
                  AND TRIM(SIFRA) = '047512'", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var key = (reader.GetString(0).Trim(), reader.GetInt32(1));
                trajanjeSlovar[key] = (int)reader.GetDecimal(2);
            }
        }

        // 3. Nazivi partnerjev
        var partnerSifre = nalogi.Select(n => n.Partner).Distinct().ToList();
        var partnerji = new Dictionary<int, string>();
        if (partnerSifre.Count > 0)
        {
            var inList = string.Join(",", partnerSifre);
            await using var cmd = new FbCommand($"SELECT SIFRA, NAZIV FROM PARTNER WHERE SIFRA IN ({inList})", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sifra = reader.GetInt32(0);
                var naziv = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                partnerji[sifra] = naziv;
            }
        }

        // 4. Sestavi DTO-je in uporabi filter
        var praznikiSet = new HashSet<DateTime>();
        for (int i = 0; i < 5; i++)
        {
            var val = _parametri.GetString(ParametriService.Praznik(i + 1));
            if (!string.IsNullOrWhiteSpace(val) && DateTime.TryParse(val, out var dt))
                praznikiSet.Add(dt.Date);
        }

        var rezultat = new List<PregledUrNalogDto>();
        foreach (var n in nalogi)
        {
            int trajanjeMin;
            if (trajanjeSlovar.TryGetValue((n.Stevilka, n.LetoN), out var trajHd))
            {
                trajanjeMin = trajHd;
            }
            else
            {
                var t = (int)(n.Konec - n.Zac).TotalMinutes;
                if (t < 0) t += 1440;
                trajanjeMin = t;
            }

            var ure = trajanjeMin / 60m;
            var datumNaloga = n.Datum == DateTime.MinValue ? (DateTime?)null : n.Datum.Date;
            var tipDneva = datumNaloga.HasValue
                ? MinuteCalculator.DolocitTipDneva(datumNaloga.Value, praznikiSet) switch
                {
                    TipDneva.Vikend => "Vikend",
                    TipDneva.Praznik => "Praznik",
                    _ => "Delavnik"
                }
                : "";

            var dto = new PregledUrNalogDto
            {
                Stevilka = n.Stevilka,
                LetoNaloga = n.LetoN,
                Datum = datumNaloga,
                ZacetekUra = n.Zac == DateTime.MinValue ? null : n.Zac,
                KonecUra = n.Konec == DateTime.MinValue ? null : n.Konec,
                Partner = n.Partner,
                NazivPartnerja = partnerji.TryGetValue(n.Partner, out var np) ? np : "",
                Nom = n.Nom == 1,
                TipDneva = tipDneva,
                TrajanjeMin = trajanjeMin,
                Opis = string.IsNullOrEmpty(n.Opis) ? null : n.Opis
            };

            if (n.Nom == 1)
            {
                dto.UreNom = ure;
            }
            else if (n.Partner == 23900)
            {
                dto.UrePartner23900 = ure;
            }
            else
            {
                var (m7_16, m16_22, m22_7) = RazcleniTrajanje(n.Zac, trajanjeMin);
                dto.UreStranke_7_16 = m7_16 / 60m;
                dto.UreStranke_16_22 = m16_22 / 60m;
                dto.UreStranke_22_7 = m22_7 / 60m;
            }

            // Filter
            bool vkljuci = filter switch
            {
                PregledUrFilter.Vsi => true,
                PregledUrFilter.Nom => n.Nom == 1,
                PregledUrFilter.Partner23900 => n.Nom != 1 && n.Partner == 23900,
                PregledUrFilter.Stranke_7_16 => n.Nom != 1 && n.Partner != 23900 && dto.UreStranke_7_16 > 0,
                PregledUrFilter.Stranke_16_22 => n.Nom != 1 && n.Partner != 23900 && dto.UreStranke_16_22 > 0,
                PregledUrFilter.Stranke_22_7 => n.Nom != 1 && n.Partner != 23900 && dto.UreStranke_22_7 > 0,
                PregledUrFilter.StrankeVse => n.Nom != 1 && n.Partner != 23900,
                _ => true
            };

            if (vkljuci)
                rezultat.Add(dto);
        }

        return rezultat
            .OrderBy(d => d.Datum)
            .ThenBy(d => d.ZacetekUra)
            .ToList();
    }
}
