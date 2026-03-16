using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data.DTOs;

namespace ObracunDb.Services;

/// <summary>
/// Servis za delo s partner minutami (tabela OBRACUN_MINUTE)
/// </summary>
public class PartnerMinuteService
{
    private readonly Data.FirebirdConnectionManager _connectionManager;
    private readonly AuthService _authService;

    public PartnerMinuteService(Data.FirebirdConnectionManager connectionManager, AuthService authService)
    {
        _connectionManager = connectionManager;
        _authService = authService;
    }

    private string TrenutniUporabnik => _authService.CurrentUser?.UporabniskoIme ?? "?";

    private static async Task ZapisiRevizijo(FbConnection connection, string uporabnik,
        string polje, string? staraVrednost, string? novaVrednost, string? kontekst, int idVTabeli)
    {
        await using var cmd = new FbCommand(@"
            INSERT INTO OBRACUN_REVIZIJA (DATUM, UPORABNIK, TABELA, POLJE, STARA_VREDNOST, NOVA_VREDNOST, KONTEKST, ID_V_TABELI)
            VALUES (@Datum, @Uporabnik, 'OBRACUN_MINUTE', @Polje, @StaraVrednost, @NovaVrednost, @Kontekst, @IdVTabeli)", connection);

        cmd.Parameters.AddWithValue("@Datum", DateTime.Now);
        cmd.Parameters.AddWithValue("@Uporabnik", uporabnik);
        cmd.Parameters.AddWithValue("@Polje", polje);
        cmd.Parameters.AddWithValue("@StaraVrednost", (object?)staraVrednost ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NovaVrednost", (object?)novaVrednost ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Kontekst", (object?)kontekst ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IdVTabeli", idVTabeli);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Pridobi vse partner minute z nazivom partnerja
    /// </summary>
    public async Task<List<PartnerMinuteGridDto>> GetAllAsync()
    {
        var result = new List<PartnerMinuteGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT 
                m.ID, m.PARTNER, m.DATUM, m.MINUT, m.VELJAVNOST_MESECIH,
                m.OPOMBA, m.ZACETEK_MESEC, m.ZACETEK_LETO,
                p.NAZIV, m.UPORABNIK
            FROM OBRACUN_MINUTE m
            LEFT JOIN PARTNER p ON m.PARTNER = p.SIFRA
            ORDER BY m.DATUM DESC, m.ID DESC", connection);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new PartnerMinuteGridDto
            {
                Id = reader.GetInt32(0),
                Partner = reader.GetInt32(1),
                Datum = reader.GetDateTime(2),
                Minut = reader.GetDecimal(3),
                VeljavnostMesecih = reader.GetInt32(4),
                Opomba = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                ZacetekMesec = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                ZacetekLeto = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                NazivPartnerja = reader.IsDBNull(8) ? "" : reader.GetString(8).Trim(),
                Uporabnik = reader.IsDBNull(9) ? "" : reader.GetString(9).Trim()
            });
        }

        return result;
    }

    /// <summary>
    /// Pridobi partner minute za doloèenega partnerja z izraèunom preostalih minut.
    /// Poraba se bere iz OBRACUN_PORABA_MINUT za mesece PRED mesec/leto.
    /// </summary>
    public async Task<List<PartnerMinuteGridDto>> GetByPartnerAsync(int partner, int? predMesec = null, int? predLeto = null)
    {
        var result = new List<PartnerMinuteGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT 
                m.ID, m.PARTNER, m.DATUM, m.MINUT, m.VELJAVNOST_MESECIH,
                m.OPOMBA, m.ZACETEK_MESEC, m.ZACETEK_LETO,
                p.NAZIV, m.UPORABNIK
            FROM OBRACUN_MINUTE m
            LEFT JOIN PARTNER p ON m.PARTNER = p.SIFRA
            WHERE m.PARTNER = @Partner
            ORDER BY m.DATUM DESC, m.ID DESC", connection);

        command.Parameters.AddWithValue("@Partner", partner);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new PartnerMinuteGridDto
            {
                Id = reader.GetInt32(0),
                Partner = reader.GetInt32(1),
                Datum = reader.GetDateTime(2),
                Minut = reader.GetDecimal(3),
                VeljavnostMesecih = reader.GetInt32(4),
                Opomba = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                ZacetekMesec = reader.IsDBNull(6) ? null : reader.GetInt32(6),
                ZacetekLeto = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                NazivPartnerja = reader.IsDBNull(8) ? "" : reader.GetString(8).Trim(),
                Uporabnik = reader.IsDBNull(9) ? "" : reader.GetString(9).Trim()
            });
        }

