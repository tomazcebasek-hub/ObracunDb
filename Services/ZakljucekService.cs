using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data.Entities;

namespace ObracunDb.Services;

/// <summary>
/// Servis za zakljuèek meseca
/// </summary>
public class ZakljucekService
{
    private readonly Data.FirebirdConnectionManager _connectionManager;
    private readonly ParametriService _parametriService;

    public ZakljucekService(Data.FirebirdConnectionManager connectionManager, ParametriService parametriService)
    {
        _connectionManager = connectionManager;
        _parametriService = parametriService;
    }

    /// <summary>
    /// Pridobi statistiko delovnih nalogov za obdobje (iz FA_DN_NALOG po datumu)
    /// </summary>
    public async Task<ZakljucekStatistikaDto> GetStatistikoAsync(int leto, int mesec)
    {
        var dto = new ZakljucekStatistikaDto();
        var datumOd = new DateTime(leto, mesec, 1);
        var datumDo = datumOd.AddMonths(1);

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // Število vseh nalogov v obdobju
        await using (var cmd = new FbCommand(@"
            SELECT COUNT(*)
            FROM FA_DN_NALOG
            WHERE ZACETEK_DATUM >= @DatumOd AND ZACETEK_DATUM < @DatumDo", connection))
        {
            cmd.Parameters.AddWithValue("@DatumOd", datumOd);
            cmd.Parameters.AddWithValue("@DatumDo", datumDo);
            var result = await cmd.ExecuteScalarAsync();
            dto.SkupajNalogov = Convert.ToInt32(result);
        }

        // Število nalogov po KajObracunam (iz OBRACUN_DN)
        await using (var cmd = new FbCommand(@"
            SELECT COALESCE(d.KAJ_OBRACUNAM, -1), COUNT(*)
            FROM FA_DN_NALOG n
            LEFT JOIN OBRACUN_DN d ON n.STEVILKA = d.STEVILKA AND n.LETO = d.LETO
            WHERE n.ZACETEK_DATUM >= @DatumOd AND n.ZACETEK_DATUM < @DatumDo
            GROUP BY COALESCE(d.KAJ_OBRACUNAM, -1)
            ORDER BY 1", connection))
        {
            cmd.Parameters.AddWithValue("@DatumOd", datumOd);
            cmd.Parameters.AddWithValue("@DatumDo", datumDo);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var val = reader.GetInt32(0);
                var stevilo = reader.GetInt32(1);
                if (val == -1)
                    dto.BrezObracunDn = stevilo;
                else
                    dto.PoKajObracunam[(KajObracunam)val] = stevilo;
            }
        }

        return dto;
    }

    /// <summary>
    /// Izvedi zakljuèek meseca:
    /// 1. Iz FA_DN_NALOG preberi naloge za mesec/leto (po DATUM)
    /// 2. Za vsak nalog preveri OBRACUN_DN — èe ne obstaja, javi napako
    /// 3. Posodobi FA_DN_NALOG.FAKTURIRANA glede na KAJ_OBRACUNAM
    /// 4. Zapiši revizijsko sled v OBRACUN_REVIZIJA
    /// 5. Poveèaj mesec/leto v parametrih
    /// </summary>
    public async Task<ZakljucekRezultatDto> IzvediZakljucekAsync(int leto, int mesec, string uporabnik)
    {
        var rezultat = new ZakljucekRezultatDto();
        var datum = DateTime.Now;
        var datumOd = new DateTime(leto, mesec, 1);
        var datumDo = datumOd.AddMonths(1);

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // 1. Preberi vse naloge iz FA_DN_NALOG za obdobje
            var nalogi = new List<(string Stevilka, int Leto, int Fakturirana)>();
            await using (var cmd = new FbCommand(@"
                SELECT STEVILKA, LETO, COALESCE(FAKTURIRANA, 0)
                FROM FA_DN_NALOG
                WHERE ZACETEK_DATUM >= @DatumOd AND ZACETEK_DATUM < @DatumDo", connection))
            {
                cmd.Transaction = transaction;
                cmd.Parameters.AddWithValue("@DatumOd", datumOd);
                cmd.Parameters.AddWithValue("@DatumDo", datumDo);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    nalogi.Add((
                        reader.GetString(0).Trim(),
                        reader.GetInt32(1),
                        reader.GetInt32(2)
                    ));
                }
            }

            if (nalogi.Count == 0)
            {
                await transaction.RollbackAsync();
                throw new InvalidOperationException($"Za obdobje {mesec}/{leto} ni bilo najdenih delovnih nalogov.");
            }

            // 2. Preberi KAJ_OBRACUNAM iz OBRACUN_DN za te naloge
            var obracunDn = new Dictionary<(string Stevilka, int Leto), KajObracunam>();
            var stevilkeIn = string.Join(",", nalogi.Select(n => $"'{n.Stevilka.Replace("'", "''")}'"));
            var letaIn = string.Join(",", nalogi.Select(n => n.Leto).Distinct());

            await using (var cmd = new FbCommand($@"
                SELECT STEVILKA, LETO, KAJ_OBRACUNAM
                FROM OBRACUN_DN
                WHERE STEVILKA IN ({stevilkeIn}) AND LETO IN ({letaIn})", connection))
            {
                cmd.Transaction = transaction;

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var key = (reader.GetString(0).Trim(), reader.GetInt32(1));
                    obracunDn[key] = (KajObracunam)reader.GetInt32(2);
                }
            }

            //3.Preveri da imajo vsi nalogi zapis v OBRACUN_DN
           var manjkajoce = nalogi
               .Where(n => !obracunDn.ContainsKey((n.Stevilka, n.Leto)))
               .ToList();

            if (manjkajoce.Count > 0)
            {
                await transaction.RollbackAsync();
                var primeri = string.Join(", ", manjkajoce.Take(10).Select(n => $"{n.Stevilka}/{n.Leto}"));
                var msg = manjkajoce.Count > 10
                    ? $"{primeri} ... in še {manjkajoce.Count - 10} drugih"
                    : primeri;
                throw new InvalidOperationException(
                    $"Zakljuèek ni mogoè. {manjkajoce.Count} nalog(ov) nima zapisa v OBRACUN_DN: {msg}");
            }

            // 4. Za vsak nalog: posodobi FAKTURIRANA in zapiši revizijo
            foreach (var nalog in nalogi)
            {
                var kaj = obracunDn[(nalog.Stevilka, nalog.Leto)];
                var novaVrednost = (kaj == KajObracunam.Nedefinirano || kaj == KajObracunam.Nic)
                    ? 6 : 1;

                // Posodobi FAKTURIRANA
                await using (var cmd = new FbCommand(@"
                    UPDATE FA_DN_NALOG SET FAKTURIRANA = @Fakturirana
                    WHERE STEVILKA = @Stevilka AND LETO = @Leto", connection))
                {
                    cmd.Transaction = transaction;
                    cmd.Parameters.AddWithValue("@Fakturirana", novaVrednost);
                    cmd.Parameters.AddWithValue("@Stevilka", nalog.Stevilka);
                    cmd.Parameters.AddWithValue("@Leto", nalog.Leto);

                    var affected = await cmd.ExecuteNonQueryAsync();
                    if (affected > 0)
                        rezultat.SteviloSprememb++;
                }

                // Zapiši revizijsko sled
                await using (var cmd = new FbCommand(@"
                    INSERT INTO OBRACUN_REVIZIJA (DATUM, UPORABNIK, TABELA, POLJE, STARA_VREDNOST, NOVA_VREDNOST, KONTEKST, STEVILKA, LETO)
                    VALUES (@Datum, @Uporabnik, @Tabela, @Polje, @StaraVrednost, @NovaVrednost, @Kontekst, @Stevilka, @Leto)", connection))
                {
                    cmd.Transaction = transaction;
                    cmd.Parameters.AddWithValue("@Datum", datum);
                    cmd.Parameters.AddWithValue("@Uporabnik", uporabnik);
                    cmd.Parameters.AddWithValue("@Tabela", "FA_DN_NALOG");
                    cmd.Parameters.AddWithValue("@Polje", "FAKTURIRANA");
                    cmd.Parameters.AddWithValue("@StaraVrednost", nalog.Fakturirana.ToString());
                    cmd.Parameters.AddWithValue("@NovaVrednost", novaVrednost.ToString());
                    cmd.Parameters.AddWithValue("@Kontekst", $"Nalog {nalog.Stevilka}/{nalog.Leto}");
                    cmd.Parameters.AddWithValue("@Stevilka", nalog.Stevilka);
                    cmd.Parameters.AddWithValue("@Leto", nalog.Leto);

                    await cmd.ExecuteNonQueryAsync();
                }
            }

            await transaction.CommitAsync();
            rezultat.Uspesno = true;
        }
        catch
        {
            try { await transaction.RollbackAsync(); } catch { }
            throw;
        }

        // 5. Poveèaj mesec/leto v parametrih
        int noviMesec, novoLeto;
        if (mesec == 12)
        {
            noviMesec = 1;
            novoLeto = leto + 1;
        }
        else
        {
            noviMesec = mesec + 1;
            novoLeto = leto;
        }

        await _parametriService.SaveToDatabaseAsync(ObracunParam.MesecObracuna, noviMesec);
        await _parametriService.SaveToDatabaseAsync(ObracunParam.LetoObracuna, novoLeto);

        rezultat.NoviMesec = noviMesec;
        rezultat.NovoLeto = novoLeto;

        return rezultat;
    }
}

/// <summary>
/// DTO za statistiko zakljuèka meseca
/// </summary>
public class ZakljucekStatistikaDto
{
    public int SkupajNalogov { get; set; }
    public int BrezObracunDn { get; set; }
    public Dictionary<KajObracunam, int> PoKajObracunam { get; set; } = new();
}

/// <summary>
/// Rezultat zakljuèka meseca
/// </summary>
public class ZakljucekRezultatDto
{
    public bool Uspesno { get; set; }
    public int SteviloSprememb { get; set; }
    public int NoviMesec { get; set; }
    public int NovoLeto { get; set; }
}
