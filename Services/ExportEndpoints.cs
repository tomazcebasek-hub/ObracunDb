using ClosedXML.Excel;
using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data;
using ObracunDb.Data.Entities;

namespace ObracunDb.Services;

public static class ExportEndpoints
{
    public static async Task<IResult> KoriscenjePredracuni(FirebirdConnectionManager cm, ParametriService parametri)
    {
        var paramMesec = parametri.GetInt(ObracunParam.MesecObracuna);
        var paramLeto = parametri.GetInt(ObracunParam.LetoObracuna);
        var datumOd = new DateTime(2026, 1, 1);
        var datumDo = new DateTime(paramLeto, paramMesec, 1).AddMonths(1).AddDays(-1);

        var mesecLabels = new List<(string Key, string Label)>();
        var d = new DateTime(2026, 1, 1);
        var konec = new DateTime(paramLeto, paramMesec, 1);
        while (d <= konec)
        {
            mesecLabels.Add(($"{d.Month}-{d.Year % 100}", $"{d.Month}.{d.Year % 100}"));
            d = d.AddMonths(1);
        }

        await using var connection = cm.GetConnection();
        await connection.OpenAsync();

        // 1. Paket minute slovar
        var paketMinute = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new FbCommand("SELECT TRIM(ARTIKEL), MINUT FROM OBRACUN_PAKET_MINUTE", connection))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                paketMinute[reader.GetString(0)] = reader.GetInt32(1);
        }

        if (paketMinute.Count == 0)
            return Results.File(new MemoryStream(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "koriscenje-predracuni.xlsx");

        var sifraIn = string.Join(",", paketMinute.Keys.Select(s => "'" + s.Replace("'", "''") + "'"));

        // 2. Predračuni z minutami (status 2/5 ali plačani)
        var predracuni = new Dictionary<(string Stevilka, int Leto), (int Partner, DateTime? Datum, int Minute)>();

        var sqlPred = "SELECT fp.STEVILKA, fp.LETO, fp.SIFRA_KUPCA, fp.DATUM, TRIM(k.SIFRA), CAST(k.KOLICINA AS INTEGER)"
            + " FROM FA_PREDRACUN fp"
            + " JOIN FA_PREDRACUN_KNJIZBA k ON k.STEVILKA = fp.STEVILKA AND k.LETO = fp.LETO"
            + $" WHERE fp.DATUM >= '{datumOd:yyyy-MM-dd}' AND fp.DATUM <= '{datumDo:yyyy-MM-dd}'"
            + " AND TRIM(k.SIFRA) IN (" + sifraIn + ")"
            + " AND ("
            + "   fp.STANJE IN (2, 5)"
            + "   OR EXISTS ("
            + "     SELECT 1 FROM FA_RACUN_PLACILO rp"
            + "     WHERE rp.PREDRACUN_STEVILKA = fp.STEVILKA AND rp.PREDRACUN_LETO = fp.LETO"
            + "       AND (rp.ZNESEK + COALESCE(rp.SCONTO, 0)) > 0"
            + "   )"
            + " )";

        await using (var cmd = new FbCommand(sqlPred, connection))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var stevilka = reader.GetString(0).Trim();
                var leto = reader.GetInt32(1);
                var partner = reader.GetInt32(2);
                var datum = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3);
                var sifra = reader.GetString(4);
                var kol = reader.GetInt32(5);

                if (!paketMinute.TryGetValue(sifra, out var minutNaArtikel)) continue;
                var minute = kol * minutNaArtikel;
                var key = (stevilka, leto);

                if (predracuni.TryGetValue(key, out var existing))
                    predracuni[key] = (partner, datum, existing.Minute + minute);
                else
                    predracuni[key] = (partner, datum, minute);
            }
        }

        if (predracuni.Count == 0)
            return Results.File(new MemoryStream(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "koriscenje-predracuni.xlsx");

        // 3. Poraba minut iz OBRACUN_PORABA_MINUT (TIP=1) po predračunu + obračunski mesec
        var poraba = new Dictionary<(string, int), Dictionary<string, int>>();

        await using (var cmd = new FbCommand(@"
            SELECT TRIM(pm.PREDRACUN_STEVILKA), pm.PREDRACUN_LETO, pm.MESEC, pm.LETO, SUM(pm.KOLICINA)
            FROM OBRACUN_PORABA_MINUT pm
            WHERE pm.TIP = 1
              AND pm.PREDRACUN_STEVILKA IS NOT NULL AND pm.PREDRACUN_LETO IS NOT NULL
            GROUP BY TRIM(pm.PREDRACUN_STEVILKA), pm.PREDRACUN_LETO, pm.MESEC, pm.LETO", connection))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var predSt = reader.GetString(0);
                var predLeto = reader.GetInt32(1);
                var porMesec = reader.GetInt32(2);
                var porLeto = reader.GetInt32(3);
                var kolicina = reader.GetInt32(4);

                var mesecKey = $"{porMesec}-{porLeto % 100}";
                var predKey = (predSt, predLeto);

                if (!poraba.TryGetValue(predKey, out var dict))
                {
                    dict = new Dictionary<string, int>();
                    poraba[predKey] = dict;
                }
                dict.TryGetValue(mesecKey, out var ex);
                dict[mesecKey] = ex + kolicina;
            }
        }

        // 4. Nazivi partnerjev
        var partnerIds = predracuni.Values.Select(v => v.Partner).Distinct().ToList();
        var nazivi = new Dictionary<int, string?>();
        if (partnerIds.Count > 0)
        {
            var partnerIn = string.Join(",", partnerIds);
            await using var cmd = new FbCommand($"SELECT SIFRA, NAZIV FROM PARTNER WHERE SIFRA IN ({partnerIn})", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                nazivi[reader.GetInt32(0)] = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
        }

        // 5. Generiraj XLSX
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Koriščenje");

        var headers = new List<string> { "Šifra", "Partner", "Pred.št.", "Leto", "Datum", "Minute" };
        foreach (var m in mesecLabels)
            headers.Add(m.Label);
        headers.Add("Preostalo");

        for (int c = 0; c < headers.Count; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;

        int row = 2;
        foreach (var kv in predracuni.OrderBy(x => nazivi.GetValueOrDefault(x.Value.Partner) ?? "").ThenBy(x => x.Value.Datum))
        {
            ws.Cell(row, 1).Value = kv.Value.Partner;
            ws.Cell(row, 2).Value = nazivi.GetValueOrDefault(kv.Value.Partner);
            ws.Cell(row, 3).Value = kv.Key.Stevilka;
            ws.Cell(row, 4).Value = kv.Key.Leto;
            if (kv.Value.Datum.HasValue)
                ws.Cell(row, 5).Value = kv.Value.Datum.Value;
            ws.Cell(row, 5).Style.DateFormat.Format = "dd.MM.yyyy";
            ws.Cell(row, 6).Value = kv.Value.Minute;

            poraba.TryGetValue((kv.Key.Stevilka, kv.Key.Leto), out var porabaDict);
            int skupajPorabljeno = 0;
            int col = 7;
            foreach (var m in mesecLabels)
            {
                var val = porabaDict?.GetValueOrDefault(m.Key) ?? 0;
                if (val != 0)
                    ws.Cell(row, col).Value = val;
                skupajPorabljeno += val;
                col++;
            }
            ws.Cell(row, col).Value = kv.Value.Minute - skupajPorabljeno;
            row++;
        }

        ws.Columns().AdjustToContents();

        var ms2 = new MemoryStream();
        wb.SaveAs(ms2);
        ms2.Position = 0;

        return Results.File(ms2,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "koriscenje-predracuni.xlsx");
    }
}
