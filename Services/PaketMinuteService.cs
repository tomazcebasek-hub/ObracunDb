using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data.DTOs;
using ObracunDb.Data.Entities;

namespace ObracunDb.Services;

/// <summary>
/// Servis za delo s paketi minut
/// </summary>
public class PaketMinuteService
{
    private readonly Data.FirebirdConnectionManager _connectionManager;

    public PaketMinuteService(Data.FirebirdConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    /// <summary>
    /// Pridobi vse pakete minut z nazivom artikla
    /// </summary>
    public async Task<List<PaketMinuteGridDto>> GetAllAsync()
    {
        var result = new List<PaketMinuteGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT 
                p.ID, p.DATUM, p.ARTIKEL, p.MINUT,
                a.NAZIV, a.NAZIV2, a.ENOTA
            FROM OBRACUN_PAKET_MINUTE p
            LEFT JOIN FA_ARTIKEL a ON p.ARTIKEL = a.SIFRA
            ORDER BY p.DATUM DESC, p.ID DESC", connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var naziv = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim();
            var naziv2 = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim();
            var enota = reader.IsDBNull(6) ? null : reader.GetString(6).Trim();

            result.Add(new PaketMinuteGridDto
            {
                Id = reader.GetInt32(0),
                Datum = reader.GetDateTime(1),
                Artikel = reader.GetString(2).Trim(),
                Minut = reader.GetInt32(3),
                NazivArtikla = ArtikelHelper.GetFullName(naziv, naziv2),
                Enota = enota
            });
        }

        return result;
    }

    /// <summary>
    /// Doda nov paket minut
    /// </summary>
    public async Task<int> AddAsync(string artikel, int minut)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            INSERT INTO OBRACUN_PAKET_MINUTE (DATUM, ARTIKEL, MINUT)
            VALUES (@Datum, @Artikel, @Minut)
            RETURNING ID", connection);

        command.Parameters.AddWithValue("@Datum", DateTime.Now);
        command.Parameters.AddWithValue("@Artikel", artikel);
        command.Parameters.AddWithValue("@Minut", minut);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Posodobi paket minut
    /// </summary>
    public async Task UpdateAsync(int id, string artikel, int minut)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            UPDATE OBRACUN_PAKET_MINUTE
            SET ARTIKEL = @Artikel, MINUT = @Minut
            WHERE ID = @Id", connection);

        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Artikel", artikel);
        command.Parameters.AddWithValue("@Minut", minut);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Izbriši paket minut
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            DELETE FROM OBRACUN_PAKET_MINUTE
            WHERE ID = @Id", connection);

        command.Parameters.AddWithValue("@Id", id);

        await command.ExecuteNonQueryAsync();
    }
}
