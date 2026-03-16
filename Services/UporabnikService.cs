using System.Security.Cryptography;
using System.Text;
using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data;
using ObracunDb.Data.DTOs;
using ObracunDb.Data.Entities;

namespace ObracunDb.Services;

/// <summary>
/// Servis za delo z uporabniki iz tabele OBRACUN_UPORABNIK
/// </summary>
public class UporabnikService
{
    private readonly FirebirdConnectionManager _connectionManager;

    public UporabnikService(FirebirdConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    /// <summary>
    /// Preveri uporabniško ime in geslo. Vrne uporabnika ali null.
    /// </summary>
    public async Task<ObracunUporabnik?> ValidateAsync(string uporabniskoIme, string geslo)
    {
        var hash = HashPassword(geslo);

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT ID, UPORABNISKO_IME, GESLO_HASH, VLOGA, AKTIVEN, PRVA_PRIJAVA,
                   DATUM_USTVARJEN, DATUM_ZADNJA_PRIJAVA
            FROM OBRACUN_UPORABNIK
            WHERE UPPER(UPORABNISKO_IME) = UPPER(@ime) AND GESLO_HASH = @hash AND AKTIVEN = 1",
            connection);

        command.Parameters.AddWithValue("@ime", uporabniskoIme);
        command.Parameters.AddWithValue("@hash", hash);

        await using var reader = await command.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return ReadEntity(reader);
        }

