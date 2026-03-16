using FirebirdSql.Data.FirebirdClient;

namespace ObracunDb.Data;

/// <summary>
/// Modul za upravljanje povezave na Firebird bazo
/// </summary>
public class FirebirdConnectionManager
{
    private readonly string _connectionString;
    private readonly FbConnectionStringBuilder _builder;
    private readonly string? _configError;

    public FirebirdConnectionManager()
    {
        try
        {
            var iniPath = FindIniFile();
            var config = IniConfigReader.Read(iniPath);

            _builder = new FbConnectionStringBuilder
            {
                DataSource = IniConfigReader.GetValue(config, "Database", "DataSource", "localhost"),
                Database = IniConfigReader.GetValue(config, "Database", "Database"),
                UserID = IniConfigReader.GetValue(config, "Database", "UserID", "SYSDBA"),
                Password = IniConfigReader.GetValue(config, "Database", "Password", "masterkey"),
                Charset = IniConfigReader.GetValue(config, "Database", "Charset", "UTF8"),
                Port = int.TryParse(IniConfigReader.GetValue(config, "Database", "Port", "3050"), out var port) ? port : 3050,
                Pooling = true,
                MinPoolSize = 0,
                MaxPoolSize = 50,
                ServerType = FbServerType.Default
            };

            _connectionString = _builder.ToString();
        }
        catch (Exception ex)
        {
            _configError = ex.Message;
            _builder = new FbConnectionStringBuilder();
            _connectionString = "";
        }
    }

    /// <summary>
    /// Poišèe INI datoteko (poleg exe ali v korenskem direktoriju projekta)
    /// </summary>
    private static string FindIniFile()
    {
        var fileName = "ObracunDb.ini";

        // 1. Poleg exe (output directory)
        var exeDir = AppContext.BaseDirectory;
        var path = Path.Combine(exeDir, fileName);
        if (File.Exists(path)) return path;

        // 2. V trenutnem direktoriju
        path = Path.Combine(Directory.GetCurrentDirectory(), fileName);
        if (File.Exists(path)) return path;

        throw new FileNotFoundException(
            $"Datoteka '{fileName}' ni najdena!\n" +
            $"Iskano v:\n  {Path.Combine(exeDir, fileName)}\n  {Path.Combine(Directory.GetCurrentDirectory(), fileName)}");
    }

    /// <summary>
    /// Ali je prišlo do napake pri branju konfiguracije
    /// </summary>
    public bool HasConfigError => _configError != null;

    /// <summary>
    /// Sporoèilo napake pri branju konfiguracije
    /// </summary>
    public string? ConfigError => _configError;

    /// <summary>
    /// Pridobi novo povezavo na bazo
    /// </summary>
    public FbConnection GetConnection()
    {
        return new FbConnection(_connectionString);
    }

    /// <summary>
    /// Connection string za direktno uporabo
    /// </summary>
    public string ConnectionString => _connectionString;

    /// <summary>
    /// Testira povezavo in vrne rezultat
    /// </summary>
    public async Task<ConnectionTestResult> TestConnectionAsync()
    {
        var result = new ConnectionTestResult
        {
            DataSource = _builder.DataSource,
            Database = _builder.Database,
            Port = _builder.Port,
            UserID = _builder.UserID,
            ServerType = _builder.ServerType.ToString(),
            ConnectionString = _connectionString
        };

        try
        {
            await using var connection = GetConnection();
            await connection.OpenAsync();
            
            result.IsSuccess = true;
            result.ServerVersion = connection.ServerVersion;
            result.Message = "Povezava uspešna!";
            
            await connection.CloseAsync();
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.Message = ex.Message;
            result.FullError = ex.ToString();
        }

        return result;
    }
}

public class ConnectionTestResult
{
    public bool IsSuccess { get; set; }
    public string? Message { get; set; }
    public string? FullError { get; set; }
    public string? ServerVersion { get; set; }
    
    // Parametri
    public string? DataSource { get; set; }
    public string? Database { get; set; }
    public int Port { get; set; }
    public string? UserID { get; set; }
    public string? ServerType { get; set; }
    public string? ConnectionString { get; set; }
}
