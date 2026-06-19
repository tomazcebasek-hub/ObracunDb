using FirebirdSql.Data.FirebirdClient;

namespace ObracunDb.Data;

/// <summary>
/// Upravljalec migracij baze. Tabela OBRACUN_MIGRATION že obstaja (ustvarjena v starem projektu).
/// Migracije v tem projektu se začnejo pri številki 100.
/// </summary>
public static class MigrationManager
{
    private static string? _lastError;

    /// <summary>
    /// Zadnje sporočilo napake migracij (za prikaz v UI).
    /// </summary>
    public static string? LastError => _lastError;
    private static readonly Dictionary<int, Func<FbConnection, MigrationStatus?, string?>> Migrations = new()
    {
        { 100, ApplyV100 },
        { 101, ApplyV101 },
        { 102, ApplyV102 },
        { 103, ApplyV103 },
        { 104, ApplyV104 },
        { 105, ApplyV105 },
        { 106, ApplyV106 },
        { 107, ApplyV107 },
        { 108, ApplyV108 },
        { 109, ApplyV109 },
        { 110, ApplyV110 },
        { 111, ApplyV111 },
        { 112, ApplyV112 },
        { 113, ApplyV113 },
        { 114, ApplyV114 },
        { 115, ApplyV115 },
        { 116, ApplyV116 },
        { 117, ApplyV117 },
        { 118, ApplyV118 }
    };

    /// <summary>
    /// Izvede vse manjkajoče migracije.
    /// </summary>
    public static async Task ApplyMigrationsAsync(FirebirdConnectionManager connectionManager, MigrationStatus? status = null)
    {
        if (connectionManager.HasConfigError)
            return;

        status?.AddLog("Odpiranje povezave na bazo...");
        await using var connection = connectionManager.GetConnection();
        await connection.OpenAsync();
        status?.AddLog("Povezava odprta.");

        // Preveri trenutno verzijo
        int current = 0;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM RDB$RELATIONS WHERE UPPER(TRIM(RDB$RELATION_NAME)) = 'OBRACUN_MIGRATION'";
            var res = cmd.ExecuteScalar();
            if (res != null && Convert.ToInt32(res) > 0)
            {
                using var cmd2 = connection.CreateCommand();
                cmd2.CommandText = "SELECT MAX(VERZIJA) FROM OBRACUN_MIGRATION";
                var r2 = cmd2.ExecuteScalar();
                if (r2 != null && r2 != DBNull.Value)
                    current = Convert.ToInt32(r2);
            }
        }
        status?.AddLog($"Trenutna verzija baze: {current}");

        if (current > 0 && current < 32)
        {
            _lastError =
                $"Podatkovna baza je prestara (OBRACUN_MIGRATION.VERZIJA={current}). " +
                "Najprej zazenite stari program in izvedite vse migracije do verzije 32, " +
                "nato ponovno zazenite ta program.";
            status?.AddLog($"NAPAKA: {_lastError}");
            return;
        }

        // Izvede vse migracije vecje od current
        var maxVersion = Migrations.Keys.Max();
        if (current >= maxVersion)
        {
            status?.AddLog("Vse migracije so ze izvedene.");
            return;
        }