        return null;
    }

    /// <summary>
    /// Pridobi uporabnika po ID-ju.
    /// </summary>
    public async Task<ObracunUporabnik?> GetByIdAsync(int id)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT ID, UPORABNISKO_IME, GESLO_HASH, VLOGA, AKTIVEN, PRVA_PRIJAVA,
                   DATUM_USTVARJEN, DATUM_ZADNJA_PRIJAVA
            FROM OBRACUN_UPORABNIK
            WHERE ID = @Id AND AKTIVEN = 1",
            connection);

        command.Parameters.AddWithValue("@Id", id);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return ReadEntity(reader);

        return null;
    }

    /// <summary>
    /// Posodobi geslo uporabnika in oznaèi, da ni veè prva prijava.
    /// </summary>
    public async Task UpdatePasswordAsync(int userId, string novoGeslo)
    {
        var hash = HashPassword(novoGeslo);

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            UPDATE OBRACUN_UPORABNIK
            SET GESLO_HASH = @hash, PRVA_PRIJAVA = 0
            WHERE ID = @id",
            connection);

        command.Parameters.AddWithValue("@hash", hash);
        command.Parameters.AddWithValue("@id", userId);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Posodobi datum zadnje prijave.
    /// </summary>
    public async Task UpdateZadnjaPrijavaAsync(int userId)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            UPDATE OBRACUN_UPORABNIK
            SET DATUM_ZADNJA_PRIJAVA = @datum
            WHERE ID = @id",
            connection);

        command.Parameters.AddWithValue("@datum", DateTime.Now);
        command.Parameters.AddWithValue("@id", userId);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Pridobi vse uporabnike za grid.
    /// </summary>
    public async Task<List<UporabnikGridDto>> GetAllAsync()
    {
        var result = new List<UporabnikGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT ID, UPORABNISKO_IME, VLOGA, AKTIVEN, PRVA_PRIJAVA,
                   DATUM_USTVARJEN, DATUM_ZADNJA_PRIJAVA
            FROM OBRACUN_UPORABNIK
            ORDER BY UPORABNISKO_IME", connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new UporabnikGridDto
            {
                Id = reader.GetInt32(0),
                UporabniskoIme = reader.GetString(1).Trim(),
                Vloga = (UporabnikVloga)reader.GetInt32(2),
                Aktiven = reader.GetInt32(3) == 1,
                PrvaPrijava = reader.GetInt32(4) == 1,
                DatumUstvarjen = reader.GetDateTime(5),
                DatumZadnjaPrijava = reader.IsDBNull(6) ? null : reader.GetDateTime(6)
            });
        }

        return result;
    }

    /// <summary>
    /// Ustvari novega uporabnika z default geslom 123456.
    /// </summary>
    public async Task<int> CreateAsync(string uporabniskoIme, UporabnikVloga vloga)
    {
        var hash = HashPassword("123456");

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            INSERT INTO OBRACUN_UPORABNIK (UPORABNISKO_IME, GESLO_HASH, VLOGA, AKTIVEN, PRVA_PRIJAVA, DATUM_USTVARJEN)
            VALUES (@ime, @hash, @vloga, 1, 1, CURRENT_TIMESTAMP)
            RETURNING ID", connection);

        command.Parameters.AddWithValue("@ime", uporabniskoIme.Trim());
        command.Parameters.AddWithValue("@hash", hash);
        command.Parameters.AddWithValue("@vloga", (int)vloga);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>
    /// Posodobi uporabniško ime, vlogo in aktivnost.
    /// </summary>
    public async Task UpdateAsync(int id, string uporabniskoIme, UporabnikVloga vloga, bool aktiven)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            UPDATE OBRACUN_UPORABNIK
            SET UPORABNISKO_IME = @ime, VLOGA = @vloga, AKTIVEN = @aktiven
            WHERE ID = @id", connection);

        command.Parameters.AddWithValue("@ime", uporabniskoIme.Trim());
        command.Parameters.AddWithValue("@vloga", (int)vloga);
        command.Parameters.AddWithValue("@aktiven", aktiven ? 1 : 0);
        command.Parameters.AddWithValue("@id", id);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Ponastavi geslo uporabnika na default (123456) in oznaèi prvo prijavo.
    /// </summary>
    public async Task ResetPasswordAsync(int id)
    {
        var hash = HashPassword("123456");

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            UPDATE OBRACUN_UPORABNIK
            SET GESLO_HASH = @hash, PRVA_PRIJAVA = 1
            WHERE ID = @id", connection);

        command.Parameters.AddWithValue("@hash", hash);
        command.Parameters.AddWithValue("@id", id);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Izbriši uporabnika.
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            DELETE FROM OBRACUN_UPORABNIK WHERE ID = @id", connection);

        command.Parameters.AddWithValue("@id", id);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Preveri ali uporabniško ime že obstaja (za validacijo).
    /// </summary>
    public async Task<bool> ExistsAsync(string uporabniskoIme, int? excludeId = null)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        var sql = excludeId.HasValue
            ? "SELECT COUNT(*) FROM OBRACUN_UPORABNIK WHERE UPPER(UPORABNISKO_IME) = UPPER(@ime) AND ID <> @id"
            : "SELECT COUNT(*) FROM OBRACUN_UPORABNIK WHERE UPPER(UPORABNISKO_IME) = UPPER(@ime)";

        await using var command = new FbCommand(sql, connection);
        command.Parameters.AddWithValue("@ime", uporabniskoIme.Trim());
        if (excludeId.HasValue)
            command.Parameters.AddWithValue("@id", excludeId.Value);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    /// <summary>
    /// SHA256 hash gesla ? Base64 string
    /// </summary>
    public static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    private static ObracunUporabnik ReadEntity(FbDataReader reader)
    {
        return new ObracunUporabnik
        {
            Id = reader.GetInt32(reader.GetOrdinal("ID")),
            UporabniskoIme = reader.GetString(reader.GetOrdinal("UPORABNISKO_IME")),
            GesloHash = reader.GetString(reader.GetOrdinal("GESLO_HASH")),
            Vloga = reader.GetInt32(reader.GetOrdinal("VLOGA")),
            Aktiven = reader.GetInt32(reader.GetOrdinal("AKTIVEN")),
            PrvaPrijava = reader.GetInt32(reader.GetOrdinal("PRVA_PRIJAVA")),
            DatumUstvarjen = reader.GetDateTime(reader.GetOrdinal("DATUM_USTVARJEN")),
            DatumZadnjaPrijava = reader.IsDBNull(reader.GetOrdinal("DATUM_ZADNJA_PRIJAVA"))
                ? null
                : reader.GetDateTime(reader.GetOrdinal("DATUM_ZADNJA_PRIJAVA"))
        };
    }
}
