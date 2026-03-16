namespace ObracunDb.Services;

/// <summary>
/// Servis za uporabniške parametre (velja samo za trenutno sejo)
/// </summary>
public class UserParametersService
{
    /// <summary>
    /// Ali je temno ozadje aktivno (privzeto: false — svetlo ozadje)
    /// </summary>
    public bool IsDarkTheme { get; set; } = false;

    /// <summary>
    /// Event ki se sproži ob spremembi teme
    /// </summary>
    public event Action? OnThemeChanged;

    /// <summary>
    /// Nastavi temo in sproži event
    /// </summary>
    public void SetTheme(bool isDark)
    {
        IsDarkTheme = isDark;
        OnThemeChanged?.Invoke();
    }
}
