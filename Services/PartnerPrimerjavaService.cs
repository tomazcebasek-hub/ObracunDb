using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data.DTOs;

namespace ObracunDb.Services;

/// <summary>
/// Servis za primerjavo zneska računov partnerjev med dvema letoma.
/// Znesek je izračunan enako kot v glavnem gridu Partnerji:
/// SUM(ZNESEK_KONCNI / 1.22) iz FA_RACUN, brez TIP_RACUNA = 4.
/// </summary>
public class PartnerPrimerjavaService
{
    private readonly Data.FirebirdConnectionManager _connectionManager;

    public PartnerPrimerjavaService(Data.FirebirdConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    /// <summary>
    /// Vrne seznam partnerjev z vsoto računov za obe leti.
    /// Partner je vključen, če ima vsaj en račun v katerem koli od obeh let.
    /// </summary>
    /// <param name="leto1">Prvo (prejšnje) leto — stolpec Znesek1.</param>
    /// <param name="leto2">Drugo (tekoče) leto — stolpec Znesek2.</param>
    public async Task<List<PartnerPrimerjavaDto>> GetPrimerjavaAsync(int leto1, int leto2)
    {
        var result = new List<PartnerPrimerjavaDto>();

        var minLeto = Math.Min(leto1, leto2);
        var maxLeto = Math.Max(leto1, leto2);
        var od = new DateTime(minLeto, 1, 1);
        var doEks = new DateTime(maxLeto + 1, 1, 1);
        var danes = DateTime.Today;
        var dan = Math.Min(danes.Day, DateTime.DaysInMonth(leto1, danes.Month));
        var doDanesEks = new DateTime(leto1, danes.Month, dan).AddDays(1);

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(@"
            SELECT r.SIFRA_KUPCA, p.NAZIV,
                   COALESCE(SUM(CASE WHEN EXTRACT(YEAR FROM r.DATUM) = @Leto1 THEN r.ZNESEK_KONCNI / 1.22 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN EXTRACT(YEAR FROM r.DATUM) = @Leto2 THEN r.ZNESEK_KONCNI / 1.22 ELSE 0 END), 0),
                   COALESCE(SUM(CASE WHEN r.DATUM >= @OdLeto1 AND r.DATUM < @DoDanesEks THEN r.ZNESEK_KONCNI / 1.22 ELSE 0 END), 0)
            FROM FA_RACUN r
            LEFT JOIN PARTNER p ON r.SIFRA_KUPCA = p.SIFRA
            WHERE r.DATUM >= @Od AND r.DATUM < @DoEks
              AND COALESCE(r.TIP_RACUNA, 0) <> 4
            GROUP BY r.SIFRA_KUPCA, p.NAZIV
            ORDER BY p.NAZIV", connection);

        cmd.Parameters.AddWithValue("@Leto1", leto1);
        cmd.Parameters.AddWithValue("@Leto2", leto2);
        cmd.Parameters.AddWithValue("@Od", od);
        cmd.Parameters.AddWithValue("@DoEks", doEks);
        cmd.Parameters.AddWithValue("@OdLeto1", new DateTime(leto1, 1, 1));
        cmd.Parameters.AddWithValue("@DoDanesEks", doDanesEks);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PartnerPrimerjavaDto
            {
                Sifra = reader.GetInt32(0),
                Naziv = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                Znesek1 = reader.GetDecimal(2),
                Znesek2 = reader.GetDecimal(3),
                Znesek1DoDanes = reader.GetDecimal(4)
            });
        }

        return result;
    }
}