        if (result.Count == 0 || predMesec == null || predLeto == null)
            return result;

        // Preberi porabo iz OBRACUN_PORABA_MINUT za vse ID-je tega partnerja
        var idsIn = string.Join(",", result.Select(r => r.Id));

        var poraba = new Dictionary<int, List<PorabaMesecDto>>();
        await using var cmd2 = new FbCommand($@"
            SELECT ID_OBRACUN_MINUTE, MESEC, LETO, KOLICINA
            FROM OBRACUN_PORABA_MINUT
            WHERE TIP = 2 AND ID_OBRACUN_MINUTE IN ({idsIn})
            ORDER BY LETO, MESEC", connection);

        await using var reader2 = await cmd2.ExecuteReaderAsync();
        while (await reader2.ReadAsync())
        {
            var idMin = reader2.GetInt32(0);
            var mes = reader2.GetInt32(1);
            var let = reader2.GetInt32(2);
            var kol = reader2.GetInt32(3);

            if (!poraba.ContainsKey(idMin))
                poraba[idMin] = new();

            poraba[idMin].Add(new PorabaMesecDto { Mesec = mes, Leto = let, Kolicina = kol });
        }

        // Izraèunaj preostalo: Minut - vsota porabe PRED mesec/leto
        foreach (var item in result)
        {
            if (poraba.TryGetValue(item.Id, out var meseci))
            {
                item.PorabaPoMesecih = meseci;
                var porabljeno = meseci
                    .Where(m => m.Leto < predLeto.Value || (m.Leto == predLeto.Value && m.Mesec < predMesec.Value))
                    .Sum(m => m.Kolicina);
                item.Preostalo = Math.Max(0, (int)item.Minut - porabljeno);
            }
            else
            {
                item.Preostalo = (int)item.Minut;
            }
        }

        return result;
    }

    /// <summary>
    public async Task<List<PartnerFilterDto>> GetPartnerjeAsync()
    {
        var result = new List<PartnerFilterDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT DISTINCT m.PARTNER, p.NAZIV
            FROM OBRACUN_MINUTE m
            LEFT JOIN PARTNER p ON m.PARTNER = p.SIFRA
            ORDER BY p.NAZIV", connection);

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

    /// <summary>
    /// Pridobi vse partnerje za vnos
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

    /// <summary>
    /// Dodaj nov zapis
    /// </summary>
    public async Task<int> AddAsync(int partner, decimal minut, int veljavnostMesecih, string? opomba, int? zacetekMesec, int? zacetekLeto)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            INSERT INTO OBRACUN_MINUTE (PARTNER, DATUM, MINUT, VELJAVNOST_MESECIH, OPOMBA, ZACETEK_MESEC, ZACETEK_LETO, UPORABNIK)
            VALUES (@Partner, @Datum, @Minut, @VeljavnostMesecih, @Opomba, @ZacetekMesec, @ZacetekLeto, @Uporabnik)
            RETURNING ID", connection);

        command.Parameters.AddWithValue("@Partner", partner);
        command.Parameters.AddWithValue("@Datum", DateTime.Now);
        command.Parameters.AddWithValue("@Minut", minut);
        command.Parameters.AddWithValue("@VeljavnostMesecih", veljavnostMesecih);
        command.Parameters.AddWithValue("@Opomba", (object?)opomba ?? DBNull.Value);
        command.Parameters.AddWithValue("@ZacetekMesec", (object?)zacetekMesec ?? DBNull.Value);
        command.Parameters.AddWithValue("@ZacetekLeto", (object?)zacetekLeto ?? DBNull.Value);
        command.Parameters.AddWithValue("@Uporabnik", TrenutniUporabnik);

        var result = await command.ExecuteScalarAsync();
        var newId = Convert.ToInt32(result);

        var kontekst = $"Partner {partner}";
        await ZapisiRevizijo(connection, TrenutniUporabnik, "VNOS", null,
            $"Minut={minut}, Veljavnost={veljavnostMesecih}, Zaèetek={zacetekMesec}/{zacetekLeto}",
            kontekst, newId);

        return newId;
    }

