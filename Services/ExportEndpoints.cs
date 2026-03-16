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
        var pretDatum = new DateTime(paramLeto, paramMesec, 1).AddMonths(-1);
        var pretMesec = pretDatum.Month;
        var pretLeto = pretDatum.Year;
        var datumOd = new DateTime(pretLeto, pretMesec, 1);
        var datumDo = new DateTime(paramLeto, paramMesec, 1).AddMonths(1).AddDays(-1);

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

        // 2. "Vse" minute iz predračunov
        var vseMinute = new Dictionary<int, (int Pret, int Tre)>();

        var sqlVse = "SELECT fp.SIFRA_KUPCA, EXTRACT(YEAR FROM fp.DATUM), EXTRACT(MONTH FROM fp.DATUM), TRIM(k.SIFRA), CAST(k.KOLICINA AS INTEGER)"
            + " FROM FA_PREDRACUN fp"
            + " JOIN FA_PREDRACUN_KNJIZBA k ON k.STEVILKA = fp.STEVILKA AND k.LETO = fp.LETO"
            + $" WHERE fp.DATUM >= '{datumOd:yyyy-MM-dd}' AND fp.DATUM <= '{datumDo:yyyy-MM-dd}'"
            + " AND TRIM(k.SIFRA) IN (" + sifraIn + ")"
            + " AND ("
            + "   fp.STANJE = 5"
            + "   OR EXISTS ("
            + "     SELECT 1 FROM FA_RACUN_PLACILO rp"
            + "     WHERE rp.PREDRACUN_STEVILKA = fp.STEVILKA AND rp.PREDRACUN_LETO = fp.LETO"
            + "       AND (rp.ZNESEK + COALESCE(rp.SCONTO, 0)) > 0"
            + "   )"
            + " )";

        await using (var cmd = new FbCommand(sqlVse, connection))
        {
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var partner = reader.GetInt32(0);
                var predLetoVal = reader.GetInt32(1);
                var predMesecVal = reader.GetInt32(2);
                var sifra = reader.GetString(3);
                var kol = reader.GetInt32(4);

                if (!paketMinute.TryGetValue(sifra, out var minutNaArtikel)) continue;
                var minute = kol * minutNaArtikel;

                bool jePret = predLetoVal == pretLeto && predMesecVal == pretMesec;
                bool jeTre = predLetoVal == paramLeto && predMesecVal == paramMesec;

                vseMinute.TryGetValue(partner, out var existing);
                if (jePret) vseMinute[partner] = (existing.Pret + minute, existing.Tre);
                else if (jeTre) vseMinute[partner] = (existing.Pret, existing.Tre + minute);
            }
        }

        // 3. Koriščene minute
        var korPreteklo = new Dictionary<int, (int Pret, int Tre)>();
        var korMesec = new Dictionary<int, (int Pret, int Tre)>();

        await using (var cmd = new FbCommand(@"
            SELECT pm.PARTNER,
                   EXTRACT(YEAR FROM fp.DATUM) AS PRED_LETO,
                   EXTRACT(MONTH FROM fp.DATUM) AS PRED_MESEC,
                   pm.MESEC, pm.LETO,
                   SUM(pm.KOLICINA) AS SKUPAJ
            FROM OBRACUN_PORABA_MINUT pm
            JOIN FA_PREDRACUN fp ON fp.STEVILKA = pm.PREDRACUN_STEVILKA AND fp.LETO = pm.PREDRACUN_LETO
            WHERE pm.TIP = 1
              AND fp.DATUM >= @DatumOd AND fp.DATUM <= @DatumDo
            GROUP BY pm.PARTNER, EXTRACT(YEAR FROM fp.DATUM), EXTRACT(MONTH FROM fp.DATUM), pm.MESEC, pm.LETO", connection))
        {
            cmd.Parameters.AddWithValue("@DatumOd", datumOd);
            cmd.Parameters.AddWithValue("@DatumDo", datumDo);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var partner = reader.GetInt32(0);
                var predLetoVal = reader.GetInt32(1);
                var predMesecVal = reader.GetInt32(2);
                var porMesec = reader.GetInt32(3);
                var porLeto = reader.GetInt32(4);
                var skupaj = reader.GetInt32(5);

                bool jePret = predLetoVal == pretLeto && predMesecVal == pretMesec;
                bool jeTre = predLetoVal == paramLeto && predMesecVal == paramMesec;
                if (!jePret && !jeTre) continue;

                bool jePretekloObdobje = porLeto < paramLeto || (porLeto == paramLeto && porMesec < paramMesec);
                bool jeMesecObdobje = porLeto == paramLeto && porMesec == paramMesec;

                if (jePretekloObdobje)
                {
                    korPreteklo.TryGetValue(partner, out var ex);
                    if (jePret) korPreteklo[partner] = (ex.Pret + skupaj, ex.Tre);
                    else korPreteklo[partner] = (ex.Pret, ex.Tre + skupaj);
                }
                else if (jeMesecObdobje)
                {
                    korMesec.TryGetValue(partner, out var ex);
                    if (jePret) korMesec[partner] = (ex.Pret + skupaj, ex.Tre);
                    else korMesec[partner] = (ex.Pret, ex.Tre + skupaj);
                }
            }
        }

        // 4. Nazivi partnerjev — samo partnerji s porabo v tekočem mesecu
        var vsiPartnerji = korMesec.Keys.ToList();
        var nazivi = new Dictionary<int, string?>();
        if (vsiPartnerji.Count > 0)
        {
            var partnerIn = string.Join(",", vsiPartnerji);
            await using var cmd = new FbCommand($"SELECT SIFRA, NAZIV FROM PARTNER WHERE SIFRA IN ({partnerIn})", connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                nazivi[reader.GetInt32(0)] = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
        }

        // 5. Generiraj XLSX
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Koriščenje");

        var pretLabel = $"{pretMesec}.{pretLeto % 100}";
        var treLabel = $"{paramMesec}.{paramLeto % 100}";
        string[] headers = ["Šifra", "Partner",
            $"{pretLabel} Vse", $"{pretLabel} Pret.", $"{pretLabel} Mes.", $"{pretLabel} Preost.",
            $"{treLabel} Vse", $"{treLabel} Pret.", $"{treLabel} Mes.", $"{treLabel} Preost."];
        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;

        int row = 2;
        foreach (var partner in vsiPartnerji.OrderBy(p => nazivi.GetValueOrDefault(p) ?? "").ThenBy(p => p))
        {
            vseMinute.TryGetValue(partner, out var vse);
            korPreteklo.TryGetValue(partner, out var pret);
            korMesec.TryGetValue(partner, out var mes);

            ws.Cell(row, 1).Value = partner;
            ws.Cell(row, 2).Value = nazivi.GetValueOrDefault(partner);
            ws.Cell(row, 3).Value = vse.Pret;
            ws.Cell(row, 4).Value = pret.Pret;
            ws.Cell(row, 5).Value = mes.Pret;
            ws.Cell(row, 6).Value = vse.Pret - pret.Pret - mes.Pret;
            ws.Cell(row, 7).Value = vse.Tre;
            ws.Cell(row, 8).Value = pret.Tre;
            ws.Cell(row, 9).Value = mes.Tre;
            ws.Cell(row, 10).Value = vse.Tre - pret.Tre - mes.Tre;
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
