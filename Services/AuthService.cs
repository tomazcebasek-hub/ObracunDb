using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using ObracunDb.Data.Entities;

namespace ObracunDb.Services;

public class AuthService
{
    private readonly UporabnikService _uporabnikService;

    private bool _isAuthenticated;
    private bool _mustChangePassword;
    private ObracunUporabnik? _currentUser;

    public event Action? OnAuthenticationStateChanged;

    public bool IsAuthenticated => _isAuthenticated;
    public bool MustChangePassword => _mustChangePassword;
    public ObracunUporabnik? CurrentUser => _currentUser;

    /// <summary>
    /// Ali je trenutni uporabnik direktor (admin ali jankokuhar, case-insensitive).
    /// </summary>
    public bool JeDirektor => _currentUser != null
        && (_currentUser.UporabniskoIme.Equals("admin", StringComparison.OrdinalIgnoreCase)
            || _currentUser.UporabniskoIme.Equals("jankokuhar", StringComparison.OrdinalIgnoreCase));

    public AuthService(UporabnikService uporabnikService)
    {
        _uporabnikService = uporabnikService;
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        var user = await _uporabnikService.ValidateAsync(username, password);
        if (user == null)
            return false;

        _currentUser = user;
        _mustChangePassword = user.PrvaPrijava == 1;
        _isAuthenticated = true;

        await _uporabnikService.UpdateZadnjaPrijavaAsync(user.Id);
        OnAuthenticationStateChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Obnovi sejo iz browser storage (za nove zavihke).
    /// Seja se obnovi samo če je sessionStorage zastavica prisotna (brskalnik ni bil zaprt).
    /// </summary>
    public async Task<bool> RestoreSessionAsync(ProtectedLocalStorage localStorage, ProtectedSessionStorage sessionStorage)
    {
        if (_isAuthenticated) return true;

        try
        {
            // sessionStorage se pobriše ob zaprtju brskalnika — če zastavice ni, zahtevaj login
            var activeResult = await sessionStorage.GetAsync<bool>("auth_browser_active");
            if (!activeResult.Success || !activeResult.Value)
                return false;

            var result = await localStorage.GetAsync<int>("auth_user_id");
            if (result.Success && result.Value > 0)
            {
                var user = await _uporabnikService.GetByIdAsync(result.Value);
                if (user != null && user.Aktiven == 1)
                {
                    _currentUser = user;
                    _mustChangePassword = user.PrvaPrijava == 1;
                    _isAuthenticated = true;
                    return true;
                }
            }
        }
        catch
        {
            // ProtectedLocalStorage/SessionStorage lahko vrže exception pri prerender
        }

        return false;
    }

    /// <summary>
    /// Shrani sejo v browser storage.
    /// </summary>
    public async Task SaveSessionAsync(ProtectedLocalStorage localStorage, ProtectedSessionStorage sessionStorage)
    {
        if (_currentUser != null)
        {
            await localStorage.SetAsync("auth_user_id", _currentUser.Id);
            await sessionStorage.SetAsync("auth_browser_active", true);
        }
    }

    public async Task<bool> ChangePasswordAsync(string novoGeslo)
    {
        if (_currentUser == null) return false;

        await _uporabnikService.UpdatePasswordAsync(_currentUser.Id, novoGeslo);
        _currentUser.PrvaPrijava = 0;
        _mustChangePassword = false;
        OnAuthenticationStateChanged?.Invoke();
        return true;
    }

    public async Task LogoutAsync(ProtectedLocalStorage localStorage, ProtectedSessionStorage sessionStorage)
    {
        _isAuthenticated = false;
        _mustChangePassword = false;
        _currentUser = null;

        try
        {
            await localStorage.DeleteAsync("auth_user_id");
            await sessionStorage.DeleteAsync("auth_browser_active");
        }
        catch { }

        OnAuthenticationStateChanged?.Invoke();
    }

    public void Logout()
    {
        _isAuthenticated = false;
        _mustChangePassword = false;
        _currentUser = null;
        OnAuthenticationStateChanged?.Invoke();
    }

    /// <summary>
    /// Avtomatska prijava kot admin (samo za Development okolje).
    /// </summary>
    public async Task<bool> AutoLoginAsAdminAsync()
    {
        if (_isAuthenticated) return true;

        var user = await _uporabnikService.GetByUsernameAsync("admin");
        if (user == null || user.Aktiven != 1) return false;

        _currentUser = user;
        _mustChangePassword = false;
        _isAuthenticated = true;
        OnAuthenticationStateChanged?.Invoke();
        return true;
    }
}
