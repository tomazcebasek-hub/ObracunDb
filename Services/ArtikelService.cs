using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data.DTOs;
using ObracunDb.Data.Entities;

namespace ObracunDb.Services;

/// <summary>
/// Servis za delo z artikli iz FA_ARTIKEL tabele
/// </summary>
public class ArtikelService
{
    private readonly Data.FirebirdConnectionManager _connectionManager;

    public ArtikelService(Data.FirebirdConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    /// <summary>
    /// Pridobi vse artikle iz baze
    /// </summary>
    public async Task<List<ArtikelDto>> GetAllArtikliAsync()
    {
        var artikli = new List<ArtikelDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(
            "SELECT SIFRA, NAZIV, NAZIV2 FROM FA_ARTIKEL ORDER BY SIFRA", 
            connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var entity = new FaArtikel
            {
                Sifra = reader.GetString(0).Trim(),
                Naziv = reader.GetString(1).Trim(),
                Naziv2 = reader.IsDBNull(2) ? null : reader.GetString(2).Trim()
            };

            // Mapiranje Entity -> DTO
            artikli.Add(MapToDto(entity));
        }

        return artikli;
    }

    /// <summary>
    /// Pridobi artikel po šifri
    /// </summary>
    public async Task<ArtikelDto?> GetArtikelBySifraAsync(string sifra)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(
            "SELECT SIFRA, NAZIV, NAZIV2 FROM FA_ARTIKEL WHERE SIFRA = @Sifra", 
            connection);
        command.Parameters.AddWithValue("@Sifra", sifra);

        await using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            var entity = new FaArtikel
            {
                Sifra = reader.GetString(0).Trim(),
                Naziv = reader.GetString(1).Trim(),
                Naziv2 = reader.IsDBNull(2) ? null : reader.GetString(2).Trim()
            };

            return MapToDto(entity);
        }

        return null;
    }

    /// <summary>
    /// Pridobi naziv, enoto in prodajno ceno artikla po šifri.
    /// </summary>
    public async Task<(string Naziv, string Enota, decimal ProdajnaCena)?> GetArtikelDetailBySifraAsync(string sifra)
    {
        if (string.IsNullOrWhiteSpace(sifra))
            return null;

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(
            "SELECT NAZIV, NAZIV2, ENOTA, PRODAJNA_CENA FROM FA_ARTIKEL WHERE SIFRA = @Sifra",
            connection);
        command.Parameters.AddWithValue("@Sifra", sifra);

        await using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            var naziv = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
            var naziv2 = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
            var fullNaziv = string.IsNullOrWhiteSpace(naziv2) ? naziv : $"{naziv} / {naziv2}";
            var enota = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
            var cena = reader.IsDBNull(3) ? 0m : reader.GetDecimal(3);
            return (fullNaziv, enota, cena);
        }

        return null;
    }

    /// <summary>
    /// Mapiranje iz Entity v DTO
    /// </summary>
    private static ArtikelDto MapToDto(FaArtikel entity)
    {
        return new ArtikelDto
        {
            Sifra = entity.Sifra,
            Naziv = entity.Naziv,
            Naziv2 = entity.Naziv2
        };
    }
}