        for (int v = current + 1; v <= maxVersion; v++)
        {
            if (!Migrations.TryGetValue(v, out var migrationFunc))
                continue;

            status?.AddLog($"--- Migracija {v}: {GetMigrationDescription(v)} ---");

            string opis;
            try
            {
                var info = migrationFunc(connection, status);
                opis = string.IsNullOrEmpty(info)
                    ? $"OK: {GetMigrationDescription(v)}"
                    : $"OK: {GetMigrationDescription(v)} -- {info}";
                status?.AddLog($"Migracija {v} USPESNA: {info}");
            }
            catch (Exception ex)
            {
                opis = $"NAPAKA: {GetMigrationDescription(v)} -- {ex.Message}";
                status?.AddLog($"Migracija {v} NAPAKA: {ex.Message}");
            }

            // Zapisi v tabelo OBRACUN_MIGRATION po izvedbi
            try
            {
                // OPIS je VARCHAR(255), skrajsaj ce je predolg
                if (opis.Length > 255)
                    opis = opis[..252] + "...";

                using (var ins = connection.CreateCommand())
                {
                    ins.CommandText = "INSERT INTO OBRACUN_MIGRATION (VERZIJA, DATUM, OPIS) VALUES (@v, CURRENT_TIMESTAMP, @opis)";
                    var pv = ins.CreateParameter();
                    pv.ParameterName = "@v";
                    pv.Value = v;
                    ins.Parameters.Add(pv);
                    var pop = ins.CreateParameter();
                    pop.ParameterName = "@opis";
                    pop.Value = opis;
                    ins.Parameters.Add(pop);
                    ins.ExecuteNonQuery();
                }
                status?.AddLog($"Zapis v OBRACUN_MIGRATION uspesen.");
            }
            catch (Exception logEx)
            {
                _lastError = $"Migracija {v} izvedena, napaka pri zapisu v OBRACUN_MIGRATION: {logEx.Message}";
                status?.AddLog($"NAPAKA pri zapisu v OBRACUN_MIGRATION: {logEx.Message}");
            }
        }

