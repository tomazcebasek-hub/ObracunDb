using ObracunDb.Components;
using ObracunDb.Data;
using ObracunDb.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDevExpressBlazor(options =>
{
    options.SizeMode = DevExpress.Blazor.SizeMode.Small;
});
builder.Services.AddMvc();

builder.Services.AddDevExpressServerSideBlazorPdfViewer();

// Auth
builder.Services.AddScoped<UporabnikService>();
builder.Services.AddScoped<AuthService>();

// Database - preberemo iz ObracunDb.ini
var connectionManager = new FirebirdConnectionManager();
builder.Services.AddSingleton(connectionManager);
var migrationStatus = new MigrationStatus();
builder.Services.AddSingleton(migrationStatus);
builder.Services.AddScoped<ArtikelService>();
builder.Services.AddScoped<ObracunService>();
builder.Services.AddScoped<PredracunService>();
builder.Services.AddScoped<PaketMinuteService>();
builder.Services.AddScoped<PartnerMinuteService>();
builder.Services.AddScoped<PartnerService>();
builder.Services.AddScoped<ZakljucekService>();
builder.Services.AddScoped<ObracunIzvedbaService>();
builder.Services.AddScoped<FawService>();
builder.Services.AddScoped<LoceniRacuniService>();
builder.Services.AddScoped<ObracunOsnutekSpremembaService>();

// Parametri (singleton — naložijo se enkrat ob zagonu)
var parametriService = new ParametriService(connectionManager);
builder.Services.AddSingleton(parametriService);

// User Parameters (scoped — vsak uporabnik ima svojo sejo)
builder.Services.AddScoped<UserParametersService>();

// Test povezave ob zagonu - rezultat shranim za prikaz na UI
ConnectionTestResult connectionTestResult;
if (connectionManager.HasConfigError)
{
    connectionTestResult = new ConnectionTestResult
    {
        IsSuccess = false,
        Message = connectionManager.ConfigError,
        FullError = connectionManager.ConfigError
    };
}
else
{
    connectionTestResult = await connectionManager.TestConnectionAsync();
}
builder.Services.AddSingleton(connectionTestResult);

// Migracije baze ob zagonu
if (connectionTestResult.IsSuccess)
{
    await MigrationManager.ApplyMigrationsAsync(connectionManager, migrationStatus);

    if (!string.IsNullOrEmpty(MigrationManager.LastError))
    {
        migrationStatus.ErrorMessage = MigrationManager.LastError;
    }
    else
    {
        await parametriService.LoadFromDatabaseAsync();
    }
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

// API endpoint za XLSX izvoz
app.MapGet("/api/export/koriscenje-predracuni", async (FirebirdConnectionManager cm, ParametriService ps) =>
{
    return await ExportEndpoints.KoriscenjePredracuni(cm, ps);
}).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();

app.Run();