using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data.DTOs;

namespace ObracunDb.Services;

/// <summary>
/// Servis za pregled partnerjev
/// </summary>
public class PartnerService
{
    private readonly Data.FirebirdConnectionManager _connectionManager;
    private static readonly DateTime MejniDatum = new(2025, 1, 1);

    /// <summary>
    /// Subquery za vsoto nabavne vrednosti iz FA_INTERNI_KNJIZBA (POVEZAVA_TIP=51)
    /// </summary>
    private const string InterniNabavnaSubquery = @"
        SELECT I.POVEZAVA_STEVILKA, I.POVEZAVA_LETO, IK.ZS_SESTAVA,
               SUM(COALESCE(IK.NABAVNA_VREDNOST, 0)) as NAB_VRED
        FROM FA_INTERNI I
        JOIN FA_INTERNI_KNJIZBA IK ON I.STEVILKA = IK.STEVILKA AND I.LETO = IK.LETO
        WHERE I.POVEZAVA_TIP = 51
        GROUP BY I.POVEZAVA_STEVILKA, I.POVEZAVA_LETO, IK.ZS_SESTAVA";

    public PartnerService(Data.FirebirdConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    /// <summary>
    /// Centralna metoda za izraèun Blago/Storitve/BlagoNab po raèunih.
    /// partner=0 ? vsi partnerji, sicer samo ta partner.
    /// razcleniPoRacunih=true ? vrstica per raèun, false ? seštevek per partner.
    /// </summary>
    public async Task<List<PartnerRacunDto>> IzracunajRacuneAsync(int partner, ObdobjeRange obdobje, bool razcleniPoRacunih)
    {
        var result = new List<PartnerRacunDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // Dinamièen WHERE pogoj za partnerja
        var partnerFilter = partner > 0 ? "AND R.SIFRA_KUPCA = @Partner" : "";

        void addParams(FbCommand c)
        {
            c.Parameters.AddWithValue("@Od", obdobje.Od);
            c.Parameters.AddWithValue("@DoEks", obdobje.Do.AddDays(1));
            if (partner > 0)
                c.Parameters.AddWithValue("@Partner", partner);
        }

        // Skupna polja za SELECT in GROUP BY glede na razèlenitev
        string selectKey, groupByKey;
        if (razcleniPoRacunih)
        {
            selectKey = "R.SIFRA_KUPCA, R.STEVILKA, R.LETO, R.DATUM, COALESCE(R.ZNESEK_KONCNI / 1.22, 0), COALESCE(R.TIP_RACUNA, 0)";
            groupByKey = "R.SIFRA_KUPCA, R.STEVILKA, R.LETO, R.DATUM, R.ZNESEK_KONCNI, R.TIP_RACUNA";
        }
        else
        {
            selectKey = "R.SIFRA_KUPCA, CAST('' AS VARCHAR(1)), 0, CAST(NULL AS DATE), CAST(0 AS NUMERIC(15,2)), 0";
            groupByKey = "R.SIFRA_KUPCA";
        }

        // Rezultat kot dictionary za hitro iskanje
        var map = new Dictionary<string, PartnerRacunDto>();

        string makeKey(int sifra, string stevilka, int leto) =>
            razcleniPoRacunih ? $"{sifra}_{stevilka}_{leto}" : $"{sifra}";

        void parseRow(System.Data.Common.DbDataReader reader, decimal blago, decimal storitve, decimal blagoNab)
        {
            var sifra = reader.GetInt32(0);
            var stevilka = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
            var leto = reader.GetInt32(2);
            var key = makeKey(sifra, stevilka, leto);

            if (map.TryGetValue(key, out var existing))
            {
                existing.Blago += blago;
                existing.Storitve += storitve;
                existing.BlagoNab += blagoNab;
                if (!razcleniPoRacunih)
                    existing.ZnesekKoncni += reader.GetDecimal(4);
            }
            else
            {
                var dto = new PartnerRacunDto
                {
                    SifraKupca = sifra,
                    Stevilka = stevilka,
                    Leto = leto,
                    Datum = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                    ZnesekKoncni = reader.GetDecimal(4),
                    TipRacuna = reader.GetInt32(5),
                    Blago = blago,
                    Storitve = storitve,
                    BlagoNab = blagoNab
                };
                map[key] = dto;
                result.Add(dto);
            }
        }

        // 1. TIP_RACUNA 0,1,6 — knjižbe iz FA_DOBAVNICA_KNJIZBA
        await using (var cmd = new FbCommand($@"
            SELECT {selectKey},
                SUM(CASE
                    WHEN COALESCE(A.KARTICA_ARTIKLA, -1) = 1 THEN COALESCE(K.VREDNOST, 0)
                    WHEN COALESCE(A.KARTICA_ARTIKLA, -1) = 0 AND IK_AGG.NAB_VRED IS NOT NULL THEN COALESCE(K.VREDNOST, 0)
                    ELSE 0 END),
                SUM(CASE
                    WHEN COALESCE(A.KARTICA_ARTIKLA, -1) = 1 THEN 0
                    WHEN COALESCE(A.KARTICA_ARTIKLA, -1) = 0 AND IK_AGG.NAB_VRED IS NOT NULL THEN 0
                    ELSE COALESCE(K.VREDNOST, 0) END),
                SUM(CASE
                    WHEN COALESCE(A.KARTICA_ARTIKLA, -1) = 1 THEN COALESCE(K.VREDNOST, 0) - COALESCE(K.NABAVNA_VREDNOST, 0)
                    WHEN COALESCE(A.KARTICA_ARTIKLA, -1) = 0 AND IK_AGG.NAB_VRED IS NOT NULL THEN COALESCE(K.VREDNOST, 0) - COALESCE(IK_AGG.NAB_VRED, 0)
                    ELSE 0 END)
            FROM FA_RACUN R
            INNER JOIN FA_DOBAVNICA D ON R.STEVILKA = D.RACUN_STEVILKA AND R.LETO = D.RACUN_LETO
            LEFT JOIN FA_DOBAVNICA_KNJIZBA K ON D.LETO = K.LETO AND D.STEVILKA = K.STEVILKA
            LEFT JOIN FA_ARTIKEL A ON A.SIFRA = K.SIFRA
            LEFT JOIN ({InterniNabavnaSubquery}
            ) IK_AGG ON IK_AGG.POVEZAVA_STEVILKA = D.STEVILKA AND IK_AGG.POVEZAVA_LETO = D.LETO AND IK_AGG.ZS_SESTAVA = K.ZS
            WHERE R.DATUM >= @Od AND R.DATUM < @DoEks
              AND COALESCE(R.TIP_RACUNA, 0) IN (0, 1, 6)
              AND COALESCE(R.TIP_RACUNA, 0) <> 4
              {partnerFilter}
            GROUP BY {groupByKey}", connection))
        {
            addParams(cmd);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                parseRow(reader, reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8));
        }

        // 2. TIP_RACUNA 2,5 — vse je storitev iz FA_RACUN_KNJIZBA
        await using (var cmd = new FbCommand($@"
            SELECT {selectKey},
                CAST(0 AS NUMERIC(15,2)),
                SUM(COALESCE(K.VREDNOST, 0)),
                CAST(0 AS NUMERIC(15,2))
            FROM FA_RACUN R
            LEFT JOIN FA_RACUN_KNJIZBA K ON R.STEVILKA = K.STEVILKA AND R.LETO = K.LETO
            WHERE R.DATUM >= @Od AND R.DATUM < @DoEks
              AND COALESCE(R.TIP_RACUNA, 0) IN (2, 5)
              AND COALESCE(R.TIP_RACUNA, 0) <> 4
              {partnerFilter}
            GROUP BY {groupByKey}", connection))
        {
            addParams(cmd);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                parseRow(reader, reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8));
        }

        // 3. TIP_RACUNA 7,8,12 — knjižbe iz FA_DN_NALOG_KNJ
        await using (var cmd = new FbCommand($@"
            SELECT {selectKey},
                SUM(CASE WHEN COALESCE(A.KARTICA_ARTIKLA, 0) = 1 THEN COALESCE(K.VREDNOST, 0) ELSE 0 END),
                SUM(CASE WHEN COALESCE(A.KARTICA_ARTIKLA, 0) <> 1 THEN COALESCE(K.VREDNOST, 0) ELSE 0 END),
                SUM(CASE WHEN COALESCE(A.KARTICA_ARTIKLA, 0) = 1 THEN COALESCE(K.VREDNOST, 0) - COALESCE(K.NABAVNA_VREDNOST, 0) ELSE 0 END)
            FROM FA_RACUN R
            INNER JOIN FA_DN_NALOG D ON R.STEVILKA = D.RACUN_STEVILKA AND R.LETO = D.RACUN_LETO
            LEFT JOIN FA_DN_NALOG_KNJ K ON D.LETO = K.LETO AND D.STEVILKA = K.STEVILKA AND K.FAKTURIRA <> 0
            LEFT JOIN FA_ARTIKEL A ON A.SIFRA = K.SIFRA
            WHERE R.DATUM >= @Od AND R.DATUM < @DoEks
              AND COALESCE(R.TIP_RACUNA, 0) IN (7, 8, 12)
              AND COALESCE(R.TIP_RACUNA, 0) <> 4
              {partnerFilter}
            GROUP BY {groupByKey}", connection))
        {
            addParams(cmd);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                parseRow(reader, reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8));
        }

        // 4. Ostale raèune (ki niso pokriti zgoraj) dodaj z Blago/Storitve/BlagoNab = 0
        if (razcleniPoRacunih)
        {
            await using var cmd = new FbCommand($@"
                SELECT SIFRA_KUPCA, STEVILKA, LETO, DATUM, COALESCE(ZNESEK_KONCNI / 1.22, 0), COALESCE(TIP_RACUNA, 0)
                FROM FA_RACUN
                WHERE DATUM >= @Od AND DATUM < @DoEks
                  AND COALESCE(TIP_RACUNA, 0) <> 4
                  {partnerFilter.Replace("R.", "")}
                ORDER BY DATUM DESC, STEVILKA DESC", connection);
            addParams(cmd);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sifra = reader.GetInt32(0);
                var stevilka = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                var racLeto = reader.GetInt32(2);
                var key = makeKey(sifra, stevilka, racLeto);
                if (!map.ContainsKey(key))
                {
                    var dto = new PartnerRacunDto
                    {
                        SifraKupca = sifra,
                        Stevilka = stevilka,
                        Leto = racLeto,
                        Datum = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                        ZnesekKoncni = reader.GetDecimal(4),
                        TipRacuna = reader.GetInt32(5)
                    };
                    map[key] = dto;
                    result.Add(dto);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Pridobi vse partnerje ki se pojavijo na raèunih, obogatene s podatki iz drugih tabel
    /// </summary>
    public async Task<List<PartnerGridDto>> GetAllAsync(ObdobjeRange obdobje)
    {
        var partnerji = new Dictionary<int, PartnerGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        var od = obdobje.Od;
        var doEks = obdobje.Do.AddDays(1);

        // 1. Partnerji iz FA_RACUN — distinct šifra + skupni znesek
        await using (var cmd = new FbCommand(@"
            SELECT r.SIFRA_KUPCA, p.NAZIV, COALESCE(SUM(r.ZNESEK_KONCNI / 1.22), 0)
            FROM FA_RACUN r
            LEFT JOIN PARTNER p ON r.SIFRA_KUPCA = p.SIFRA
            WHERE r.DATUM >= @Od AND r.DATUM < @DoEks
              AND COALESCE(r.TIP_RACUNA, 0) <> 4
            GROUP BY r.SIFRA_KUPCA, p.NAZIV
            ORDER BY p.NAZIV", connection))
        {
            cmd.Parameters.AddWithValue("@Od", od);
            cmd.Parameters.AddWithValue("@DoEks", doEks);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sifra = reader.GetInt32(0);
                partnerji[sifra] = new PartnerGridDto
                {
                    Sifra = sifra,
                    Naziv = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                    SkupniZnesekRacunov = reader.GetDecimal(2)
                };
            }
        }

        // 2. Število nalogov iz FA_DN_NALOG
        await using (var cmd = new FbCommand(@"
            SELECT PARTNER, COUNT(*)
            FROM FA_DN_NALOG
            WHERE ZACETEK_DATUM >= @Od AND ZACETEK_DATUM < @DoEks
            GROUP BY PARTNER", connection))
        {
            cmd.Parameters.AddWithValue("@Od", od);
            cmd.Parameters.AddWithValue("@DoEks", doEks);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sifra = reader.GetInt32(0);
                var stevilo = reader.GetInt32(1);

                if (partnerji.TryGetValue(sifra, out var partner))
                {
                    partner.SteviloNalogov = stevilo;
                }
            }
        }

        // 3. Blago/Storitve/BlagoNab — iz centralne metode (seštevek po partnerju)
        var racuni = await IzracunajRacuneAsync(0, obdobje, false);
        foreach (var r in racuni)
        {
            if (partnerji.TryGetValue(r.SifraKupca, out var partner))
            {
                partner.Blago += r.Blago;
                partner.Storitve += r.Storitve;
                partner.BlagoNab += r.BlagoNab;
            }
        }

        // 4. Porabljen èas iz FA_DN_NALOG
        //    Helpdesk (STEVILKA med 1000000 in 1999999): trajanje iz FA_DN_NALOG_KNJ, šifra 047512, polje KOLICINA
        //    Ostali: KONEC_URA - ZACETEK_URA v minutah (èe negativno, prištej 1 dan)
        //    FAKTURIRANA=1 ? UreObr, sicer ? UreNeobr

        // 4a. Helpdesk nalogi
        await using (var cmd = new FbCommand(@"
            SELECT N.PARTNER, N.FAKTURIRANA, SUM(COALESCE(K.KOLICINA, 0))
            FROM FA_DN_NALOG N
            JOIN FA_DN_NALOG_KNJ K ON N.STEVILKA = K.STEVILKA AND N.LETO = K.LETO
            WHERE N.ZACETEK_DATUM >= @Od AND N.ZACETEK_DATUM < @DoEks
              AND CAST(N.STEVILKA AS INTEGER) BETWEEN 1000000 AND 1999999
              AND K.SIFRA = '047512'
            GROUP BY N.PARTNER, N.FAKTURIRANA", connection))
        {
            cmd.Parameters.AddWithValue("@Od", od);
            cmd.Parameters.AddWithValue("@DoEks", doEks);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sifra = reader.GetInt32(0);
                var fakturirana = reader.GetInt32(1);
                var minute = reader.GetDecimal(2);

                if (partnerji.TryGetValue(sifra, out var partner))
                {
                    if (fakturirana == 1)
                        partner.UreObr += minute;
                    else
                        partner.UreNeobr += minute;
                }
            }
        }

        // 4b. Ostali nalogi (za leto 2025: trajanje iz OBRACUN_DN.MINUTE_NALOGA, sicer KONEC_URA - ZACETEK_URA)
        await using (var cmd = new FbCommand(@"
            SELECT N.PARTNER, N.FAKTURIRANA, N.ZACETEK_URA, N.KONEC_URA, N.LETO, N.STEVILKA
            FROM FA_DN_NALOG N
            WHERE N.ZACETEK_DATUM >= @Od AND N.ZACETEK_DATUM < @DoEks
              AND (CAST(N.STEVILKA AS INTEGER) < 1000000 OR CAST(N.STEVILKA AS INTEGER) > 1999999)", connection))
        {
            cmd.Parameters.AddWithValue("@Od", od);
            cmd.Parameters.AddWithValue("@DoEks", doEks);

            // Preload OBRACUN_DN minute za leto 2025 (samo ce obdobje prekriva leto 2025)
            var obracunDnMinute = new Dictionary<string, int>();
            if (obdobje.Od.Year <= 2025 && obdobje.Do.Year >= 2025)
            {
                await using var cmdDn = new FbCommand(
                    "SELECT STEVILKA, MINUTE_NALOGA FROM OBRACUN_DN WHERE LETO = 2025 AND MINUTE_NALOGA IS NOT NULL", connection);
                await using var readerDn = await cmdDn.ExecuteReaderAsync();
                while (await readerDn.ReadAsync())
                {
                    var st = readerDn.IsDBNull(0) ? "" : readerDn.GetString(0).Trim();
                    var min = readerDn.GetInt32(1);
                    obracunDnMinute[st] = min;
                }
            }

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var sifra = reader.GetInt32(0);
                var fakturirana = reader.GetInt32(1);
                var nalogLeto = reader.GetInt32(4);
                var stevilka = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim();

                if (!partnerji.TryGetValue(sifra, out var partner)) continue;

                decimal minute;
                if (nalogLeto == 2025 && obracunDnMinute.TryGetValue(stevilka, out var dnMinute))
                {
                    minute = dnMinute;
                }
                else
                {
                    var zacetek = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2);
                    var konec = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3);

                    if (zacetek == null || konec == null) continue;

                    var razlika = konec.Value - zacetek.Value;
                    if (razlika.TotalMinutes < 0)
                        razlika = razlika.Add(TimeSpan.FromDays(1));

                    minute = (decimal)razlika.TotalMinutes;
                }

                if (fakturirana == 1)
                    partner.UreObr += minute;
                else
                    partner.UreNeobr += minute;
            }
        }

        // 5. Pogodbe — število in minute
        var pogodbeAgg = await GetPogodbeAggAsync(obdobje);
        foreach (var (sifra, vals) in pogodbeAgg)
        {
            if (partnerji.TryGetValue(sifra, out var partner))
            {
                partner.SteviloPogodb = vals.Count;
                partner.PogodbeneMinute = vals.Minutes > 0 ? vals.Minutes : null;
            }
        }

        return partnerji.Values.ToList();
    }

    /// <summary>
    /// Pridobi raèune za partnerja — uporablja centralno metodo
    /// </summary>
    public async Task<List<PartnerRacunDto>> GetRacuniAsync(int sifraPartnerja, ObdobjeRange obdobje)
    {
        var result = await IzracunajRacuneAsync(sifraPartnerja, obdobje, true);
        return result.OrderByDescending(r => r.Datum).ThenByDescending(r => r.Stevilka).ToList();
    }

    /// <summary>
    /// Debug: izpis Blago/Storitve/BlagoRazl po vrsticah za doloèen raèun
    /// </summary>
    public async Task<string> GetDebugBlagoRazlAsync(string stevilkaRacuna, int letoRacuna, int tipRacuna)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Debug za raèun {stevilkaRacuna}/{letoRacuna}, TIP_RACUNA={tipRacuna}");
        sb.AppendLine(new string('-', 120));

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        if (tipRacuna is 2 or 5)
        {
            // Vse je storitev iz FA_RACUN_KNJIZBA
            sb.AppendLine("TIP 2/5: vse je storitev (FA_RACUN_KNJIZBA)");
            sb.AppendLine($"{"ZS",4} {"Artikel",-10} {"Naziv",-30} {"VREDNOST",12}");
            sb.AppendLine(new string('-', 60));

            await using var cmd = new FbCommand(@"
                SELECT K.ZS, K.SIFRA, A.NAZIV, COALESCE(K.VREDNOST, 0)
                FROM FA_RACUN_KNJIZBA K
                LEFT JOIN FA_ARTIKEL A ON A.SIFRA = K.SIFRA
                WHERE K.STEVILKA = @Stevilka AND K.LETO = @Leto
                ORDER BY K.ZS", connection);
            cmd.Parameters.AddWithValue("@Stevilka", int.Parse(stevilkaRacuna));
            cmd.Parameters.AddWithValue("@Leto", letoRacuna);

            decimal skupaj = 0;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var zs = reader.GetInt32(0);
                var sifra = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                var naziv = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                var vrednost = reader.GetDecimal(3);
                skupaj += vrednost;
                if (naziv.Length > 30) naziv = naziv[..30];
                sb.AppendLine($"{zs,4} {sifra,-10} {naziv,-30} {vrednost,12:N2}");
            }
            sb.AppendLine(new string('-', 60));
            sb.AppendLine($"{"SKUPAJ Storitve:",-46} {skupaj,12:N2}");
        }
        else if (tipRacuna is 7 or 8 or 12)
        {
            // FA_DN_NALOG_KNJ
            sb.AppendLine("TIP 7/8/12: knjižbe iz FA_DN_NALOG_KNJ");
            sb.AppendLine($"{"ZS",4} {"Artikel",-10} {"Naziv",-25} {"KA",3} {"Tip",-3} {"VREDNOST",12} {"NAB_VRED",12} {"RAZLIKA",12} {"BLAGORAZL",12}");
            sb.AppendLine(new string('-', 100));

            await using var cmd = new FbCommand(@"
                SELECT K.ZS, K.SIFRA, A.NAZIV,
                       COALESCE(A.KARTICA_ARTIKLA, -1), COALESCE(K.VREDNOST, 0), COALESCE(K.NABAVNA_VREDNOST, 0)
                FROM FA_DN_NALOG D
                LEFT JOIN FA_DN_NALOG_KNJ K ON D.LETO = K.LETO AND D.STEVILKA = K.STEVILKA AND K.FAKTURIRA <> 0
                LEFT JOIN FA_ARTIKEL A ON A.SIFRA = K.SIFRA
                WHERE D.RACUN_STEVILKA = @Stevilka AND D.RACUN_LETO = @Leto
                ORDER BY K.ZS", connection);
            cmd.Parameters.AddWithValue("@Stevilka", stevilkaRacuna);
            cmd.Parameters.AddWithValue("@Leto", letoRacuna);

            decimal skupajBlago = 0, skupajBlagoRazl = 0, skupajStoritve = 0, skupajTotal = 0;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var zs = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                var sifra = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                var naziv = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                var kartica = reader.GetInt32(3);
                var vrednost = reader.GetDecimal(4);
                var nabavna = reader.GetDecimal(5);
                var jeBlago = kartica == 1;
                var tip = jeBlago ? "B" : "S";
                var razlika = jeBlago ? vrednost - nabavna : 0m;
                var blagoRazlRow = jeBlago ? razlika : vrednost;

                if (jeBlago) { skupajBlago += vrednost; skupajBlagoRazl += razlika; }
                else { skupajStoritve += vrednost; }
                skupajTotal += blagoRazlRow;

                if (naziv.Length > 25) naziv = naziv[..25];
                sb.AppendLine($"{zs,4} {sifra,-10} {naziv,-25} {kartica,3} {tip,-3} {vrednost,12:N2} {(jeBlago ? nabavna.ToString("N2") : ""),12} {(jeBlago ? razlika.ToString("N2") : ""),12} {blagoRazlRow,12:N2}");
            }
            sb.AppendLine(new string('-', 100));
            sb.AppendLine($"{"SKUPAJ Blago:",-50} {skupajBlago,50:N2}");
            sb.AppendLine($"{"SKUPAJ BlagoRazl:",-50} {skupajBlagoRazl,50:N2}");
            sb.AppendLine($"{"SKUPAJ Storitve:",-50} {skupajStoritve,50:N2}");
            sb.AppendLine($"{"SKUPAJ:",-50} {skupajTotal,50:N2}");
        }
        else
        {
            // TIP 0,1,6: FA_DOBAVNICA_KNJIZBA + interni
            sb.AppendLine("TIP 0/1/6: knjižbe iz FA_DOBAVNICA_KNJIZBA");

            // Interni lookup
            var nabavnaInterni = new Dictionary<(int, int, int), decimal>();
            await using (var cmdI = new FbCommand(@"
                SELECT I.POVEZAVA_STEVILKA, I.POVEZAVA_LETO, IK.ZS_SESTAVA,
                       SUM(COALESCE(IK.NABAVNA_VREDNOST, 0))
                FROM FA_INTERNI I
                JOIN FA_INTERNI_KNJIZBA IK ON I.STEVILKA = IK.STEVILKA AND I.LETO = IK.LETO
                JOIN FA_DOBAVNICA D ON I.POVEZAVA_STEVILKA = D.STEVILKA AND I.POVEZAVA_LETO = D.LETO
                WHERE I.POVEZAVA_TIP = 51
                  AND D.RACUN_STEVILKA = @Stevilka AND D.RACUN_LETO = @Leto
                GROUP BY I.POVEZAVA_STEVILKA, I.POVEZAVA_LETO, IK.ZS_SESTAVA", connection))
            {
                cmdI.Parameters.AddWithValue("@Stevilka", stevilkaRacuna);
                cmdI.Parameters.AddWithValue("@Leto", letoRacuna);
                await using var rdr = await cmdI.ExecuteReaderAsync();
                while (await rdr.ReadAsync())
                    nabavnaInterni[(rdr.GetInt32(0), rdr.GetInt32(1), rdr.GetInt32(2))] = rdr.GetDecimal(3);
            }
            sb.AppendLine($"Interni lookup: {nabavnaInterni.Count} zapisov");
            sb.AppendLine();

            sb.AppendLine($"{"Dob",6} {"ZS",4} {"Artikel",-10} {"Naziv",-25} {"KA",3} {"Tip",-3} {"VREDNOST",12} {"NAB_DOB",12} {"NAB_INT",12} {"RAZLIKA",12} {"BLAGORAZL",12}");
            sb.AppendLine(new string('-', 120));

            await using var cmd = new FbCommand(@"
                SELECT D.STEVILKA, D.LETO, K.ZS, K.SIFRA, A.NAZIV,
                       COALESCE(A.KARTICA_ARTIKLA, -1), COALESCE(K.VREDNOST, 0), COALESCE(K.NABAVNA_VREDNOST, 0)
                FROM FA_DOBAVNICA D
                LEFT JOIN FA_DOBAVNICA_KNJIZBA K ON D.LETO = K.LETO AND D.STEVILKA = K.STEVILKA
                LEFT JOIN FA_ARTIKEL A ON A.SIFRA = K.SIFRA
                WHERE D.RACUN_STEVILKA = @Stevilka AND D.RACUN_LETO = @Leto
                ORDER BY D.STEVILKA, K.ZS", connection);
            cmd.Parameters.AddWithValue("@Stevilka", stevilkaRacuna);
            cmd.Parameters.AddWithValue("@Leto", letoRacuna);

            decimal skupajBlago = 0, skupajBlagoRazl = 0, skupajStoritve = 0, skupajTotal = 0;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var dobSt = reader.GetInt32(0);
                var dobLeto = reader.GetInt32(1);
                var zs = reader.GetInt32(2);
                var sifra = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim();
                var naziv = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim();
                var kartica = reader.GetInt32(5);
                var vrednost = reader.GetDecimal(6);
                var nabDob = reader.GetDecimal(7);
                var nabInt = nabavnaInterni.GetValueOrDefault((dobSt, dobLeto, zs), -1);
                var imaInterni = nabInt >= 0;

                string tip;
                var nabavna = 0m;
                var razlika = 0m;
                decimal blagoRazlRow;

                if (kartica == 1)
                {
                    tip = "B1"; nabavna = nabDob; razlika = vrednost - nabavna;
                    blagoRazlRow = razlika; skupajBlago += vrednost; skupajBlagoRazl += razlika;
                }
                else if (kartica == 0 && imaInterni)
                {
                    tip = "B0"; nabavna = nabInt; razlika = vrednost - nabavna;
                    blagoRazlRow = razlika; skupajBlago += vrednost; skupajBlagoRazl += razlika;
                }
                else
                {
                    tip = "S"; blagoRazlRow = vrednost; skupajStoritve += vrednost;
                }
                skupajTotal += blagoRazlRow;

                if (naziv.Length > 25) naziv = naziv[..25];
                var isBlago = tip.StartsWith('B');
                sb.AppendLine($"{dobSt,6} {zs,4} {sifra,-10} {naziv,-25} {kartica,3} {tip,-3} {vrednost,12:N2} {(kartica == 1 ? nabDob.ToString("N2") : ""),12} {(kartica == 0 && imaInterni ? nabInt.ToString("N2") : ""),12} {(isBlago ? razlika.ToString("N2") : ""),12} {blagoRazlRow,12:N2}");
            }
            sb.AppendLine(new string('-', 120));
            sb.AppendLine($"{"SKUPAJ Blago:",-50} {skupajBlago,70:N2}");
            sb.AppendLine($"{"SKUPAJ BlagoRazl:",-50} {skupajBlagoRazl,70:N2}");
            sb.AppendLine($"{"SKUPAJ Storitve:",-50} {skupajStoritve,70:N2}");
            sb.AppendLine($"{"SKUPAJ:",-50} {skupajTotal,70:N2}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Pridobi naloge za partnerja
    /// </summary>
    public async Task<List<PartnerNalogDto>> GetNalogiAsync(int sifraPartnerja, ObdobjeRange obdobje)
    {
        var result = new List<PartnerNalogDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT N.STEVILKA, N.ZACETEK_DATUM, COALESCE(N.FAKTURIRANA, 0),
                   N.NAZIV1, N.NAZIV2, N.NAZIV3, N.NAZIV4, N.NAZIV5,
                   N.NAZIV6, N.NAZIV7, N.NAZIV8, N.NAZIV9,
                   N.POTNIK, K.PRIIMEK, K.IME,
                   N.ZACETEK_URA, N.KONEC_URA, N.LETO
            FROM FA_DN_NALOG N
            LEFT JOIN FA_KOMERCIALIST K ON N.POTNIK = K.SIFRA
            WHERE N.PARTNER = @Partner AND N.ZACETEK_DATUM >= @Od AND N.ZACETEK_DATUM < @DoEks
            ORDER BY N.ZACETEK_DATUM DESC, N.STEVILKA DESC", connection);

        cmd.Parameters.AddWithValue("@Partner", sifraPartnerja);
        cmd.Parameters.AddWithValue("@Od", obdobje.Od);
        cmd.Parameters.AddWithValue("@DoEks", obdobje.Do.AddDays(1));

        var helpdeskNalogi = new List<(PartnerNalogDto Dto, int Leto)>();

        // Preload OBRACUN_DN minute za leto 2025 (samo ce obdobje prekriva leto 2025)
        var obracunDnMinute = new Dictionary<string, int>();
        if (obdobje.Od.Year <= 2025 && obdobje.Do.Year >= 2025)
        {
            await using var cmdDn = new FbCommand(
                "SELECT STEVILKA, MINUTE_NALOGA FROM OBRACUN_DN WHERE LETO = 2025 AND MINUTE_NALOGA IS NOT NULL", connection);
            await using var readerDn = await cmdDn.ExecuteReaderAsync();
            while (await readerDn.ReadAsync())
            {
                var st = readerDn.IsDBNull(0) ? "" : readerDn.GetString(0).Trim();
                var min = readerDn.GetInt32(1);
                obracunDnMinute[st] = min;
            }
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var nazivi = new List<string>();
            for (int i = 3; i <= 11; i++)
            {
                if (!reader.IsDBNull(i))
                {
                    var val = reader.GetString(i).Trim();
                    if (val.Length > 0) nazivi.Add(val);
                }
            }

            var potnik = reader.IsDBNull(12) ? "" : reader.GetString(12).Trim();
            var priimek = reader.IsDBNull(13) ? "" : reader.GetString(13).Trim();
            var ime = reader.IsDBNull(14) ? "" : reader.GetString(14).Trim();
            var serviser = string.IsNullOrEmpty(ime) ? priimek : $"{priimek} {ime}".Trim();

            var stevilka = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
            var nalogLeto = reader.GetInt32(17);
            var trajanje = 0m;

            if (int.TryParse(stevilka, out var stNum) && stNum >= 1000000 && stNum <= 1999999)
            {
                // helpdesk — trajanje se doda po drugem queryju
            }
            else if (nalogLeto == 2025 && obracunDnMinute.TryGetValue(stevilka, out var dnMinute))
            {
                // leto 2025 — trajanje iz OBRACUN_DN
                trajanje = dnMinute;
            }
            else
            {
                var zacetek = reader.IsDBNull(15) ? (DateTime?)null : reader.GetDateTime(15);
                var konec = reader.IsDBNull(16) ? (DateTime?)null : reader.GetDateTime(16);
                if (zacetek != null && konec != null)
                {
                    var razlika = konec.Value - zacetek.Value;
                    if (razlika.TotalMinutes < 0)
                        razlika = razlika.Add(TimeSpan.FromDays(1));
                    trajanje = (decimal)razlika.TotalMinutes;
                }
            }

            var dto = new PartnerNalogDto
            {
                Stevilka = stevilka,
                Datum = reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                Obracunan = reader.GetInt32(2) == 1 ? "DA" : "",
                Serviser = serviser,
                Opis = string.Join(Environment.NewLine, nazivi),
                Trajanje = trajanje
            };

            if (int.TryParse(stevilka, out var stNum2) && stNum2 >= 1000000 && stNum2 <= 1999999)
                helpdeskNalogi.Add((dto, nalogLeto));

            result.Add(dto);
        }

        // Helpdesk nalogi — trajanje iz knjižbe 047512
        if (helpdeskNalogi.Count > 0)
        {
            var stevilkeIn = string.Join(",", helpdeskNalogi.Select(n => $"'{n.Dto.Stevilka.Replace("'", "''")}'"));
            var letaIn = string.Join(",", helpdeskNalogi.Select(n => n.Leto).Distinct());

            await using var cmd2 = new FbCommand($@"
                SELECT STEVILKA, LETO, SUM(COALESCE(KOLICINA, 0))
                FROM FA_DN_NALOG_KNJ
                WHERE STEVILKA IN ({stevilkeIn}) AND LETO IN ({letaIn}) AND SIFRA = '047512'
                GROUP BY STEVILKA, LETO", connection);

            await using var reader2 = await cmd2.ExecuteReaderAsync();
            var hdMap = new Dictionary<string, decimal>();
            while (await reader2.ReadAsync())
            {
                var st = reader2.IsDBNull(0) ? "" : reader2.GetString(0).Trim();
                var lt = reader2.GetInt32(1);
                hdMap[$"{st}_{lt}"] = reader2.GetDecimal(2);
            }

            foreach (var (nalog, nLeto) in helpdeskNalogi)
            {
                if (hdMap.TryGetValue($"{nalog.Stevilka}_{nLeto}", out var min))
                    nalog.Trajanje = min;
            }
        }

        return result;
    }

    /// <summary>
    /// Pridobi knjižbe za raèun glede na TIP_RACUNA
    /// </summary>
    public async Task<List<RacunKnjizbaDto>> GetRacunKnjizbeAsync(string stevilka, int leto, int tipRacuna)
    {
        return tipRacuna switch
        {
            2 or 5 => await GetKnjizbeIzRacunaAsync(stevilka, leto),
            0 or 1 or 6 => await GetKnjizbeIzDobavniceAsync(stevilka, leto),
            7 or 8 or 12 => await GetKnjizbeIzNalogaAsync(stevilka, leto),
            _ => await GetKnjizbeIzRacunaAsync(stevilka, leto)
        };
    }

    /// <summary>
    /// Knjižbe iz FA_RACUN_KNJIZBA (TIP_RACUNA = 0, 5)
    /// </summary>
    private async Task<List<RacunKnjizbaDto>> GetKnjizbeIzRacunaAsync(string stevilka, int leto)
    {
        var result = new List<RacunKnjizbaDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT k.ZS, k.SIFRA, k.KOLICINA, k.PRODAJNA_CENA, k.PRODAJNA_VREDNOST, k.RABAT1,
                   a.NAZIV, a.NAZIV2, a.ENOTA
            FROM FA_RACUN_KNJIZBA k
            LEFT JOIN FA_ARTIKEL a ON k.SIFRA = a.SIFRA
            WHERE k.STEVILKA = @Stevilka AND k.LETO = @Leto
            ORDER BY k.ZS", connection);

        cmd.Parameters.AddWithValue("@Stevilka", int.Parse(stevilka));
        cmd.Parameters.AddWithValue("@Leto", leto);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var naziv = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim();
            var naziv2 = reader.IsDBNull(7) ? "" : reader.GetString(7).Trim();

            result.Add(new RacunKnjizbaDto
            {
                Zs = reader.GetInt32(0),
                SifraArtikla = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                NazivArtikla = ArtikelHelper.GetFullName(naziv, naziv2),
                Enota = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
                Kolicina = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                ProdajnaCena = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                ProdajnaVrednost = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                Rabat1 = reader.IsDBNull(5) ? null : reader.GetDecimal(5)
            });
        }

        return result;
    }

    /// <summary>
    /// Knjižbe iz FA_DOBAVNICA_KNJIZBA prek FA_DOBAVNICA (TIP_RACUNA = 1, 2, 6)
    /// </summary>
    private async Task<List<RacunKnjizbaDto>> GetKnjizbeIzDobavniceAsync(string stevilkaRacuna, int letoRacuna)
    {
        var result = new List<RacunKnjizbaDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT K.ZS, K.SIFRA, K.KOLICINA, K.PRODAJNA_CENA, K.PRODAJNA_VREDNOST, K.RABAT1,
                   A.NAZIV, A.NAZIV2, A.ENOTA
            FROM FA_DOBAVNICA D
            LEFT JOIN FA_DOBAVNICA_KNJIZBA K ON D.LETO = K.LETO AND D.STEVILKA = K.STEVILKA
            LEFT JOIN FA_ARTIKEL A ON A.SIFRA = K.SIFRA
            WHERE D.RACUN_STEVILKA = @Stevilka AND D.RACUN_LETO = @Leto
            ORDER BY K.ZS", connection);

        cmd.Parameters.AddWithValue("@Stevilka", stevilkaRacuna);
        cmd.Parameters.AddWithValue("@Leto", letoRacuna);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var naziv = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim();
            var naziv2 = reader.IsDBNull(7) ? "" : reader.GetString(7).Trim();

            result.Add(new RacunKnjizbaDto
            {
                Zs = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                SifraArtikla = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                NazivArtikla = ArtikelHelper.GetFullName(naziv, naziv2),
                Enota = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
                Kolicina = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                ProdajnaCena = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                ProdajnaVrednost = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                Rabat1 = reader.IsDBNull(5) ? null : reader.GetDecimal(5)
            });
        }

        return result;
    }

    /// <summary>
    /// Knjižbe iz FA_DN_NALOG_KNJ prek FA_DN_NALOG (TIP_RACUNA = 7, 8, 12)
    /// </summary>
    private async Task<List<RacunKnjizbaDto>> GetKnjizbeIzNalogaAsync(string stevilkaRacuna, int letoRacuna)
    {
        var result = new List<RacunKnjizbaDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT K.ZS, K.SIFRA, K.KOLICINA, K.PRODAJNA_CENA, K.PRODAJNA_VREDNOST, K.RABAT1,
                   A.NAZIV, A.NAZIV2, A.ENOTA
            FROM FA_DN_NALOG D
            LEFT JOIN FA_DN_NALOG_KNJ K ON D.LETO = K.LETO AND D.STEVILKA = K.STEVILKA
            LEFT JOIN FA_ARTIKEL A ON A.SIFRA = K.SIFRA
            WHERE D.RACUN_STEVILKA = @Stevilka AND D.RACUN_LETO = @Leto
              AND K.FAKTURIRA <> 0
            ORDER BY K.ZS", connection);

        cmd.Parameters.AddWithValue("@Stevilka", stevilkaRacuna);
        cmd.Parameters.AddWithValue("@Leto", letoRacuna);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var naziv = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim();
            var naziv2 = reader.IsDBNull(7) ? "" : reader.GetString(7).Trim();

            result.Add(new RacunKnjizbaDto
            {
                Zs = reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
                SifraArtikla = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                NazivArtikla = ArtikelHelper.GetFullName(naziv, naziv2),
                Enota = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
                Kolicina = reader.IsDBNull(2) ? null : reader.GetDecimal(2),
                ProdajnaCena = reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                ProdajnaVrednost = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                Rabat1 = reader.IsDBNull(5) ? null : reader.GetDecimal(5)
            });
        }

        return result;
    }

    /// <summary>
    /// Pridobi pogodbe za partnerja, ki veljajo v danem letu.
    /// Pogodba velja, èe VELJA_DO je NULL ali VELJA_DO >= 1.1.leto
    /// </summary>
    public async Task<List<PartnerPogodbaDto>> GetPogodbeAsync(int sifraPartnerja, ObdobjeRange obdobje)
    {
        var result = new List<PartnerPogodbaDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT STEVILKA, LETO, ST_POGODBE, DATUM, PRVI_RACUN_OD, VELJA_DO,
                   NA_KOLIKO_MESECEV, ST_MINUT, OPOMBA
            FROM FA_POGODBE
            WHERE PARTNER = @Partner
              AND (VELJA_DO IS NULL OR VELJA_DO >= @Od)
            ORDER BY VELJA_DO DESC, STEVILKA DESC", connection);

        cmd.Parameters.AddWithValue("@Partner", sifraPartnerja);
        cmd.Parameters.AddWithValue("@Od", obdobje.Od);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PartnerPogodbaDto
            {
                Stevilka = reader.GetInt32(0),
                Leto = reader.GetInt32(1),
                StPogodbe = reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                Datum = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                PrviRacunOd = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                VeljaDo = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                NaKolikoMesecev = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                StMinut = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                Opomba = reader.IsDBNull(8) ? null : reader.GetString(8).Trim()
            });
        }

        return result;
    }

    /// <summary>
    /// Pridobi postavke pogodbe z nazivi artiklov.
    /// Meseci se prikažejo kot "1234567890AB" — samo znaki za mesece z vnosom.
    /// </summary>
    public async Task<List<PogodbaPozicijaDto>> GetPogodbePozicijeAsync(int stevilka, int leto)
    {
        var result = new List<PogodbaPozicijaDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT P.ZS, P.SIFRA, A.NAZIV, A.NAZIV2, P.KOLICINA, P.PRODAJNA_CENA, P.MESECI, P.RABAT1
            FROM FA_POGODBE_POS P
            LEFT JOIN FA_ARTIKEL A ON A.SIFRA = P.SIFRA
            WHERE P.STEVILKA = @Stevilka AND P.LETO = @Leto
            ORDER BY P.ZS", connection);

        cmd.Parameters.AddWithValue("@Stevilka", stevilka);
        cmd.Parameters.AddWithValue("@Leto", leto);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var naziv = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
            var naziv2 = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim();
            var meseci = reader.IsDBNull(6) ? null : reader.GetString(6).Trim();

            result.Add(new PogodbaPozicijaDto
            {
                Pozicija = reader.GetInt32(0),
                SifraArtikla = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
                NazivArtikla = ArtikelHelper.GetFullName(naziv, naziv2),
                Kolicina = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                ProdajnaCena = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                Meseci = FormatMeseci(meseci),
                Rabat1 = reader.IsDBNull(7) ? null : reader.GetDecimal(7)
            });
        }

        return result;
    }

    /// <summary>
    /// Pridobi število pogodb in skupne minute za vse partnerje v letu.
    /// </summary>
    public async Task<Dictionary<int, (int Count, int Minutes)>> GetPogodbeAggAsync(ObdobjeRange obdobje)
    {
        var result = new Dictionary<int, (int Count, int Minutes)>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT PARTNER, COUNT(*), SUM(COALESCE(ST_MINUT, 0))
            FROM FA_POGODBE
            WHERE VELJA_DO IS NULL OR VELJA_DO >= @Od
            GROUP BY PARTNER", connection);

        cmd.Parameters.AddWithValue("@Od", obdobje.Od);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var partner = reader.GetInt32(0);
            var count = reader.GetInt32(1);
            var minutes = reader.GetInt32(2);
            result[partner] = (count, minutes);
        }

        return result;
    }

    /// <summary>
    /// Formatira MESECI string: "1234567890AB" — prikaže samo znake za mesece z vnosom.
    /// Vhodni format iz baze: "01,02,03,...,12," (comma-separated dvomestne številke mesecev)
    /// Pozicija: jan=1, feb=2, ..., sep=9, okt=0, nov=A, dec=B
    /// </summary>
    private static string? FormatMeseci(string? meseci)
    {
        if (string.IsNullOrEmpty(meseci)) return null;

        const string znaki = "1234567890AB";
        var prisotni = new HashSet<int>();

        foreach (var del in meseci.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(del.Trim(), out var mesec) && mesec >= 1 && mesec <= 12)
                prisotni.Add(mesec);
        }

        if (prisotni.Count == 0) return null;

        var chars = new char[12];
        for (int i = 0; i < 12; i++)
            chars[i] = prisotni.Contains(i + 1) ? znaki[i] : '.';

        return new string(chars);
    }
}

public class PartnerRacunDto
{
    public int SifraKupca { get; set; }
    public string Stevilka { get; set; } = "";
    public int Leto { get; set; }
    public DateTime? Datum { get; set; }
    public decimal ZnesekKoncni { get; set; }
    public int TipRacuna { get; set; }
    public decimal Blago { get; set; }
    public decimal BlagoNab { get; set; }
    public decimal Storitve { get; set; }
    public decimal Skupaj => Storitve + BlagoNab;
    public string Key => $"{Stevilka}_{Leto}";
}

public class PartnerNalogDto
{
    public string Stevilka { get; set; } = "";
    public DateTime? Datum { get; set; }
    public string Obracunan { get; set; } = "";
    public string Serviser { get; set; } = "";
    public string Opis { get; set; } = "";
    public decimal Trajanje { get; set; }
}

public class RacunKnjizbaDto
{
    public int Zs { get; set; }
    public string? SifraArtikla { get; set; }
    public string? NazivArtikla { get; set; }
    public string? Enota { get; set; }
    public decimal? Kolicina { get; set; }
    public decimal? ProdajnaCena { get; set; }
    public decimal? ProdajnaVrednost { get; set; }
    public decimal? Rabat1 { get; set; }
    public decimal? NetoVrednost => ProdajnaVrednost.HasValue
        ? ProdajnaVrednost.Value * (1 - (Rabat1 ?? 0) / 100)
        : null;
}

public class PartnerPogodbaDto
{
    public int Stevilka { get; set; }
    public int Leto { get; set; }
    public string? StPogodbe { get; set; }
    public DateTime? Datum { get; set; }
    public DateTime? PrviRacunOd { get; set; }
    public DateTime? VeljaDo { get; set; }
    public int? NaKolikoMesecev { get; set; }
    public int? StMinut { get; set; }
    public string? Opomba { get; set; }
    public string Key => $"{Stevilka}_{Leto}";
}

public class PogodbaPozicijaDto
{
    public int Pozicija { get; set; }
    public string? SifraArtikla { get; set; }
    public string? NazivArtikla { get; set; }
    public decimal? Kolicina { get; set; }
    public decimal? ProdajnaCena { get; set; }
    public string? Meseci { get; set; }
    public decimal? Rabat1 { get; set; }
}