        status?.AddLog("Migracije zakljucene.");
    }

    private static string GetMigrationDescription(int version) => version switch
    {
        100 => "Dodan stolpec MINUTE_NALOGA v OBRACUN_DN",
        101 => "OBRACUN_DN.STEVILKA: INTEGER -> VARCHAR(10) + vodilne nicle na 7 mest",
        102 => "OBRACUN_PORABA_MINUT: popravek TIP za 1/2026 (TIP+1)",
        103 => "OBRACUN_REVIZIJA: dodana polja STEVILKA, LETO + backfill iz KONTEKST",
        104 => "OBRACUN_PORABA_MINUT.PREDRACUN_STEVILKA: INTEGER -> VARCHAR(10) + vodilne nicle na 7 mest",
        105 => "OBRACUN_OSNUTEK: dodana polja VSE_MINUTE_* in ZE_PORABLJENE_* za predracune in partner minute",
        108 => "OBRACUN_OSNUTEK_POTRDITEV: nova tabela za potrditve osnutkov",
        109 => "OBRACUN_OSNUTEK: dodan stolpec LETNA_POGODBA",
        110 => "OBRACUN_LOCENI_RACUNI: nova tabela za ločene račune",
        112 => "OBRACUN_OSNUTEK_POS: dodana stolpca KDO in KDAJ",
        113 => "OBRACUN_OSNUTEK_POS: dodana stolpca POGODBA_STEVILKA in POGODBA_LETO",
        114 => "OBRACUN_OSNUTEK_RACUN: nova tabela za ločene račune po pogodbah",
        115 => "OBRACUN_OSNUTEK_SPREMEMBA: nova tabela za ročne korekture količin",
        116 => "OBRACUN_REKLAMACIJA: novi tabeli za reklamacije",
        117 => "OBRACUN_PRILOGA: priponke reklamacij",
        118 => "OBRACUN_MENU_DOVOLJENJE: vidnost menijev po uporabniku",
        _ => $"Migracija na verzijo {version}"
    };

    /// <summary>
    /// Verzija 100: Dodaj stolpec MINUTE v tabelo OBRACUN_DN.
    /// </summary>
    private static string? ApplyV100(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, "ALTER TABLE OBRACUN_DN ADD MINUTE_NALOGA INTEGER DEFAULT 0", status);
        return null;
    }

    /// <summary>
    /// Verzija 102: Popravek OBRACUN_PORABA_MINUT za mesec 1, leto 2026.
    /// Polje TIP je bilo napačno shranjeno (za 1 premajhno). Povečaj TIP za 1.
    /// </summary>
    private static string? ApplyV102(FbConnection conn, MigrationStatus? status)
    {
        var affected = ExecuteSql(conn,
            "UPDATE OBRACUN_PORABA_MINUT SET TIP = TIP + 1 WHERE MESEC = 1 AND LETO = 2026",
            status);
        return $"Popravljenih zapisov: {affected}";
    }

    /// <summary>
    /// Pomozna metoda: izvede SQL in logira v MigrationStatus.
    /// </summary>
    private static int ExecuteSql(FbConnection conn, string sql, MigrationStatus? status)
    {
        status?.AddLog($"SQL: {sql.Trim().Replace("\r\n", " ").Replace("\n", " ")}");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var affected = cmd.ExecuteNonQuery();
        status?.AddLog($"  -> OK (affected: {affected})");
        return affected;
    }

    private static object? ExecuteScalarSql(FbConnection conn, string sql, MigrationStatus? status)
    {
        status?.AddLog($"SQL: {sql.Trim().Replace("\r\n", " ").Replace("\n", " ")}");
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = cmd.ExecuteScalar();
        status?.AddLog($"  -> OK (result: {result})");
        return result;
    }

    /// <summary>
    /// Verzija 101: 
    /// 1. Pobrisi OBRACUN_DN_NEW (ostanek stare migracije)
    /// 2. Umakni PK
    /// 3. Preimenuj STEVILKA -> STEVILKA_OLD
    /// 4. Dodaj nov stolpec STEVILKA VARCHAR(10)
    /// 5. Prepisi podatke z vodilnimi niclami (LPAD)
    /// 6. Pobrisi STEVILKA_OLD
    /// 7. Ponastavi PK (STEVILKA, LETO)
    /// Idempotentna: zaznava delno izvedeno stanje in nadaljuje od tam naprej.
    /// </summary>
    private static string? ApplyV101(FbConnection conn, MigrationStatus? status)
    {
        var log = new List<string>();

        // 0. Preglej trenutno strukturo tabele
        status?.AddLog("Pregled stolpcev OBRACUN_DN:");
        var stolpci = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT TRIM(rf.RDB$FIELD_NAME), TRIM(t.RDB$TYPE_NAME), f.RDB$FIELD_LENGTH
                FROM RDB$RELATION_FIELDS rf
                JOIN RDB$FIELDS f ON rf.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME
                JOIN RDB$TYPES t ON f.RDB$FIELD_TYPE = t.RDB$TYPE AND t.RDB$FIELD_NAME = 'RDB$FIELD_TYPE'
                WHERE TRIM(rf.RDB$RELATION_NAME) = 'OBRACUN_DN'
                ORDER BY rf.RDB$FIELD_POSITION";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var colName = reader.GetString(0).Trim();
                var colType = reader.GetString(1).Trim();
                var colLen = reader.GetInt32(2);
                stolpci.Add(colName);
                status?.AddLog($"  Stolpec: {colName}, Tip: {colType}, Dolzina: {colLen}");
            }
        }
        catch (Exception ex)
        {
            status?.AddLog($"  Napaka pri pregledu: {ex.Message}");
        }

        bool jeDelnoIzvedena = stolpci.Contains("STEVILKA_OLD");
        status?.AddLog($"Delno izvedena: {jeDelnoIzvedena}");

        // 1. Pobrisi OBRACUN_DN_NEW
        status?.AddLog("Korak 1: Brisanje OBRACUN_DN_NEW...");
        try
        {
            ExecuteSql(conn, "DROP TABLE OBRACUN_DN_NEW", status);
            log.Add("OBRACUN_DN_NEW pobrisana");
        }
        catch (Exception ex)
        {
            status?.AddLog($"  Preskoceno: {ex.Message}");
            log.Add("OBRACUN_DN_NEW ni obstajala");
        }

        if (!jeDelnoIzvedena)
        {
            // 2. Umakni PK
            status?.AddLog("Korak 2: Iskanje PK constrainta...");
            var pkResult = ExecuteScalarSql(conn, @"
                SELECT TRIM(rc.RDB$CONSTRAINT_NAME)
                FROM RDB$RELATION_CONSTRAINTS rc
                WHERE TRIM(rc.RDB$RELATION_NAME) = 'OBRACUN_DN'
                  AND rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY'", status);
            var pkName = pkResult?.ToString()?.Trim();

            if (!string.IsNullOrEmpty(pkName))
            {
                status?.AddLog($"Korak 2: Brisanje PK '{pkName}'...");
                ExecuteSql(conn, $"ALTER TABLE OBRACUN_DN DROP CONSTRAINT {pkName}", status);
                log.Add($"PK '{pkName}' pobrisana");
            }
            else
            {
                status?.AddLog("Korak 2: PK ne obstaja, preskoceno.");
            }

            // 3. Preimenuj STEVILKA -> STEVILKA_OLD
            status?.AddLog("Korak 3: Preimenovanje STEVILKA -> STEVILKA_OLD...");
            ExecuteSql(conn, "ALTER TABLE OBRACUN_DN ALTER COLUMN STEVILKA TO STEVILKA_OLD", status);
            log.Add("STEVILKA preimenovana v STEVILKA_OLD");

            // 4. Dodaj nov stolpec STEVILKA VARCHAR(10)
            status?.AddLog("Korak 4: Dodajanje novega stolpca STEVILKA VARCHAR(10)...");
            ExecuteSql(conn, "ALTER TABLE OBRACUN_DN ADD STEVILKA VARCHAR(10) DEFAULT '' NOT NULL", status);
            log.Add("Nov stolpec STEVILKA VARCHAR(10) dodan");

            // 5. Prepisi podatke z vodilnimi niclami
            status?.AddLog("Korak 5: Prepis podatkov z vodilnimi niclami...");
            var affected = ExecuteSql(conn, "UPDATE OBRACUN_DN SET STEVILKA = LPAD(CAST(STEVILKA_OLD AS VARCHAR(10)), 7, '0')", status);
            log.Add($"Podatki prepisani z vodilnimi niclami: {affected} vrstic");
        }
        else
        {
            status?.AddLog("Koraki 2-5 ze izvedeni (delno izvedena migracija). Nadaljujem s korakom 6.");
            log.Add("Koraki 2-5 ze izvedeni");
        }

        // 6. Pobrisi STEVILKA_OLD (ce obstaja)
        if (stolpci.Contains("STEVILKA_OLD") || !jeDelnoIzvedena)
        {
            status?.AddLog("Korak 6: Brisanje STEVILKA_OLD...");
            ExecuteSql(conn, "ALTER TABLE OBRACUN_DN DROP STEVILKA_OLD", status);
            log.Add("STEVILKA_OLD pobrisana");
        }

        // 7. Ponastavi PK (ce ne obstaja)
        status?.AddLog("Korak 7: Preverjanje PK...");
        var existingPk = ExecuteScalarSql(conn, @"
            SELECT TRIM(rc.RDB$CONSTRAINT_NAME)
            FROM RDB$RELATION_CONSTRAINTS rc
            WHERE TRIM(rc.RDB$RELATION_NAME) = 'OBRACUN_DN'
              AND rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY'", status);

        if (string.IsNullOrEmpty(existingPk?.ToString()?.Trim()))
        {
            status?.AddLog("Korak 7: Ponastavljanje PK (STEVILKA, LETO)...");
            ExecuteSql(conn, "ALTER TABLE OBRACUN_DN ADD PRIMARY KEY (STEVILKA, LETO)", status);
            log.Add("PK ponastavljeno (STEVILKA, LETO)");
        }
        else
        {
            status?.AddLog("Korak 7: PK ze obstaja, preskoceno.");
            log.Add("PK ze obstaja");
        }

        return string.Join(". ", log);
    }

    /// <summary>
    /// Verzija 103: Dodaj polja STEVILKA (VARCHAR(10)), LETO (INTEGER) v OBRACUN_REVIZIJA.
    /// Backfill iz KONTEKST: "Nalog {stevilka}/{leto}" -> STEVILKA, LETO.
    /// </summary>
    private static string? ApplyV103(FbConnection conn, MigrationStatus? status)
    {
        // 1. Dodaj stolpce
        ExecuteSql(conn, "ALTER TABLE OBRACUN_REVIZIJA ADD STEVILKA VARCHAR(10)", status);
        ExecuteSql(conn, "ALTER TABLE OBRACUN_REVIZIJA ADD LETO INTEGER", status);

        // 2. Backfill: iz KONTEKST oblike "Nalog {stevilka}/{leto}"
        var affected = ExecuteSql(conn, @"
            UPDATE OBRACUN_REVIZIJA
            SET STEVILKA = TRIM(SUBSTRING(KONTEKST FROM 7 FOR POSITION('/' IN KONTEKST) - 7)),
                LETO = CAST(SUBSTRING(KONTEKST FROM POSITION('/' IN KONTEKST) + 1) AS INTEGER)
            WHERE KONTEKST LIKE 'Nalog %' AND POSITION('/' IN KONTEKST) > 0", status);

        return $"Backfill: {affected} zapisov";
    }

    /// <summary>
    /// Verzija 104: OBRACUN_PORABA_MINUT.PREDRACUN_STEVILKA: INTEGER -> VARCHAR(10).
    /// Obstoječe int vrednosti se pretvorijo v string z vodilnimi ničlami (7 mest).
    /// </summary>
    private static string? ApplyV104(FbConnection conn, MigrationStatus? status)
    {
        var log = new List<string>();

        // 1. Preimenuj PREDRACUN_STEVILKA -> PREDRACUN_STEVILKA_OLD
        ExecuteSql(conn, "ALTER TABLE OBRACUN_PORABA_MINUT ALTER COLUMN PREDRACUN_STEVILKA TO PREDRACUN_STEVILKA_OLD", status);
        log.Add("PREDRACUN_STEVILKA preimenovana v PREDRACUN_STEVILKA_OLD");

        // 2. Dodaj nov stolpec PREDRACUN_STEVILKA VARCHAR(10)
        ExecuteSql(conn, "ALTER TABLE OBRACUN_PORABA_MINUT ADD PREDRACUN_STEVILKA VARCHAR(10)", status);
        log.Add("Nov stolpec PREDRACUN_STEVILKA VARCHAR(10) dodan");

        // 3. Prepisi podatke z vodilnimi ničlami (7 mest)
        var affected = ExecuteSql(conn,
            "UPDATE OBRACUN_PORABA_MINUT SET PREDRACUN_STEVILKA = LPAD(CAST(PREDRACUN_STEVILKA_OLD AS VARCHAR(10)), 7, '0') WHERE PREDRACUN_STEVILKA_OLD IS NOT NULL",
            status);
        log.Add($"Podatki prepisani z vodilnimi niclami: {affected} vrstic");

        // 4. Pobriši stari stolpec
        ExecuteSql(conn, "ALTER TABLE OBRACUN_PORABA_MINUT DROP PREDRACUN_STEVILKA_OLD", status);
        log.Add("PREDRACUN_STEVILKA_OLD pobrisana");

        return string.Join(". ", log);
    }

    /// <summary>
    /// Verzija 105: Dodaj polja za skupne minute in ze porabljene v OBRACUN_OSNUTEK.
    /// </summary>
    private static string? ApplyV105(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, "ALTER TABLE OBRACUN_OSNUTEK ADD VSE_MINUTE_PREDRACUN INTEGER DEFAULT 0", status);
        ExecuteSql(conn, "ALTER TABLE OBRACUN_OSNUTEK ADD ZE_PORABLJENE_PREDRACUN INTEGER DEFAULT 0", status);
        ExecuteSql(conn, "ALTER TABLE OBRACUN_OSNUTEK ADD VSE_MINUTE_PARTNER_MINUTE INTEGER DEFAULT 0", status);
        ExecuteSql(conn, "ALTER TABLE OBRACUN_OSNUTEK ADD ZE_PORABLJENE_PARTNER_MINUTE INTEGER DEFAULT 0", status);
        return null;
    }

    private static string? ApplyV106(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, "ALTER TABLE OBRACUN_REVIZIJA ADD ID_V_TABELI INTEGER", status);
        return null;
    }

    private static string? ApplyV107(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, "ALTER TABLE OBRACUN_MINUTE ADD UPORABNIK VARCHAR(100) DEFAULT 'Neznan'", status);
        ExecuteSql(conn, "UPDATE OBRACUN_MINUTE SET UPORABNIK = 'Neznan' WHERE UPORABNIK IS NULL", status);
        return null;
    }

    private static string? ApplyV108(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, @"CREATE TABLE OBRACUN_OSNUTEK_POTRDITEV (
            ID INTEGER NOT NULL PRIMARY KEY,
            PARTNER INTEGER NOT NULL,
            MESEC INTEGER NOT NULL,
            LETO INTEGER NOT NULL,
            KDO VARCHAR(100) NOT NULL,
            KDAJ TIMESTAMP NOT NULL
        )", status);
        ExecuteSql(conn, "CREATE GENERATOR GEN_OBRACUN_OSNUTEK_POTRDITEV_ID", status);
        ExecuteSql(conn, @"CREATE TRIGGER TRG_OBRACUN_OSNUTEK_POTRDITEV FOR OBRACUN_OSNUTEK_POTRDITEV
            ACTIVE BEFORE INSERT POSITION 0
            AS BEGIN
                IF (NEW.ID IS NULL OR NEW.ID = 0) THEN
                    NEW.ID = GEN_ID(GEN_OBRACUN_OSNUTEK_POTRDITEV_ID, 1);
            END", status);
        return null;
    }

    private static string? ApplyV109(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, "ALTER TABLE OBRACUN_OSNUTEK ADD LETNA_POGODBA INTEGER DEFAULT 0", status);
        return null;
    }

    private static string? ApplyV110(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, @"CREATE TABLE OBRACUN_LOCENI_RACUNI (
            ID INTEGER NOT NULL PRIMARY KEY,
            PARTNER INTEGER NOT NULL,
            PRODAJALNA INTEGER NOT NULL,
            POGODBA_STEVILKA INTEGER NOT NULL,
            POGODBA_LETO INTEGER NOT NULL,
            DATUM_VNOSA TIMESTAMP NOT NULL,
            UPORABNIK VARCHAR(100) NOT NULL
        )", status);
        ExecuteSql(conn, "CREATE GENERATOR GEN_OBRACUN_LOCENI_RACUNI_ID", status);
        ExecuteSql(conn, @"CREATE TRIGGER TRG_OBRACUN_LOCENI_RACUNI FOR OBRACUN_LOCENI_RACUNI
            ACTIVE BEFORE INSERT POSITION 0
            AS BEGIN
                IF (NEW.ID IS NULL OR NEW.ID = 0) THEN
                    NEW.ID = GEN_ID(GEN_OBRACUN_LOCENI_RACUNI_ID, 1);
            END", status);
        return null;
    }

    private static string? ApplyV111(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, @"CREATE TABLE OBRACUN_DN_PREDRACUN (
            STEVILKA VARCHAR(20) NOT NULL,
            LETO INTEGER NOT NULL,
            PREDRACUN_STEVILKA VARCHAR(20) NOT NULL,
            PREDRACUN_LETO INTEGER NOT NULL,
            PRIMARY KEY (STEVILKA, LETO, PREDRACUN_STEVILKA, PREDRACUN_LETO)
        )", status);
        return null;
    }

    /// <summary>
    /// Verzija 112: Dodaj stolpca KDO in KDAJ v tabelo OBRACUN_OSNUTEK_POS.
    /// </summary>
    private static string? ApplyV112(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, "ALTER TABLE OBRACUN_OSNUTEK_POS ADD KDO VARCHAR(100)", status);
        ExecuteSql(conn, "ALTER TABLE OBRACUN_OSNUTEK_POS ADD KDAJ TIMESTAMP", status);
        return null;
    }

    /// <summary>
    /// Verzija 113: Dodaj stolpca POGODBA_STEVILKA in POGODBA_LETO v tabelo OBRACUN_OSNUTEK_POS.
    /// </summary>
    private static string? ApplyV113(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, "ALTER TABLE OBRACUN_OSNUTEK_POS ADD POGODBA_STEVILKA INTEGER", status);
        ExecuteSql(conn, "ALTER TABLE OBRACUN_OSNUTEK_POS ADD POGODBA_LETO INTEGER", status);
        return null;
    }

    /// <summary>
    /// Verzija 114: Nova tabela OBRACUN_OSNUTEK_RACUN za evidenco ločenih računov po pogodbah.
    /// </summary>
    private static string? ApplyV114(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, @"CREATE TABLE OBRACUN_OSNUTEK_RACUN (
            MESEC              INTEGER NOT NULL,
            LETO               INTEGER NOT NULL,
            PARTNER            INTEGER NOT NULL,
            POGODBA_STEVILKA   INTEGER NOT NULL,
            POGODBA_LETO       INTEGER NOT NULL,
            PRODAJALNA         INTEGER NOT NULL,
            RACUN_STEVILKA     INTEGER,
            RACUN_LETO         INTEGER,
            PRIMARY KEY (MESEC, LETO, PARTNER, POGODBA_STEVILKA, POGODBA_LETO)
        )", status);
        return null;
    }

    /// <summary>
    /// Verzija 115: Nova tabela OBRACUN_OSNUTEK_SPREMEMBA za ročne korekture količin v osnutku.
    /// </summary>
    private static string? ApplyV115(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, @"CREATE TABLE OBRACUN_OSNUTEK_SPREMEMBA (
            ID           INTEGER NOT NULL PRIMARY KEY,
            MESEC        INTEGER NOT NULL,
            LETO         INTEGER NOT NULL,
            PARTNER      INTEGER NOT NULL,
            ARTIKEL      VARCHAR(20) NOT NULL,
            KOLICINA     NUMERIC(15,4) NOT NULL,
            OPOMBA       VARCHAR(255),
            UPORABNIK    VARCHAR(100) NOT NULL,
            DATUM_VNOSA  TIMESTAMP NOT NULL
        )", status);
        ExecuteSql(conn, "CREATE GENERATOR GEN_OBRACUN_OSNUTEK_SPREMEMBA_ID", status);
        ExecuteSql(conn, @"CREATE TRIGGER TRG_OBRACUN_OSNUTEK_SPREMEMBA FOR OBRACUN_OSNUTEK_SPREMEMBA
            ACTIVE BEFORE INSERT POSITION 0
            AS BEGIN
                IF (NEW.ID IS NULL OR NEW.ID = 0) THEN
                    NEW.ID = GEN_ID(GEN_OBRACUN_OSNUTEK_SPREMEMBA_ID, 1);
            END", status);
        return null;
    }

    /// <summary>
    /// Verzija 116: Novi tabeli OBRACUN_REKLAMACIJA in OBRACUN_REKLAMACIJA_POS.
    /// </summary>
    private static string? ApplyV116(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, @"CREATE TABLE OBRACUN_REKLAMACIJA (
            ID                INTEGER NOT NULL PRIMARY KEY,
            TIP_REKLAMACIJE   INTEGER NOT NULL,
            PARTNER           INTEGER NOT NULL,
            DATUM_ZAHTEVE     DATE NOT NULL,
            STEVILKE_POGODB   VARCHAR(500),
            KONTAKT           VARCHAR(255),
            TIP_PREKINITVE    VARCHAR(255),
            RACUNI_DO_DNE     DATE
        )", status);
        ExecuteSql(conn, "CREATE GENERATOR GEN_OBRACUN_REKLAMACIJA_ID", status);
        ExecuteSql(conn, @"CREATE TRIGGER TRG_OBRACUN_REKLAMACIJA FOR OBRACUN_REKLAMACIJA
            ACTIVE BEFORE INSERT POSITION 0
            AS BEGIN
                IF (NEW.ID IS NULL OR NEW.ID = 0) THEN
                    NEW.ID = GEN_ID(GEN_OBRACUN_REKLAMACIJA_ID, 1);
            END", status);

        ExecuteSql(conn, @"CREATE TABLE OBRACUN_REKLAMACIJA_POS (
            ID                INTEGER NOT NULL PRIMARY KEY,
            ID_REKLAMACIJA    INTEGER NOT NULL,
            DATUM             TIMESTAMP NOT NULL,
            UPORABNIK         VARCHAR(100) NOT NULL,
            OPIS              BLOB SUB_TYPE TEXT,
            KDO_NAJ_OBDELA    VARCHAR(100)
        )", status);
        ExecuteSql(conn, "CREATE GENERATOR GEN_OBRACUN_REKLAMACIJA_POS_ID", status);
        ExecuteSql(conn, @"CREATE TRIGGER TRG_OBRACUN_REKLAMACIJA_POS FOR OBRACUN_REKLAMACIJA_POS
            ACTIVE BEFORE INSERT POSITION 0
            AS BEGIN
                IF (NEW.ID IS NULL OR NEW.ID = 0) THEN
                    NEW.ID = GEN_ID(GEN_OBRACUN_REKLAMACIJA_POS_ID, 1);
            END", status);
        ExecuteSql(conn, @"ALTER TABLE OBRACUN_REKLAMACIJA_POS
            ADD CONSTRAINT FK_OBR_REKL_POS_GLAVA
            FOREIGN KEY (ID_REKLAMACIJA) REFERENCES OBRACUN_REKLAMACIJA (ID)", status);
        return null;
    }

    /// <summary>
    /// Verzija 117: Priponke reklamacij.
    /// </summary>
    private static string? ApplyV117(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, @"CREATE TABLE OBRACUN_PRILOGA (
            ID                INTEGER NOT NULL PRIMARY KEY,
            ID_REKLAMACIJA    INTEGER NOT NULL,
            IME_DATOTEKE      VARCHAR(255) NOT NULL,
            TIP_VSEBINE       VARCHAR(100) NOT NULL,
            VSEBINA           BLOB SUB_TYPE BINARY NOT NULL,
            VELIKOST          INTEGER NOT NULL,
            DATUM             TIMESTAMP NOT NULL,
            UPORABNIK         VARCHAR(100) NOT NULL
        )", status);
        ExecuteSql(conn, "CREATE GENERATOR GEN_OBRACUN_PRILOGA_ID", status);
        ExecuteSql(conn, @"CREATE TRIGGER TRG_OBRACUN_PRILOGA FOR OBRACUN_PRILOGA
            ACTIVE BEFORE INSERT POSITION 0
            AS BEGIN
                IF (NEW.ID IS NULL OR NEW.ID = 0) THEN
                    NEW.ID = GEN_ID(GEN_OBRACUN_PRILOGA_ID, 1);
            END", status);
        ExecuteSql(conn, @"ALTER TABLE OBRACUN_PRILOGA
            ADD CONSTRAINT FK_OBR_PRILOGA_REKLAMACIJA
            FOREIGN KEY (ID_REKLAMACIJA) REFERENCES OBRACUN_REKLAMACIJA (ID)", status);
        return null;
    }

    /// <summary>
    /// Verzija 118: Nova tabela OBRACUN_MENU_DOVOLJENJE za vidnost menijev po uporabniku.
    /// Logika "allow-list": obstoj vrstice pomeni, da uporabnik ta meni vidi.
    /// Zaseje meni "uporabniki" za jankokuhar, admin in katja, da si lahko nastavijo ostale menije.
    /// </summary>
    private static string? ApplyV118(FbConnection conn, MigrationStatus? status)
    {
        ExecuteSql(conn, @"CREATE TABLE OBRACUN_MENU_DOVOLJENJE (
            ID            INTEGER NOT NULL PRIMARY KEY,
            UPORABNIK_ID  INTEGER NOT NULL,
            MENU_KLJUC    VARCHAR(50) NOT NULL
        )", status);
        ExecuteSql(conn, "CREATE GENERATOR GEN_OBRACUN_MENU_DOVOLJENJE_ID", status);
        ExecuteSql(conn, @"CREATE TRIGGER TRG_OBRACUN_MENU_DOVOLJENJE FOR OBRACUN_MENU_DOVOLJENJE
            ACTIVE BEFORE INSERT POSITION 0
            AS BEGIN
                IF (NEW.ID IS NULL OR NEW.ID = 0) THEN
                    NEW.ID = GEN_ID(GEN_OBRACUN_MENU_DOVOLJENJE_ID, 1);
            END", status);
        ExecuteSql(conn, @"ALTER TABLE OBRACUN_MENU_DOVOLJENJE
            ADD CONSTRAINT UQ_OBR_MENU_DOVOLJENJE UNIQUE (UPORABNIK_ID, MENU_KLJUC)", status);

        var affected = ExecuteSql(conn, @"INSERT INTO OBRACUN_MENU_DOVOLJENJE (UPORABNIK_ID, MENU_KLJUC)
            SELECT ID, 'uporabniki' FROM OBRACUN_UPORABNIK
            WHERE UPPER(UPORABNISKO_IME) IN ('JANKOKUHAR', 'ADMIN', 'KATJA')", status);

        return $"Zasejanih dovoljenj 'uporabniki': {affected}";
    }
}
