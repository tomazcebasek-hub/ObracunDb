using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data.DTOs;

namespace ObracunDb.Services;

/// <summary>
/// Servis za delo s predraèuni
/// </summary>
public class PredracunService
{
    private readonly Data.FirebirdConnectionManager _connectionManager;
    private readonly ArtikelService _artikelService;

    public PredracunService(Data.FirebirdConnectionManager connectionManager, ArtikelService artikelService)
    {
        _connectionManager = connectionManager;
        _artikelService = artikelService;
    }

    /// <summary>
    /// Pridobi vse predraèune z nazivom partnerja
    /// </summary>
    public async Task<List<PredracunGridDto>> GetAllPredracuniAsync(int odLeta = 2026)
    {
        var result = new List<PredracunGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // 1. Naložim vse predraèune
        await using var command = new FbCommand(@"
            SELECT 
                p.STEVILKA, p.LETO, p.DATUM, p.SIFRA_KUPCA, p.STANJE, p.ZNESEK_KONCNI,
                pa.NAZIV,
                pl.VSOTA_PLACIL
            FROM FA_PREDRACUN p
            LEFT JOIN PARTNER pa ON p.SIFRA_KUPCA = pa.SIFRA
            LEFT JOIN (
                SELECT PREDRACUN_STEVILKA, PREDRACUN_LETO, SUM(ZNESEK + COALESCE(SCONTO, 0)) AS VSOTA_PLACIL
                FROM FA_RACUN_PLACILO
                WHERE PREDRACUN_STEVILKA IS NOT NULL 
                  AND PREDRACUN_LETO IS NOT NULL 
                  AND PREDRACUN_LETO >= 2025
                GROUP BY PREDRACUN_STEVILKA, PREDRACUN_LETO
                HAVING SUM(ZNESEK + COALESCE(SCONTO, 0)) > 0
            ) pl ON p.STEVILKA = pl.PREDRACUN_STEVILKA AND p.LETO = pl.PREDRACUN_LETO
            WHERE p.LETO >= @OdLeta
            ORDER BY p.LETO DESC, p.STEVILKA DESC", connection);

        command.Parameters.AddWithValue("@OdLeta", odLeta);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new PredracunGridDto
            {
                Stevilka = reader.GetString(0).Trim(),
                Leto = reader.GetInt32(1),
                Datum = reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                SifraKupca = reader.GetInt32(3),
                Stanje = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                ZnesekKoncni = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                NazivPartnerja = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                Placano = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
                PlacanoIzRacunov = 0
            });
        }

        // 2. Naložim vse raèune kjer je vpisan predraèun
        var racunZneski = new Dictionary<string, decimal>();
        var racunObstaja = new HashSet<string>(); // Sledim kateri predraèuni imajo raèun