    /// <summary>
    /// Posodobi zapis
    /// </summary>
    public async Task UpdateAsync(int id, int partner, decimal minut, int veljavnostMesecih, string? opomba, int? zacetekMesec, int? zacetekLeto)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // Preberi stare vrednosti
        int staraPartner = 0; decimal staraMinut = 0; int staraVeljavnost = 0;
        string? staraOpomba = null; int? staraZacMesec = null; int? staraZacLeto = null;
        await using (var cmdOld = new FbCommand("SELECT PARTNER, MINUT, VELJAVNOST_MESECIH, OPOMBA, ZACETEK_MESEC, ZACETEK_LETO FROM OBRACUN_MINUTE WHERE ID = @Id", connection))
        {
            cmdOld.Parameters.AddWithValue("@Id", id);
            await using var r = await cmdOld.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                staraPartner = r.GetInt32(0);
                staraMinut = r.GetDecimal(1);
                staraVeljavnost = r.GetInt32(2);
                staraOpomba = r.IsDBNull(3) ? null : r.GetString(3).Trim();
                staraZacMesec = r.IsDBNull(4) ? null : r.GetInt32(4);
                staraZacLeto = r.IsDBNull(5) ? null : r.GetInt32(5);
            }
        }

        await using var command = new FbCommand(@"
            UPDATE OBRACUN_MINUTE
            SET PARTNER = @Partner, MINUT = @Minut, VELJAVNOST_MESECIH = @VeljavnostMesecih,
                OPOMBA = @Opomba, ZACETEK_MESEC = @ZacetekMesec, ZACETEK_LETO = @ZacetekLeto
            WHERE ID = @Id", connection);

        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Partner", partner);
        command.Parameters.AddWithValue("@Minut", minut);
        command.Parameters.AddWithValue("@VeljavnostMesecih", veljavnostMesecih);
        command.Parameters.AddWithValue("@Opomba", (object?)opomba ?? DBNull.Value);
        command.Parameters.AddWithValue("@ZacetekMesec", (object?)zacetekMesec ?? DBNull.Value);
        command.Parameters.AddWithValue("@ZacetekLeto", (object?)zacetekLeto ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();

        // Zapiši revizijo za vsako spremenjeno polje
        var kontekst = $"Partner {partner}";
        var user = TrenutniUporabnik;
        if (staraPartner != partner)
            await ZapisiRevizijo(connection, user, "PARTNER", staraPartner.ToString(), partner.ToString(), kontekst, id);
        if (staraMinut != minut)
            await ZapisiRevizijo(connection, user, "MINUT", staraMinut.ToString("F0"), minut.ToString("F0"), kontekst, id);
        if (staraVeljavnost != veljavnostMesecih)
            await ZapisiRevizijo(connection, user, "VELJAVNOST_MESECIH", staraVeljavnost.ToString(), veljavnostMesecih.ToString(), kontekst, id);
        if ((staraOpomba ?? "") != (opomba ?? ""))
            await ZapisiRevizijo(connection, user, "OPOMBA", staraOpomba, opomba, kontekst, id);
        if (staraZacMesec != zacetekMesec)
            await ZapisiRevizijo(connection, user, "ZACETEK_MESEC", staraZacMesec?.ToString(), zacetekMesec?.ToString(), kontekst, id);
        if (staraZacLeto != zacetekLeto)
            await ZapisiRevizijo(connection, user, "ZACETEK_LETO", staraZacLeto?.ToString(), zacetekLeto?.ToString(), kontekst, id);
    }

    /// <summary>
    /// Izbriši zapis
    /// </summary>
    public async Task DeleteAsync(int id)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        // Preberi podatke pred brisanjem
        int partner = 0; decimal minut = 0; int veljavnost = 0;
        int? zacMesec = null; int? zacLeto = null;
        await using (var cmdOld = new FbCommand("SELECT PARTNER, MINUT, VELJAVNOST_MESECIH, ZACETEK_MESEC, ZACETEK_LETO FROM OBRACUN_MINUTE WHERE ID = @Id", connection))
        {
            cmdOld.Parameters.AddWithValue("@Id", id);
            await using var r = await cmdOld.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                partner = r.GetInt32(0);
                minut = r.GetDecimal(1);
                veljavnost = r.GetInt32(2);
                zacMesec = r.IsDBNull(3) ? null : r.GetInt32(3);
                zacLeto = r.IsDBNull(4) ? null : r.GetInt32(4);
            }
        }

        await using var command = new FbCommand(@"
            DELETE FROM OBRACUN_MINUTE
            WHERE ID = @Id", connection);

        command.Parameters.AddWithValue("@Id", id);

        await command.ExecuteNonQueryAsync();

        var kontekst = $"Partner {partner}";
        await ZapisiRevizijo(connection, TrenutniUporabnik, "BRISANJE",
            $"Minut={minut:F0}, Veljavnost={veljavnost}, Zaèetek={zacMesec}/{zacLeto}", null,
            kontekst, id);
    }
}

/// <summary>
/// DTO za filter partnerjev
/// </summary>
public class PartnerFilterDto
{
    public int Sifra { get; set; }
    public string Naziv { get; set; } = string.Empty;
    public string DisplayName => $"{Sifra} - {Naziv}";
}
