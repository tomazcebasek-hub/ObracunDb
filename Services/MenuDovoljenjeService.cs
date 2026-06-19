using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data;

namespace ObracunDb.Services;

/// <summary>
/// Servis za dovoljenja vidnosti menijev po uporabniku (tabela OBRACUN_MENU_DOVOLJENJE).
/// Logika je "allow-list": obstoj vrstice pomeni, da uporabnik ta meni vidi.
/// Ce vrstice ni (npr. nov meni), uporabnik menija ne vidi, dokler ga nekdo ne vklopi.
/// </summary>
public class MenuDovoljenjeService
{
    private readonly FirebirdConnectionManager _connectionManager;

    /// <summary>
    /// Sprozi se, ko se dovoljenja shranijo (za osvezitev menija v isti seji).
    /// </summary>
    public event Action? OnDovoljenjaSpremenjena;

    public MenuDovoljenjeService(FirebirdConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    /// <summary>
    /// Vrne mnozico kljucev menijev, ki jih uporabnik vidi.
    /// </summary>
    public async Task<HashSet<string>> GetDovoljeniKljuciAsync(int uporabnikId)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(
            "SELECT MENU_KLJUC FROM OBRACUN_MENU_DOVOLJENJE WHERE UPORABNIK_ID = @id",
            connection);
        command.Parameters.AddWithValue("@id", uporabnikId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0).Trim());

        return result;
    }

    /// <summary>
    /// Shrani dovoljenja uporabnika: izbrise obstojeca in vstavi izbrane kljuce.
    /// </summary>
    public async Task SaveAsync(int uporabnikId, IEnumerable<string> dovoljeniKljuci)
    {
        var kljuci = dovoljeniKljuci
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using (var delCmd = new FbCommand(
            "DELETE FROM OBRACUN_MENU_DOVOLJENJE WHERE UPORABNIK_ID = @id", connection, (FbTransaction)transaction))
        {
            delCmd.Parameters.AddWithValue("@id", uporabnikId);
            await delCmd.ExecuteNonQueryAsync();
        }

        foreach (var kljuc in kljuci)
        {
            await using var insCmd = new FbCommand(
                "INSERT INTO OBRACUN_MENU_DOVOLJENJE (UPORABNIK_ID, MENU_KLJUC) VALUES (@id, @kljuc)",
                connection, (FbTransaction)transaction);
            insCmd.Parameters.AddWithValue("@id", uporabnikId);
            insCmd.Parameters.AddWithValue("@kljuc", kljuc);
            await insCmd.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        OnDovoljenjaSpremenjena?.Invoke();
    }
}
