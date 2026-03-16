namespace ObracunDb.Data;

/// <summary>
/// Stanje migracij za prikaz v UI.
/// </summary>
public class MigrationStatus
{
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Podroben log migracij (vsak korak posebej).
    /// </summary>
    public List<string> Log { get; } = new();

    public void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Log.Add($"[{timestamp}] {message}");
    }
}