        await using var cmdRacuni = new FbCommand(@"
            SELECT 
                PREDRAC1_STEVILKA, PREDRAC1_LETO, COALESCE(PREDRAC1_ZNESEK, 0),
                PREDRAC2_STEVILKA, PREDRAC2_LETO, COALESCE(PREDRAC2_ZNESEK, 0),
                POVEZAVA_STEVILKA, POVEZAVA_LETO
            FROM FA_RACUN
            WHERE (PREDRAC1_LETO >= @OdLeta AND PREDRAC1_STEVILKA IS NOT NULL)
               OR (PREDRAC2_LETO >= @OdLeta AND PREDRAC2_STEVILKA IS NOT NULL)
               OR (POVEZAVA_LETO >= @OdLeta AND POVEZAVA_STEVILKA IS NOT NULL)", connection);

        cmdRacuni.Parameters.AddWithValue("@OdLeta", odLeta);

        await using var readerRacuni = await cmdRacuni.ExecuteReaderAsync();

        while (await readerRacuni.ReadAsync())
        {
            // PREDRAC1
            if (!readerRacuni.IsDBNull(0) && !readerRacuni.IsDBNull(1))
            {
                var stevilkaStr = readerRacuni.GetString(0).Trim();
                var leto = readerRacuni.GetInt32(1);
                var znesek = readerRacuni.GetDecimal(2);
                
                var key = $"{stevilkaStr}_{leto}";
                if (racunZneski.ContainsKey(key))
                    racunZneski[key] += znesek;
                else
                    racunZneski[key] = znesek;
                
                racunObstaja.Add(key);
            }

            // PREDRAC2
            if (!readerRacuni.IsDBNull(3) && !readerRacuni.IsDBNull(4))
            {
                var stevilkaStr = readerRacuni.GetString(3).Trim();
                var leto = readerRacuni.GetInt32(4);
                var znesek = readerRacuni.GetDecimal(5);
                
                var key = $"{stevilkaStr}_{leto}";
                if (racunZneski.ContainsKey(key))
                    racunZneski[key] += znesek;
                else
                    racunZneski[key] = znesek;
                
                racunObstaja.Add(key);
            }

            // POVEZAVA (samo oznaèim da obstaja raèun, brez zneska)
            if (!readerRacuni.IsDBNull(6) && !readerRacuni.IsDBNull(7))
            {
                var stevilkaStr = readerRacuni.GetString(6).Trim();
                var leto = readerRacuni.GetInt32(7);
                var key = $"{stevilkaStr}_{leto}";
                racunObstaja.Add(key);
            }
        }

        // 3. Združim podatke
        foreach (var predracun in result)
        {
            var key = $"{predracun.Stevilka}_{predracun.Leto}";
            
            // Nastavim znesek iz raèunov
            if (racunZneski.TryGetValue(key, out var znesek))
            {
                predracun.PlacanoIzRacunov = znesek;
            }
            
            // Èe obstaja kakršnakoli povezava na raèun, oznaèim da ima raèun
            if (racunObstaja.Contains(key))
            {
                // PlacanoIzRacunov lahko ostane 0 ali ima vrednost iz zgornjega if-a
                // RacunStatus bo vrnil "Raèun" èe PlacanoIzRacunov > 0
                // AMPAK: Èe je samo POVEZAVA (brez PREDRAC1/2), moramo nastavit fiktiven znesek
                if (!racunZneski.ContainsKey(key))
                {
                    // Nastavim zelo majhen znesek, da bo RacunStatus vrnil "Raèun"
                    predracun.PlacanoIzRacunov = 0.01m;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Pridobi knjižbe (postavke) za doloèen predraèun
    /// </summary>
    public async Task<List<PredracunKnjizbaGridDto>> GetKnjizbeAsync(string stevilka, int leto)
    {
        var result = new List<PredracunKnjizbaGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT 
                k.STEVILKA, k.LETO, k.ZS, k.SIFRA, 
                k.KOLICINA, k.PRODAJNA_CENA, k.PRODAJNA_VREDNOST, k.RABAT1,
                a.NAZIV, a.NAZIV2
            FROM FA_PREDRACUN_KNJIZBA k
            LEFT JOIN FA_ARTIKEL a ON k.SIFRA = a.SIFRA
            WHERE k.STEVILKA = @Stevilka AND k.LETO = @Leto
            ORDER BY k.ZS", connection);

        command.Parameters.AddWithValue("@Stevilka", stevilka);
        command.Parameters.AddWithValue("@Leto", leto);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var naziv = reader.IsDBNull(8) ? "" : reader.GetString(8).Trim();
            var naziv2 = reader.IsDBNull(9) ? "" : reader.GetString(9).Trim();

            result.Add(new PredracunKnjizbaGridDto
            {
                Stevilka = reader.GetString(0).Trim(),
                Leto = reader.GetInt32(1),
                Zs = reader.GetInt32(2),
                SifraArtikla = reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                NazivArtikla = ArtikelHelper.GetFullName(naziv, naziv2),
                Kolicina = reader.IsDBNull(4) ? null : reader.GetDecimal(4),
                ProdajnaCena = reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                ProdajnaVrednost = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                Rabat1 = reader.IsDBNull(7) ? null : reader.GetDecimal(7)
            });
        }

        return result;
    }
}

/// <summary>
/// Helper za združevanje imen artiklov
/// </summary>
public static class ArtikelHelper
{
    public static string GetFullName(string naziv, string? naziv2)
    {
        if (string.IsNullOrWhiteSpace(naziv2))
            return naziv;
        
        return $"{naziv} / {naziv2}";
    }
}
