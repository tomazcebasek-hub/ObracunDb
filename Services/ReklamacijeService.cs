using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data.DTOs;
using ObracunDb.Data.Entities;

namespace ObracunDb.Services;

public class ReklamacijeService
{
    private readonly Data.FirebirdConnectionManager _connectionManager;
    private readonly AuthService _authService;

    public ReklamacijeService(Data.FirebirdConnectionManager connectionManager, AuthService authService)
    {
        _connectionManager = connectionManager;
        _authService = authService;
    }

    private string TrenutniUporabnik => _authService.CurrentUser?.UporabniskoIme ?? "?";

    public async Task<List<ReklamacijaGridDto>> GetAllAsync()
    {
        var result = new List<ReklamacijaGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT
                r.ID,
                r.PARTNER,
                p.NAZIV,
                r.DATUM_ZAHTEVE,
                r.STEVILKE_POGODB,
                r.KONTAKT,
                r.TIP_PREKINITVE,
                r.RACUNI_DO_DNE,
                (SELECT FIRST 1 rp.KDO_NAJ_OBDELA
                 FROM OBRACUN_REKLAMACIJA_POS rp
                 WHERE rp.ID_REKLAMACIJA = r.ID
                 ORDER BY rp.DATUM DESC, rp.ID DESC) AS KDO_NAJ_OBDELA,
                (SELECT FIRST 1 rp.DATUM
                 FROM OBRACUN_REKLAMACIJA_POS rp
                 WHERE rp.ID_REKLAMACIJA = r.ID
                 ORDER BY rp.DATUM DESC, rp.ID DESC) AS DATUM_POSREDOVANJA,
                COALESCE(pc.STEVILO_VNOSOV, 0) AS STEVILO_VNOSOV,
                (SELECT FIRST 1 rp.STATUS_ID
                 FROM OBRACUN_REKLAMACIJA_POS rp
                 WHERE rp.ID_REKLAMACIJA = r.ID
                 ORDER BY rp.DATUM DESC, rp.ID DESC) AS ZADNJI_STATUS_ID,
                (SELECT FIRST 1 s.BARVA
                 FROM OBRACUN_REKLAMACIJA_POS rp
                 LEFT JOIN OBRACUN_REKLAMACIJA_SIFRANT s ON s.ID = rp.STATUS_ID
                 WHERE rp.ID_REKLAMACIJA = r.ID
                 ORDER BY rp.DATUM DESC, rp.ID DESC) AS ZADNJI_STATUS_BARVA,
                r.TIP_REKLAMACIJE,
                r.OPIS,
                (SELECT FIRST 1 s.NAZIV
                 FROM OBRACUN_REKLAMACIJA_POS rp
                 LEFT JOIN OBRACUN_REKLAMACIJA_SIFRANT s ON s.ID = rp.STATUS_ID
                 WHERE rp.ID_REKLAMACIJA = r.ID
                 ORDER BY rp.DATUM DESC, rp.ID DESC) AS ZADNJI_STATUS_NAZIV
            FROM OBRACUN_REKLAMACIJA r
            LEFT JOIN PARTNER p ON r.PARTNER = p.SIFRA
            LEFT JOIN (
                SELECT ID_REKLAMACIJA, COUNT(*) AS STEVILO_VNOSOV
                FROM OBRACUN_REKLAMACIJA_POS
                GROUP BY ID_REKLAMACIJA
            ) pc ON pc.ID_REKLAMACIJA = r.ID
            ORDER BY r.DATUM_ZAHTEVE DESC, r.ID DESC", connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ReklamacijaGridDto
            {
                Id = reader.GetInt32(0),
                Partner = reader.GetInt32(1),
                NazivPartnerja = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim(),
                DatumZahteve = reader.GetDateTime(3),
                StevilkePogodb = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                Kontakt = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                TipPrekinitve = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                RacuniDoDne = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                KdoNajObdela = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
                DatumPosredovanja = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                SteviloVnosov = Convert.ToInt32(reader.GetValue(10)),
                ZadnjiStatusId = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                ZadnjiStatusBarva = reader.IsDBNull(12) ? null : reader.GetString(12).Trim(),
                TipReklamacije = (ObracunDb.Data.Entities.TipReklamacije)reader.GetInt32(13),
                Opis = reader.IsDBNull(14) ? null : reader.GetString(14).Trim(),
                ZadnjiStatusNaziv = reader.IsDBNull(15) ? null : reader.GetString(15).Trim()
            });
        }

        return result;
    }

    public async Task<List<ReklamacijaGridDto>> GetZaPartnerjaAsync(int partner, ObdobjeRange obdobje)
    {
        var result = new List<ReklamacijaGridDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT
                r.ID,
                r.PARTNER,
                p.NAZIV,
                r.DATUM_ZAHTEVE,
                r.STEVILKE_POGODB,
                r.KONTAKT,
                r.TIP_PREKINITVE,
                r.RACUNI_DO_DNE,
                (SELECT FIRST 1 rp.KDO_NAJ_OBDELA
                 FROM OBRACUN_REKLAMACIJA_POS rp
                 WHERE rp.ID_REKLAMACIJA = r.ID
                 ORDER BY rp.DATUM DESC, rp.ID DESC) AS KDO_NAJ_OBDELA,
                (SELECT FIRST 1 rp.DATUM
                 FROM OBRACUN_REKLAMACIJA_POS rp
                 WHERE rp.ID_REKLAMACIJA = r.ID
                 ORDER BY rp.DATUM DESC, rp.ID DESC) AS DATUM_POSREDOVANJA,
                COALESCE(pc.STEVILO_VNOSOV, 0) AS STEVILO_VNOSOV,
                CAST(NULL AS INTEGER) AS ZADNJI_STATUS_ID,
                CAST(NULL AS VARCHAR(20)) AS ZADNJI_STATUS_BARVA
            FROM OBRACUN_REKLAMACIJA r
            LEFT JOIN PARTNER p ON r.PARTNER = p.SIFRA
            LEFT JOIN (
                SELECT ID_REKLAMACIJA, COUNT(*) AS STEVILO_VNOSOV
                FROM OBRACUN_REKLAMACIJA_POS
                GROUP BY ID_REKLAMACIJA
            ) pc ON pc.ID_REKLAMACIJA = r.ID
            WHERE r.PARTNER = @Partner
              AND r.DATUM_ZAHTEVE >= @Od AND r.DATUM_ZAHTEVE < @DoEks
            ORDER BY r.DATUM_ZAHTEVE DESC, r.ID DESC", connection);

        command.Parameters.AddWithValue("@Partner", partner);
        command.Parameters.AddWithValue("@Od", obdobje.Od);
        command.Parameters.AddWithValue("@DoEks", obdobje.Do.AddDays(1));

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ReklamacijaGridDto
            {
                Id = reader.GetInt32(0),
                Partner = reader.GetInt32(1),
                NazivPartnerja = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim(),
                DatumZahteve = reader.GetDateTime(3),
                StevilkePogodb = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
                Kontakt = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
                TipPrekinitve = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
                RacuniDoDne = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                KdoNajObdela = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
                DatumPosredovanja = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                SteviloVnosov = Convert.ToInt32(reader.GetValue(10)),
                ZadnjiStatusId = reader.IsDBNull(11) ? null : reader.GetInt32(11),
                ZadnjiStatusBarva = reader.IsDBNull(12) ? null : reader.GetString(12).Trim()
            });
        }

        return result;
    }

    public async Task<ReklamacijaGridDto?> GetByIdAsync(int id)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT
                r.ID,
                r.PARTNER,
                p.NAZIV,
                r.DATUM_ZAHTEVE,
                r.STEVILKE_POGODB,
                r.KONTAKT,
                r.TIP_PREKINITVE,
                r.RACUNI_DO_DNE,
                (SELECT FIRST 1 rp.KDO_NAJ_OBDELA
                 FROM OBRACUN_REKLAMACIJA_POS rp
                 WHERE rp.ID_REKLAMACIJA = r.ID
                 ORDER BY rp.DATUM DESC, rp.ID DESC) AS KDO_NAJ_OBDELA,
                (SELECT FIRST 1 rp.DATUM
                 FROM OBRACUN_REKLAMACIJA_POS rp
                 WHERE rp.ID_REKLAMACIJA = r.ID
                 ORDER BY rp.DATUM DESC, rp.ID DESC) AS DATUM_POSREDOVANJA,
                COALESCE(pc.STEVILO_VNOSOV, 0) AS STEVILO_VNOSOV,
                (SELECT FIRST 1 rp.STATUS_ID
                 FROM OBRACUN_REKLAMACIJA_POS rp
                 WHERE rp.ID_REKLAMACIJA = r.ID
                 ORDER BY rp.DATUM DESC, rp.ID DESC) AS ZADNJI_STATUS_ID,
                (SELECT FIRST 1 s.BARVA
                 FROM OBRACUN_REKLAMACIJA_POS rp
                 LEFT JOIN OBRACUN_REKLAMACIJA_SIFRANT s ON s.ID = rp.STATUS_ID
                 WHERE rp.ID_REKLAMACIJA = r.ID
                 ORDER BY rp.DATUM DESC, rp.ID DESC) AS ZADNJI_STATUS_BARVA,
                r.TIP_REKLAMACIJE,
                r.OPIS,
                (SELECT FIRST 1 s.NAZIV
                 FROM OBRACUN_REKLAMACIJA_POS rp
                 LEFT JOIN OBRACUN_REKLAMACIJA_SIFRANT s ON s.ID = rp.STATUS_ID
                 WHERE rp.ID_REKLAMACIJA = r.ID
                 ORDER BY rp.DATUM DESC, rp.ID DESC) AS ZADNJI_STATUS_NAZIV
            FROM OBRACUN_REKLAMACIJA r
            LEFT JOIN PARTNER p ON r.PARTNER = p.SIFRA
            LEFT JOIN (
                SELECT ID_REKLAMACIJA, COUNT(*) AS STEVILO_VNOSOV
                FROM OBRACUN_REKLAMACIJA_POS
                GROUP BY ID_REKLAMACIJA
            ) pc ON pc.ID_REKLAMACIJA = r.ID
            WHERE r.ID = @Id", connection);

        command.Parameters.AddWithValue("@Id", id);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new ReklamacijaGridDto
        {
            Id = reader.GetInt32(0),
            Partner = reader.GetInt32(1),
            NazivPartnerja = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim(),
            DatumZahteve = reader.GetDateTime(3),
            StevilkePogodb = reader.IsDBNull(4) ? null : reader.GetString(4).Trim(),
            Kontakt = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
            TipPrekinitve = reader.IsDBNull(6) ? null : reader.GetString(6).Trim(),
            RacuniDoDne = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            KdoNajObdela = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
            DatumPosredovanja = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
            SteviloVnosov = Convert.ToInt32(reader.GetValue(10)),
            ZadnjiStatusId = reader.IsDBNull(11) ? null : reader.GetInt32(11),
            ZadnjiStatusBarva = reader.IsDBNull(12) ? null : reader.GetString(12).Trim(),
            TipReklamacije = (ObracunDb.Data.Entities.TipReklamacije)reader.GetInt32(13),
            Opis = reader.IsDBNull(14) ? null : reader.GetString(14).Trim(),
            ZadnjiStatusNaziv = reader.IsDBNull(15) ? null : reader.GetString(15).Trim()
        };
    }

    public async Task<List<ReklamacijaPostavkaDto>> GetPostavkeAsync(int idReklamacija)
    {
        var result = new List<ReklamacijaPostavkaDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT rp.ID, rp.DATUM, rp.UPORABNIK, rp.OPIS, s.NAZIV
            FROM OBRACUN_REKLAMACIJA_POS rp
            LEFT JOIN OBRACUN_REKLAMACIJA_SIFRANT s ON s.ID = rp.STATUS_ID
            WHERE ID_REKLAMACIJA = @IdReklamacija
            ORDER BY rp.DATUM DESC, rp.ID DESC", connection);

        command.Parameters.AddWithValue("@IdReklamacija", idReklamacija);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ReklamacijaPostavkaDto
            {
                Id = reader.GetInt32(0),
                Datum = reader.GetDateTime(1),
                Uporabnik = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim(),
                Komentar = reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                StatusNaziv = reader.IsDBNull(4) ? null : reader.GetString(4).Trim()
            });
        }

        return result;
    }

    public async Task UpdatePostavkaKomentarAsync(int id, string? komentar)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            UPDATE OBRACUN_REKLAMACIJA_POS
            SET OPIS = @Opis
            WHERE ID = @Id", connection);

        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Opis", (object?)komentar ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<ReklamacijaStatusSifrantDto>> GetStatuseAsync()
    {
        var result = new List<ReklamacijaStatusSifrantDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT ID, NAZIV, BARVA
            FROM OBRACUN_REKLAMACIJA_SIFRANT
            ORDER BY NAZIV", connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ReklamacijaStatusSifrantDto
            {
                Id = reader.GetInt32(0),
                Naziv = reader.GetString(1).Trim(),
                Barva = reader.GetString(2).Trim()
            });
        }

        return result;
    }

    public async Task<int> AddStatusAsync(ReklamacijaStatusSifrantDto status)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            INSERT INTO OBRACUN_REKLAMACIJA_SIFRANT (NAZIV, BARVA)
            VALUES (@Naziv, @Barva)
            RETURNING ID", connection);

        command.Parameters.AddWithValue("@Naziv", status.Naziv.Trim());
        command.Parameters.AddWithValue("@Barva", status.Barva.Trim());
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task UpdateStatusAsync(ReklamacijaStatusSifrantDto status)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            UPDATE OBRACUN_REKLAMACIJA_SIFRANT
            SET NAZIV = @Naziv,
                BARVA = @Barva
            WHERE ID = @Id", connection);

        command.Parameters.AddWithValue("@Id", status.Id);
        command.Parameters.AddWithValue("@Naziv", status.Naziv.Trim());
        command.Parameters.AddWithValue("@Barva", status.Barva.Trim());
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteStatusAsync(int id)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand("DELETE FROM OBRACUN_REKLAMACIJA_SIFRANT WHERE ID = @Id", connection);
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<List<PartnerReklamacijaDto>> GetAllPartnerjeAsync()
    {
        var result = new List<PartnerReklamacijaDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT SIFRA, NAZIV, E_POSTA
            FROM PARTNER
            ORDER BY NAZIV", connection);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PartnerReklamacijaDto
            {
                Sifra = reader.GetInt32(0),
                Naziv = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim(),
                EPosta = reader.IsDBNull(2) ? null : reader.GetString(2).Trim()
            });
        }

        return result;
    }

    public async Task<List<PogodbaZaReklamacijoDto>> GetVeljavnePogodbeAsync(int partner)
    {
        var result = new List<PogodbaZaReklamacijoDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT
                p.STEVILKA,
                p.LETO,
                p.ST_POGODBE,
                COALESCE(SUM(COALESCE(pp.KOLICINA, 0) * COALESCE(pp.PRODAJNA_CENA, 0) * (1 - COALESCE(pp.RABAT1, 0) / 100)), 0) AS ZNESEK,
                p.VELJA_DO
            FROM FA_POGODBE p
            LEFT JOIN FA_POGODBE_POS pp ON p.STEVILKA = pp.STEVILKA AND p.LETO = pp.LETO
            WHERE p.PARTNER = @Partner
              AND (p.VELJA_DO IS NULL OR p.VELJA_DO >= @Today OR p.PRVI_RACUN_OD > @Today)
            GROUP BY p.STEVILKA, p.LETO, p.ST_POGODBE, p.VELJA_DO
            ORDER BY p.LETO DESC, p.STEVILKA DESC", connection);

        command.Parameters.AddWithValue("@Partner", partner);
        command.Parameters.AddWithValue("@Today", DateTime.Today);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new PogodbaZaReklamacijoDto
            {
                Stevilka = reader.GetInt32(0),
                Leto = reader.GetInt32(1),
                StPogodbe = reader.IsDBNull(2) ? null : reader.GetString(2).Trim(),
                Znesek = reader.GetDecimal(3),
                VeljaDo = reader.IsDBNull(4) ? null : reader.GetDateTime(4)
            });
        }

        return result;
    }

    public async Task<List<PogodbaZaReklamacijoDto>> GetPogodbeZaPrilogeAsync(int idReklamacija)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        int partner;
        string? stevilkePogodb;
        await using (var headerCommand = new FbCommand(@"
            SELECT PARTNER, STEVILKE_POGODB
            FROM OBRACUN_REKLAMACIJA
            WHERE ID = @IdReklamacija", connection))
        {
            headerCommand.Parameters.AddWithValue("@IdReklamacija", idReklamacija);
            await using var headerReader = await headerCommand.ExecuteReaderAsync();
            if (!await headerReader.ReadAsync())
                return new();

            partner = headerReader.GetInt32(0);
            stevilkePogodb = headerReader.IsDBNull(1) ? null : headerReader.GetString(1);
        }

        var izbraneStevilke = (stevilkePogodb ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (izbraneStevilke.Count == 0)
            return new();

        await using var command = new FbCommand(@"
            SELECT STEVILKA, LETO, ST_POGODBE
            FROM FA_POGODBE
            WHERE PARTNER = @Partner
            ORDER BY LETO, STEVILKA", connection);

        command.Parameters.AddWithValue("@Partner", partner);

        var result = new List<PogodbaZaReklamacijoDto>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var stevilka = reader.GetInt32(0);
            var leto = reader.GetInt32(1);
            var stPogodbe = reader.IsDBNull(2) ? null : reader.GetString(2).Trim();
            var displayStevilka = string.IsNullOrWhiteSpace(stPogodbe) ? $"{stevilka}/{leto}" : stPogodbe;
            if (!izbraneStevilke.Contains(displayStevilka) && !izbraneStevilke.Contains($"{stevilka}/{leto}"))
                continue;

            result.Add(new PogodbaZaReklamacijoDto
            {
                Stevilka = stevilka,
                Leto = leto,
                StPogodbe = stPogodbe
            });
        }

        return result;
    }

    public async Task<List<ReklamacijaPrilogaDto>> GetPrilogeAsync(int idReklamacija)
    {
        var result = new List<ReklamacijaPrilogaDto>();

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT ID, ID_REKLAMACIJA, IME_DATOTEKE, TIP_VSEBINE, VELIKOST, DATUM, UPORABNIK
            FROM OBRACUN_PRILOGA
            WHERE ID_REKLAMACIJA = @IdReklamacija
            ORDER BY DATUM DESC, ID DESC", connection);

        command.Parameters.AddWithValue("@IdReklamacija", idReklamacija);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new ReklamacijaPrilogaDto
            {
                Id = reader.GetInt32(0),
                IdReklamacija = reader.GetInt32(1),
                ImeDatoteke = reader.GetString(2).Trim(),
                TipVsebine = reader.GetString(3).Trim(),
                Velikost = reader.GetInt32(4),
                Datum = reader.GetDateTime(5),
                Uporabnik = reader.GetString(6).Trim()
            });
        }

        return result;
    }

    public async Task<ReklamacijaPrilogaVsebinaDto?> GetPrilogaVsebinaAsync(int id)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            SELECT ID, ID_REKLAMACIJA, IME_DATOTEKE, TIP_VSEBINE, VELIKOST, DATUM, UPORABNIK, VSEBINA
            FROM OBRACUN_PRILOGA
            WHERE ID = @Id", connection);

        command.Parameters.AddWithValue("@Id", id);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new ReklamacijaPrilogaVsebinaDto
        {
            Id = reader.GetInt32(0),
            IdReklamacija = reader.GetInt32(1),
            ImeDatoteke = reader.GetString(2).Trim(),
            TipVsebine = reader.GetString(3).Trim(),
            Velikost = reader.GetInt32(4),
            Datum = reader.GetDateTime(5),
            Uporabnik = reader.GetString(6).Trim(),
            Vsebina = (byte[])reader[7]
        };
    }

    public async Task<int> AddPrilogaAsync(int idReklamacija, ReklamacijaPrilogaVnosDto priloga)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            INSERT INTO OBRACUN_PRILOGA
                (ID_REKLAMACIJA, IME_DATOTEKE, TIP_VSEBINE, VSEBINA, VELIKOST, DATUM, UPORABNIK)
            VALUES
                (@IdReklamacija, @ImeDatoteke, @TipVsebine, @Vsebina, @Velikost, @Datum, @Uporabnik)
            RETURNING ID", connection);

        command.Parameters.AddWithValue("@IdReklamacija", idReklamacija);
        command.Parameters.AddWithValue("@ImeDatoteke", priloga.ImeDatoteke);
        command.Parameters.AddWithValue("@TipVsebine", priloga.TipVsebine);
        command.Parameters.AddWithValue("@Vsebina", priloga.Vsebina);
        command.Parameters.AddWithValue("@Velikost", priloga.Velikost);
        command.Parameters.AddWithValue("@Datum", DateTime.Now);
        command.Parameters.AddWithValue("@Uporabnik", TrenutniUporabnik);

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task AddPrilogeAsync(int idReklamacija, IEnumerable<ReklamacijaPrilogaVnosDto> priloge)
    {
        foreach (var priloga in priloge)
            await AddPrilogaAsync(idReklamacija, priloga);
    }

    public async Task DeletePrilogaAsync(int id)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand("DELETE FROM OBRACUN_PRILOGA WHERE ID = @Id", connection);
        command.Parameters.AddWithValue("@Id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<ReklamacijaFawPreviewDto> GetFawPreviewAsync(int idReklamacija)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        int partner;
        string nazivPartnerja;
        string? stevilkePogodb;
        DateTime? racuniDoDne;

        await using (var headerCommand = new FbCommand(@"
            SELECT r.PARTNER, p.NAZIV, r.STEVILKE_POGODB, r.RACUNI_DO_DNE
            FROM OBRACUN_REKLAMACIJA r
            LEFT JOIN PARTNER p ON r.PARTNER = p.SIFRA
            WHERE r.ID = @Id", connection))
        {
            headerCommand.Parameters.AddWithValue("@Id", idReklamacija);

            await using var reader = await headerCommand.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                throw new InvalidOperationException("Reklamacija ne obstaja.");

            partner = reader.GetInt32(0);
            nazivPartnerja = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
            stevilkePogodb = reader.IsDBNull(2) ? null : reader.GetString(2).Trim();
            racuniDoDne = reader.IsDBNull(3) ? null : reader.GetDateTime(3);
        }

        if (string.IsNullOrWhiteSpace(stevilkePogodb))
            throw new InvalidOperationException("Za zapis v FAW mora biti izbrana vsaj ena pogodba.");
        if (!racuniDoDne.HasValue)
            throw new InvalidOperationException("Za zapis v FAW mora biti vpisan datum Računi do dne.");

        var izbranePogodbe = stevilkePogodb
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new ReklamacijaFawPreviewDto
        {
            IdReklamacija = idReklamacija,
            Partner = partner,
            NazivPartnerja = nazivPartnerja,
            RacuniDoDne = racuniDoDne.Value
        };

        await using var pogodbeCommand = new FbCommand(@"
            SELECT STEVILKA, LETO, ST_POGODBE, VELJA_DO
            FROM FA_POGODBE
            WHERE PARTNER = @Partner
            ORDER BY LETO DESC, STEVILKA DESC", connection);

        pogodbeCommand.Parameters.AddWithValue("@Partner", partner);

        await using var pogodbeReader = await pogodbeCommand.ExecuteReaderAsync();
        while (await pogodbeReader.ReadAsync())
        {
            var stevilka = pogodbeReader.GetInt32(0);
            var leto = pogodbeReader.GetInt32(1);
            var stPogodbe = pogodbeReader.IsDBNull(2) ? null : pogodbeReader.GetString(2).Trim();
            var display = string.IsNullOrWhiteSpace(stPogodbe) ? $"{stevilka}/{leto}" : stPogodbe;

            if (!izbranePogodbe.Contains(display))
                continue;

            result.Pogodbe.Add(new ReklamacijaFawPogodbaDto
            {
                Stevilka = stevilka,
                Leto = leto,
                StPogodbe = stPogodbe,
                StariDatumVeljavnosti = pogodbeReader.IsDBNull(3) ? null : pogodbeReader.GetDateTime(3),
                NoviDatumVeljavnosti = racuniDoDne.Value
            });
        }

        if (result.Pogodbe.Count == 0)
            throw new InvalidOperationException("Izbranih pogodb ni bilo mogoče najti v FAW.");

        return result;
    }

    public async Task ZapisVFawAsync(int idReklamacija)
    {
        var preview = await GetFawPreviewAsync(idReklamacija);

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            foreach (var pogodba in preview.Pogodbe)
            {
                await using var command = new FbCommand(@"
                    UPDATE FA_POGODBE
                    SET VELJA_DO = @VeljaDo
                    WHERE STEVILKA = @Stevilka AND LETO = @Leto AND PARTNER = @Partner", connection);
                command.Transaction = transaction;
                command.Parameters.AddWithValue("@VeljaDo", preview.RacuniDoDne);
                command.Parameters.AddWithValue("@Stevilka", pogodba.Stevilka);
                command.Parameters.AddWithValue("@Leto", pogodba.Leto);
                command.Parameters.AddWithValue("@Partner", preview.Partner);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<int> AddAsync(ReklamacijaFormDto form)
    {
        var stevilkePogodb = string.Join(", ", form.Pogodbe.Where(p => p.Prekini).Select(p => p.DisplayStevilka));

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await using var insertHeader = new FbCommand(@"
                INSERT INTO OBRACUN_REKLAMACIJA
                    (TIP_REKLAMACIJE, PARTNER, DATUM_ZAHTEVE, STEVILKE_POGODB, KONTAKT, TIP_PREKINITVE, RACUNI_DO_DNE, OPIS)
                VALUES
                    (@TipReklamacije, @Partner, @DatumZahteve, @StevilkePogodb, @Kontakt, @TipPrekinitve, @RacuniDoDne, @Opis)
                RETURNING ID", connection);
            insertHeader.Transaction = transaction;

            insertHeader.Parameters.AddWithValue("@TipReklamacije", (int)form.TipReklamacije);
            insertHeader.Parameters.AddWithValue("@Partner", form.Partner);
            insertHeader.Parameters.AddWithValue("@DatumZahteve", DateTime.Today);
            insertHeader.Parameters.AddWithValue("@StevilkePogodb", string.IsNullOrWhiteSpace(stevilkePogodb) ? DBNull.Value : stevilkePogodb);
            insertHeader.Parameters.AddWithValue("@Kontakt", (object?)form.Kontakt ?? DBNull.Value);
            insertHeader.Parameters.AddWithValue("@TipPrekinitve", (object?)form.TipPrekinitve ?? DBNull.Value);
            insertHeader.Parameters.AddWithValue("@RacuniDoDne", (object?)form.RacuniDoDne ?? DBNull.Value);
            insertHeader.Parameters.AddWithValue("@Opis", (object?)form.Opis ?? DBNull.Value);

            var newId = Convert.ToInt32(await insertHeader.ExecuteScalarAsync());

            await using var insertPos = new FbCommand(@"
                INSERT INTO OBRACUN_REKLAMACIJA_POS
                    (ID_REKLAMACIJA, DATUM, UPORABNIK, OPIS, KDO_NAJ_OBDELA, STATUS_ID)
                VALUES
                    (@IdReklamacija, @Datum, @Uporabnik, @Opis, @KdoNajObdela, @StatusId)", connection);
            insertPos.Transaction = transaction;

            insertPos.Parameters.AddWithValue("@IdReklamacija", newId);
            insertPos.Parameters.AddWithValue("@Datum", DateTime.Now);
            insertPos.Parameters.AddWithValue("@Uporabnik", TrenutniUporabnik);
            insertPos.Parameters.AddWithValue("@Opis", (object?)form.Komentar ?? DBNull.Value);
            insertPos.Parameters.AddWithValue("@KdoNajObdela", (object?)form.KdoNajObdela ?? DBNull.Value);
            insertPos.Parameters.AddWithValue("@StatusId", (object?)form.StatusId ?? DBNull.Value);
            await insertPos.ExecuteNonQueryAsync();

            await transaction.CommitAsync();
            return newId;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task UpdateAsync(int id, ReklamacijaFormDto form)
    {
        var stevilkePogodb = string.Join(", ", form.Pogodbe.Where(p => p.Prekini).Select(p => p.DisplayStevilka));

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            UPDATE OBRACUN_REKLAMACIJA
            SET PARTNER = @Partner,
                STEVILKE_POGODB = @StevilkePogodb,
                KONTAKT = @Kontakt,
                TIP_PREKINITVE = @TipPrekinitve,
                RACUNI_DO_DNE = @RacuniDoDne,
                OPIS = @Opis
            WHERE ID = @Id", connection);

        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Partner", form.Partner);
        command.Parameters.AddWithValue("@StevilkePogodb", string.IsNullOrWhiteSpace(stevilkePogodb) ? DBNull.Value : stevilkePogodb);
        command.Parameters.AddWithValue("@Kontakt", (object?)form.Kontakt ?? DBNull.Value);
        command.Parameters.AddWithValue("@TipPrekinitve", (object?)form.TipPrekinitve ?? DBNull.Value);
        command.Parameters.AddWithValue("@RacuniDoDne", (object?)form.RacuniDoDne ?? DBNull.Value);
        command.Parameters.AddWithValue("@Opis", (object?)form.Opis ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task AddPostavkaAsync(int idReklamacija, string? komentar, string? kdoNajObdela, int? statusId)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var command = new FbCommand(@"
            INSERT INTO OBRACUN_REKLAMACIJA_POS
                (ID_REKLAMACIJA, DATUM, UPORABNIK, OPIS, KDO_NAJ_OBDELA, STATUS_ID)
            VALUES
                (@IdReklamacija, @Datum, @Uporabnik, @Opis, @KdoNajObdela, @StatusId)", connection);

        command.Parameters.AddWithValue("@IdReklamacija", idReklamacija);
        command.Parameters.AddWithValue("@Datum", DateTime.Now);
        command.Parameters.AddWithValue("@Uporabnik", TrenutniUporabnik);
        command.Parameters.AddWithValue("@Opis", (object?)komentar ?? DBNull.Value);
        command.Parameters.AddWithValue("@KdoNajObdela", (object?)kdoNajObdela ?? DBNull.Value);
        command.Parameters.AddWithValue("@StatusId", (object?)statusId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await using (var deletePriloge = new FbCommand("DELETE FROM OBRACUN_PRILOGA WHERE ID_REKLAMACIJA = @Id", connection))
            {
                deletePriloge.Transaction = transaction;
                deletePriloge.Parameters.AddWithValue("@Id", id);
                await deletePriloge.ExecuteNonQueryAsync();
            }

            await using (var deletePos = new FbCommand("DELETE FROM OBRACUN_REKLAMACIJA_POS WHERE ID_REKLAMACIJA = @Id", connection))
            {
                deletePos.Transaction = transaction;
                deletePos.Parameters.AddWithValue("@Id", id);
                await deletePos.ExecuteNonQueryAsync();
            }

            await using (var deleteHeader = new FbCommand("DELETE FROM OBRACUN_REKLAMACIJA WHERE ID = @Id", connection))
            {
                deleteHeader.Transaction = transaction;
                deleteHeader.Parameters.AddWithValue("@Id", id);
                await deleteHeader.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
