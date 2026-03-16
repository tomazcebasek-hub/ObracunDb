using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data.DTOs;

namespace ObracunDb.Services;

/// <summary>
/// Servis za delo z ločenimi računi (tabela OBRACUN_LOCENI_RACUNI)
/// </summary>
public class LoceniRacuniService
{
    private readonly Data.FirebirdConnectionManager _connectionManager;
    private readonly AuthService _authService;

    public LoceniRacuniService(Data.FirebirdConnectionManager connectionManager, AuthService authService)
    {
        _connectionManager = connectionManager;
        _authService = authService;
    }

    private string TrenutniUporabnik => _authService.CurrentUser?.UporabniskoIme ?? "?";

    /// <summary>
    /// Pridobi vse vnose ločenih računov z nazivi
    /// </summary>
    public async Task<List<LoceniRacuniGridDto>> GetAllAsync()
    {
        var result = new List<LoceniRacuniGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT 
                lr.ID, lr.PARTNER, lr.PRODAJALNA, lr.POGODBA_STEVILKA, lr.POGODBA_LETO,
                lr.DATUM_VNOSA, lr.UPORABNIK,
                p.NAZIV,
                pr.NAZIV,
                pg.ST_POGODBE
            FROM OBRACUN_LOCENI_RACUNI lr
            LEFT JOIN PARTNER p ON lr.PARTNER = p.SIFRA
            LEFT JOIN FA_PRODAJALNA pr ON lr.PRODAJALNA = pr.SIFRA
            LEFT JOIN FA_POGODBE pg ON lr.POGODBA_STEVILKA = pg.STEVILKA AND lr.POGODBA_LETO = pg.LETO
            ORDER BY p.NAZIV, lr.POGODBA_STEVILKA, lr.POGODBA_LETO", connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new LoceniRacuniGridDto
            {
                Id = reader.GetInt32(0),
                Partner = reader.GetInt32(1),
                Prodajalna = reader.GetInt32(2),
                PogodbaStevilka = reader.GetInt32(3),
                PogodbaLeto = reader.GetInt32(4),
                DatumVnosa = reader.GetDateTime(5),
                Uporabnik = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim(),
                NazivPartnerja = reader.IsDBNull(7) ? "" : reader.GetString(7).Trim(),
                NazivProdajalne = reader.IsDBNull(8) ? "" : reader.GetString(8).Trim(),
                StPogodbe = reader.IsDBNull(9) ? null : reader.GetString(9).Trim()
            });
        }

        return result;
    }

    /// <summary>
    /// Pridobi prodajalne za partnerja (FA_PRODAJALNA.KUPEC = partner)
    /// </summary>
    public async Task<List<ProdajalnaDto>> GetProdajalneZaPartnerja(int partner)
    {
        var result = new List<ProdajalnaDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT SIFRA, NAZIV
            FROM FA_PRODAJALNA
            WHERE KUPEC = @Partner
            ORDER BY SIFRA", connection);

        command.Parameters.AddWithValue("@Partner", partner);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ProdajalnaDto
            {
                Sifra = reader.GetInt32(0),
                Naziv = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim()
            });
        }

        return result;
    }

    /// <summary>
    /// Pridobi veljavne pogodbe za partnerja (danes ali v prihodnosti)
    /// </summary>
    public async Task<List<PogodbaDodelitevDto>> GetVeljavnePogodbeZaPartnerja(int partner)
    {
        var result = new List<PogodbaDodelitevDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT STEVILKA, LETO, ST_POGODBE, VELJA_DO
            FROM FA_POGODBE
            WHERE PARTNER = @Partner AND (VELJA_DO >= CURRENT_DATE OR VELJA_DO IS NULL)
            ORDER BY STEVILKA, LETO", connection);

        command.Parameters.AddWithValue("@Partner", partner);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PogodbaDodelitevDto
            {
                Stevilka = reader.GetInt32(0),
                Leto = reader.GetInt32(1),
                StPogodbe = reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                VeljaDo = reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                IzbranaProdajalna = 0
            });
        }

        return result;
    }

    /// <summary>
    /// Preveri ali partner že ima vnose v tabeli
    /// </summary>
    public async Task<bool> PartnerZeObstajaAsync(int partner)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(
            "SELECT COUNT(*) FROM OBRACUN_LOCENI_RACUNI WHERE PARTNER = @Partner", connection);
        command.Parameters.AddWithValue("@Partner", partner);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    /// <summary>
    /// Pridobi obstoječe dodelitve za partnerja (za urejanje)
    /// </summary>
    public async Task<Dictionary<string, int>> GetObstojeceDodelitveAsync(int partner)
    {
        var result = new Dictionary<string, int>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT POGODBA_STEVILKA, POGODBA_LETO, PRODAJALNA
            FROM OBRACUN_LOCENI_RACUNI
            WHERE PARTNER = @Partner", connection);

        command.Parameters.AddWithValue("@Partner", partner);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var key = $"{reader.GetInt32(0)}/{reader.GetInt32(1)}";
            result[key] = reader.GetInt32(2);
        }

        return result;
    }

    /// <summary>
    /// Shrani dodelitve (izbriše obstoječe in vstavi nove)
    /// </summary>
    public async Task SaveAsync(int partner, List<(int pogodbaStevilka, int pogodbaLeto, int prodajalna)> dodelitve)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // Izbriši obstoječe
        await using (var cmdDel = new FbCommand(
            "DELETE FROM OBRACUN_LOCENI_RACUNI WHERE PARTNER = @Partner", connection))
        {
            cmdDel.Parameters.AddWithValue("@Partner", partner);
            await cmdDel.ExecuteNonQueryAsync();
        }

        // Vstavi nove
        foreach (var (pogodbaStevilka, pogodbaLeto, prodajalna) in dodelitve)
        {
            await using var cmdIns = new FbCommand(@"
                INSERT INTO OBRACUN_LOCENI_RACUNI 
                    (PARTNER, PRODAJALNA, POGODBA_STEVILKA, POGODBA_LETO, DATUM_VNOSA, UPORABNIK)
                VALUES 
                    (@Partner, @Prodajalna, @PogodbaStevilka, @PogodbaLeto, @DatumVnosa, @Uporabnik)", connection);

            cmdIns.Parameters.AddWithValue("@Partner", partner);
            cmdIns.Parameters.AddWithValue("@Prodajalna", prodajalna);
            cmdIns.Parameters.AddWithValue("@PogodbaStevilka", pogodbaStevilka);
            cmdIns.Parameters.AddWithValue("@PogodbaLeto", pogodbaLeto);
            cmdIns.Parameters.AddWithValue("@DatumVnosa", DateTime.Now);
            cmdIns.Parameters.AddWithValue("@Uporabnik", TrenutniUporabnik);

            await cmdIns.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Izbriši vse vnose za partnerja
    /// </summary>
    public async Task DeletePartnerAsync(int partner)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(
            "DELETE FROM OBRACUN_LOCENI_RACUNI WHERE PARTNER = @Partner", connection);
        command.Parameters.AddWithValue("@Partner", partner);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Pridobi vse partnerje za pickup dialog
    /// </summary>
    public async Task<List<PartnerFilterDto>> GetAllPartnerjeAsync()
    {
        var result = new List<PartnerFilterDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT SIFRA, NAZIV
            FROM PARTNER
            ORDER BY NAZIV", connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PartnerFilterDto
            {
                Sifra = reader.GetInt32(0),
                Naziv = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim()
            });
        }

        return result;
    }
}
