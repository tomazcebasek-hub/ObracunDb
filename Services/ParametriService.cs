using FirebirdSql.Data.FirebirdClient;
using ObracunDb.Data.Entities;

namespace ObracunDb.Services;

/// <summary>
/// Servis za delo s parametri iz tabele OBRACUN_PARAMETER.
/// Singleton — parametri se naložijo enkrat ob zagonu, nato so v spominu.
/// Samo definirani parametri se preberejo iz baze.
/// </summary>
public class ParametriService
{
    private readonly Data.FirebirdConnectionManager _connectionManager;
    private readonly Dictionary<string, ObracunParameter> _items = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    // Konstante za ure
    public const int Ura7 = 7;
    public const int Ura16 = 16;
    public const int Ura22 = 22;

    public ParametriService(Data.FirebirdConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
        DefineAll();
    }

    // === Enum ? DB kljuè ===

    private static string ToKey(ObracunParam p) => p switch
    {
        ObracunParam.TemnoOzadje => "TEMNO_OZADJE",
        _ => p.ToString()
    };

    /// <summary>
    /// Vrne enum za praznik po indeksu (1–5).
    /// </summary>
    public static ObracunParam Praznik(int index) => index switch
    {
        1 => ObracunParam.Praznik1,
        2 => ObracunParam.Praznik2,
        3 => ObracunParam.Praznik3,
        4 => ObracunParam.Praznik4,
        5 => ObracunParam.Praznik5,
        _ => throw new ArgumentOutOfRangeException(nameof(index), "Praznik index mora biti 1–5.")
    };

    private void DefineAll()
    {
        Define(ObracunParam.MesecObracuna, "Mesec obraèuna", 1);
        Define(ObracunParam.LetoObracuna, "Leto obraèuna", 2024);
        Define(ObracunParam.Praznik1, "Praznik 1", "");
        Define(ObracunParam.Praznik2, "Praznik 2", "");
        Define(ObracunParam.Praznik3, "Praznik 3", "");
        Define(ObracunParam.Praznik4, "Praznik 4", "");
        Define(ObracunParam.Praznik5, "Praznik 5", "");
        Define(ObracunParam.ProcentPopustaPogodbe, "Procent popusta pogodbe", 0);

        // Kilometrina
        Define(ObracunParam.SifraKilometrina, "Šifra artikla za kilometrino", "");

        // Toleranca minut
        Define(ObracunParam.TolerancaMinut, "Toleranca minut za obraèun", 0);

        // FAW zapis
        Define(ObracunParam.FawDatumRacuna, "FAW datum raèuna", "");
        Define(ObracunParam.FawKomercialist, "FAW komercialist", "");

        // VASCO API nastavitve
        Define(ObracunParam.VascoApiUrl, "VASCO API URL", "");
        Define(ObracunParam.VascoApiUporabnik, "VASCO API uporabnik", "");
        Define(ObracunParam.VascoApiGeslo, "VASCO API geslo", "");
        Define(ObracunParam.VascoApiDavcna, "VASCO API davèna", "");

        // Servisna - Delavnik
        Define(ObracunParam.ServisnaPogodbaDel7_16, "Pogodba, delavnik 7-16", "");
        Define(ObracunParam.ServisnaBrezPogodbeDel7_16, "Brez pogodbe, delavnik 7-16", "");
        Define(ObracunParam.ServisnaPogodbaDel16_22, "Pogodba, delavnik 16-22", "");
        Define(ObracunParam.ServisnaBrezPogodbeDel16_22, "Brez pogodbe, delavnik 16-22", "");
        Define(ObracunParam.ServisnaPogodbaDel22_7, "Pogodba, delavnik 22-7", "");
        Define(ObracunParam.ServisnaBrezPogodbeDel22_7, "Brez pogodbe, delavnik 22-7", "");

        // Servisna - Vikend
        Define(ObracunParam.ServisnaPogodbaVik7_16, "Pogodba, vikend 7-16", "");
        Define(ObracunParam.ServisnaBrezPogodbeVik7_16, "Brez pogodbe, vikend 7-16", "");
        Define(ObracunParam.ServisnaPogodbaVik16_22, "Pogodba, vikend 16-22", "");
        Define(ObracunParam.ServisnaBrezPogodbeVik16_22, "Brez pogodbe, vikend 16-22", "");
        Define(ObracunParam.ServisnaPogodbaVik22_7, "Pogodba, vikend 22-7", "");
        Define(ObracunParam.ServisnaBrezPogodbeVik22_7, "Brez pogodbe, vikend 22-7", "");

        // Servisna - Praznik
        Define(ObracunParam.ServisnaPogodbaP7_16, "Pogodba, praznik 7-16", "");
        Define(ObracunParam.ServisnaBrezPogodbeP7_16, "Brez pogodbe, praznik 7-16", "");
        Define(ObracunParam.ServisnaPogodbaP16_22, "Pogodba, praznik 16-22", "");
        Define(ObracunParam.ServisnaBrezPogodbeP16_22, "Brez pogodbe, praznik 16-22", "");
        Define(ObracunParam.ServisnaPogodbaP22_7, "Pogodba, praznik 22-7", "");
        Define(ObracunParam.ServisnaBrezPogodbeP22_7, "Brez pogodbe, praznik 22-7", "");

        // Strokovna - Delavnik
        Define(ObracunParam.StrokovnaPogodbaDel7_16, "Pogodba, delavnik 7-16", "");
        Define(ObracunParam.StrokovnaBrezPogodbeDel7_16, "Brez pogodbe, delavnik 7-16", "");
        Define(ObracunParam.StrokovnaPogodbaDel16_22, "Pogodba, delavnik 16-22", "");
        Define(ObracunParam.StrokovnaBrezPogodbeDel16_22, "Brez pogodbe, delavnik 16-22", "");
        Define(ObracunParam.StrokovnaPogodbaDel22_7, "Pogodba, delavnik 22-7", "");
        Define(ObracunParam.StrokovnaBrezPogodbeDel22_7, "Brez pogodbe, delavnik 22-7", "");

        // Strokovna - Vikend
        Define(ObracunParam.StrokovnaPogodbaVik7_16, "Pogodba, vikend 7-16", "");
        Define(ObracunParam.StrokovnaBrezPogodbeVik7_16, "Brez pogodbe, vikend 7-16", "");
        Define(ObracunParam.StrokovnaPogodbaVik16_22, "Pogodba, vikend 16-22", "");
        Define(ObracunParam.StrokovnaBrezPogodbeVik16_22, "Brez pogodbe, vikend 16-22", "");
        Define(ObracunParam.StrokovnaPogodbaVik22_7, "Pogodba, vikend 22-7", "");
        Define(ObracunParam.StrokovnaBrezPogodbeVik22_7, "Brez pogodbe, vikend 22-7", "");

        // Strokovna - Praznik
        Define(ObracunParam.StrokovnaPogodbaP7_16, "Pogodba, praznik 7-16", "");
        Define(ObracunParam.StrokovnaBrezPogodbeP7_16, "Brez pogodbe, praznik 7-16", "");
        Define(ObracunParam.StrokovnaPogodbaP16_22, "Pogodba, praznik 16-22", "");
        Define(ObracunParam.StrokovnaBrezPogodbeP16_22, "Brez pogodbe, praznik 16-22", "");
        Define(ObracunParam.StrokovnaPogodbaP22_7, "Pogodba, praznik 22-7", "");
        Define(ObracunParam.StrokovnaBrezPogodbeP22_7, "Brez pogodbe, praznik 22-7", "");

        // Programerska - Delavnik
        Define(ObracunParam.ProgramerskaPogodbaDel7_16, "Pogodba, delavnik 7-16", "");
        Define(ObracunParam.ProgramerskaBrezPogodbeDel7_16, "Brez pogodbe, delavnik 7-16", "");
        Define(ObracunParam.ProgramerskaPogodbaDel16_22, "Pogodba, delavnik 16-22", "");
        Define(ObracunParam.ProgramerskaBrezPogodbeDel16_22, "Brez pogodbe, delavnik 16-22", "");
        Define(ObracunParam.ProgramerskaPogodbaDel22_7, "Pogodba, delavnik 22-7", "");
        Define(ObracunParam.ProgramerskaBrezPogodbeDel22_7, "Brez pogodbe, delavnik 22-7", "");

        // Programerska - Vikend
        Define(ObracunParam.ProgramerskaPogodbaVik7_16, "Pogodba, vikend 7-16", "");
        Define(ObracunParam.ProgramerskaBrezPogodbeVik7_16, "Brez pogodbe, vikend 7-16", "");
        Define(ObracunParam.ProgramerskaPogodbaVik16_22, "Pogodba, vikend 16-22", "");
        Define(ObracunParam.ProgramerskaBrezPogodbeVik16_22, "Brez pogodbe, vikend 16-22", "");
        Define(ObracunParam.ProgramerskaPogodbaVik22_7, "Pogodba, vikend 22-7", "");
        Define(ObracunParam.ProgramerskaBrezPogodbeVik22_7, "Brez pogodbe, vikend 22-7", "");

        // Programerska - Praznik
        Define(ObracunParam.ProgramerskaPogodbaP7_16, "Pogodba, praznik 7-16", "");
        Define(ObracunParam.ProgramerskaBrezPogodbeP7_16, "Brez pogodbe, praznik 7-16", "");
        Define(ObracunParam.ProgramerskaPogodbaP16_22, "Pogodba, praznik 16-22", "");
        Define(ObracunParam.ProgramerskaBrezPogodbeP16_22, "Brez pogodbe, praznik 16-22", "");
        Define(ObracunParam.ProgramerskaPogodbaP22_7, "Pogodba, praznik 22-7", "");
        Define(ObracunParam.ProgramerskaBrezPogodbeP22_7, "Brez pogodbe, praznik 22-7", "");

        // Teren_Servisna - Delavnik
        Define(ObracunParam.Teren_ServisnaPogodbaDel7_16, "Pogodba, delavnik 7-16", "");
        Define(ObracunParam.Teren_ServisnaBrezPogodbeDel7_16, "Brez pogodbe, delavnik 7-16", "");
        Define(ObracunParam.Teren_ServisnaPogodbaDel16_22, "Pogodba, delavnik 16-22", "");
        Define(ObracunParam.Teren_ServisnaBrezPogodbeDel16_22, "Brez pogodbe, delavnik 16-22", "");
        Define(ObracunParam.Teren_ServisnaPogodbaDel22_7, "Pogodba, delavnik 22-7", "");
        Define(ObracunParam.Teren_ServisnaBrezPogodbeDel22_7, "Brez pogodbe, delavnik 22-7", "");

        // Teren_Servisna - Vikend
        Define(ObracunParam.Teren_ServisnaPogodbaVik7_16, "Pogodba, vikend 7-16", "");
        Define(ObracunParam.Teren_ServisnaBrezPogodbeVik7_16, "Brez pogodbe, vikend 7-16", "");
        Define(ObracunParam.Teren_ServisnaPogodbaVik16_22, "Pogodba, vikend 16-22", "");
        Define(ObracunParam.Teren_ServisnaBrezPogodbeVik16_22, "Brez pogodbe, vikend 16-22", "");
        Define(ObracunParam.Teren_ServisnaPogodbaVik22_7, "Pogodba, vikend 22-7", "");
        Define(ObracunParam.Teren_ServisnaBrezPogodbeVik22_7, "Brez pogodbe, vikend 22-7", "");

        // Teren_Servisna - Praznik
        Define(ObracunParam.Teren_ServisnaPogodbaP7_16, "Pogodba, praznik 7-16", "");
        Define(ObracunParam.Teren_ServisnaBrezPogodbeP7_16, "Brez pogodbe, praznik 7-16", "");
        Define(ObracunParam.Teren_ServisnaPogodbaP16_22, "Pogodba, praznik 16-22", "");
        Define(ObracunParam.Teren_ServisnaBrezPogodbeP16_22, "Brez pogodbe, praznik 16-22", "");
        Define(ObracunParam.Teren_ServisnaPogodbaP22_7, "Pogodba, praznik 22-7", "");
        Define(ObracunParam.Teren_ServisnaBrezPogodbeP22_7, "Brez pogodbe, praznik 22-7", "");

        // Teren_Strokovna - Delavnik
        Define(ObracunParam.Teren_StrokovnaPogodbaDel7_16, "Pogodba, delavnik 7-16", "");
        Define(ObracunParam.Teren_StrokovnaBrezPogodbeDel7_16, "Brez pogodbe, delavnik 7-16", "");
        Define(ObracunParam.Teren_StrokovnaPogodbaDel16_22, "Pogodba, delavnik 16-22", "");
        Define(ObracunParam.Teren_StrokovnaBrezPogodbeDel16_22, "Brez pogodbe, delavnik 16-22", "");
        Define(ObracunParam.Teren_StrokovnaPogodbaDel22_7, "Pogodba, delavnik 22-7", "");
        Define(ObracunParam.Teren_StrokovnaBrezPogodbeDel22_7, "Brez pogodbe, delavnik 22-7", "");

        // Teren_Strokovna - Vikend
        Define(ObracunParam.Teren_StrokovnaPogodbaVik7_16, "Pogodba, vikend 7-16", "");
        Define(ObracunParam.Teren_StrokovnaBrezPogodbeVik7_16, "Brez pogodbe, vikend 7-16", "");
        Define(ObracunParam.Teren_StrokovnaPogodbaVik16_22, "Pogodba, vikend 16-22", "");
        Define(ObracunParam.Teren_StrokovnaBrezPogodbeVik16_22, "Brez pogodbe, vikend 16-22", "");
        Define(ObracunParam.Teren_StrokovnaPogodbaVik22_7, "Pogodba, vikend 22-7", "");
        Define(ObracunParam.Teren_StrokovnaBrezPogodbeVik22_7, "Brez pogodbe, vikend 22-7", "");

        // Teren_Strokovna - Praznik
        Define(ObracunParam.Teren_StrokovnaPogodbaP7_16, "Pogodba, praznik 7-16", "");
        Define(ObracunParam.Teren_StrokovnaBrezPogodbeP7_16, "Brez pogodbe, praznik 7-16", "");
        Define(ObracunParam.Teren_StrokovnaPogodbaP16_22, "Pogodba, praznik 16-22", "");
        Define(ObracunParam.Teren_StrokovnaBrezPogodbeP16_22, "Brez pogodbe, praznik 16-22", "");
        Define(ObracunParam.Teren_StrokovnaPogodbaP22_7, "Pogodba, praznik 22-7", "");
        Define(ObracunParam.Teren_StrokovnaBrezPogodbeP22_7, "Brez pogodbe, praznik 22-7", "");

        // Teren_Programerska - Delavnik
        Define(ObracunParam.Teren_ProgramerskaPogodbaDel7_16, "Pogodba, delavnik 7-16", "");
        Define(ObracunParam.Teren_ProgramerskaBrezPogodbeDel7_16, "Brez pogodbe, delavnik 7-16", "");
        Define(ObracunParam.Teren_ProgramerskaPogodbaDel16_22, "Pogodba, delavnik 16-22", "");
        Define(ObracunParam.Teren_ProgramerskaBrezPogodbeDel16_22, "Brez pogodbe, delavnik 16-22", "");
        Define(ObracunParam.Teren_ProgramerskaPogodbaDel22_7, "Pogodba, delavnik 22-7", "");
        Define(ObracunParam.Teren_ProgramerskaBrezPogodbeDel22_7, "Brez pogodbe, delavnik 22-7", "");

        // Teren_Programerska - Vikend
        Define(ObracunParam.Teren_ProgramerskaPogodbaVik7_16, "Pogodba, vikend 7-16", "");
        Define(ObracunParam.Teren_ProgramerskaBrezPogodbeVik7_16, "Brez pogodbe, vikend 7-16", "");
        Define(ObracunParam.Teren_ProgramerskaPogodbaVik16_22, "Pogodba, vikend 16-22", "");
        Define(ObracunParam.Teren_ProgramerskaBrezPogodbeVik16_22, "Brez pogodbe, vikend 16-22", "");
        Define(ObracunParam.Teren_ProgramerskaPogodbaVik22_7, "Pogodba, vikend 22-7", "");
        Define(ObracunParam.Teren_ProgramerskaBrezPogodbeVik22_7, "Brez pogodbe, vikend 22-7", "");

        // Teren_Programerska - Praznik
        Define(ObracunParam.Teren_ProgramerskaPogodbaP7_16, "Pogodba, praznik 7-16", "");
        Define(ObracunParam.Teren_ProgramerskaBrezPogodbeP7_16, "Brez pogodbe, praznik 7-16", "");
        Define(ObracunParam.Teren_ProgramerskaPogodbaP16_22, "Pogodba, praznik 16-22", "");
        Define(ObracunParam.Teren_ProgramerskaBrezPogodbeP16_22, "Brez pogodbe, praznik 16-22", "");
        Define(ObracunParam.Teren_ProgramerskaPogodbaP22_7, "Pogodba, praznik 22-7", "");
        Define(ObracunParam.Teren_ProgramerskaBrezPogodbeP22_7, "Brez pogodbe, praznik 22-7", "");

        // Delavnica_Servisna - Delavnik
        Define(ObracunParam.Delavnica_ServisnaPogodbaDel7_16, "Pogodba, delavnik 7-16", "");
        Define(ObracunParam.Delavnica_ServisnaBrezPogodbeDel7_16, "Brez pogodbe, delavnik 7-16", "");
        Define(ObracunParam.Delavnica_ServisnaPogodbaDel16_22, "Pogodba, delavnik 16-22", "");
        Define(ObracunParam.Delavnica_ServisnaBrezPogodbeDel16_22, "Brez pogodbe, delavnik 16-22", "");
        Define(ObracunParam.Delavnica_ServisnaPogodbaDel22_7, "Pogodba, delavnik 22-7", "");
        Define(ObracunParam.Delavnica_ServisnaBrezPogodbeDel22_7, "Brez pogodbe, delavnik 22-7", "");

        // Delavnica_Servisna - Vikend
        Define(ObracunParam.Delavnica_ServisnaPogodbaVik7_16, "Pogodba, vikend 7-16", "");
        Define(ObracunParam.Delavnica_ServisnaBrezPogodbeVik7_16, "Brez pogodbe, vikend 7-16", "");
        Define(ObracunParam.Delavnica_ServisnaPogodbaVik16_22, "Pogodba, vikend 16-22", "");
        Define(ObracunParam.Delavnica_ServisnaBrezPogodbeVik16_22, "Brez pogodbe, vikend 16-22", "");
        Define(ObracunParam.Delavnica_ServisnaPogodbaVik22_7, "Pogodba, vikend 22-7", "");
        Define(ObracunParam.Delavnica_ServisnaBrezPogodbeVik22_7, "Brez pogodbe, vikend 22-7", "");

        // Delavnica_Servisna - Praznik
        Define(ObracunParam.Delavnica_ServisnaPogodbaP7_16, "Pogodba, praznik 7-16", "");
        Define(ObracunParam.Delavnica_ServisnaBrezPogodbeP7_16, "Brez pogodbe, praznik 7-16", "");
        Define(ObracunParam.Delavnica_ServisnaPogodbaP16_22, "Pogodba, praznik 16-22", "");
        Define(ObracunParam.Delavnica_ServisnaBrezPogodbeP16_22, "Brez pogodbe, praznik 16-22", "");
        Define(ObracunParam.Delavnica_ServisnaPogodbaP22_7, "Pogodba, praznik 22-7", "");
        Define(ObracunParam.Delavnica_ServisnaBrezPogodbeP22_7, "Brez pogodbe, praznik 22-7", "");

        // Delavnica_Strokovna - Delavnik
        Define(ObracunParam.Delavnica_StrokovnaPogodbaDel7_16, "Pogodba, delavnik 7-16", "");
        Define(ObracunParam.Delavnica_StrokovnaBrezPogodbeDel7_16, "Brez pogodbe, delavnik 7-16", "");
        Define(ObracunParam.Delavnica_StrokovnaPogodbaDel16_22, "Pogodba, delavnik 16-22", "");
        Define(ObracunParam.Delavnica_StrokovnaBrezPogodbeDel16_22, "Brez pogodbe, delavnik 16-22", "");
        Define(ObracunParam.Delavnica_StrokovnaPogodbaDel22_7, "Pogodba, delavnik 22-7", "");
        Define(ObracunParam.Delavnica_StrokovnaBrezPogodbeDel22_7, "Brez pogodbe, delavnik 22-7", "");

        // Delavnica_Strokovna - Vikend
        Define(ObracunParam.Delavnica_StrokovnaPogodbaVik7_16, "Pogodba, vikend 7-16", "");
        Define(ObracunParam.Delavnica_StrokovnaBrezPogodbeVik7_16, "Brez pogodbe, vikend 7-16", "");
        Define(ObracunParam.Delavnica_StrokovnaPogodbaVik16_22, "Pogodba, vikend 16-22", "");
        Define(ObracunParam.Delavnica_StrokovnaBrezPogodbeVik16_22, "Brez pogodbe, vikend 16-22", "");
        Define(ObracunParam.Delavnica_StrokovnaPogodbaVik22_7, "Pogodba, vikend 22-7", "");
        Define(ObracunParam.Delavnica_StrokovnaBrezPogodbeVik22_7, "Brez pogodbe, vikend 22-7", "");

        // Delavnica_Strokovna - Praznik
        Define(ObracunParam.Delavnica_StrokovnaPogodbaP7_16, "Pogodba, praznik 7-16", "");
        Define(ObracunParam.Delavnica_StrokovnaBrezPogodbeP7_16, "Brez pogodbe, praznik 7-16", "");
        Define(ObracunParam.Delavnica_StrokovnaPogodbaP16_22, "Pogodba, praznik 16-22", "");
        Define(ObracunParam.Delavnica_StrokovnaBrezPogodbeP16_22, "Brez pogodbe, praznik 16-22", "");
        Define(ObracunParam.Delavnica_StrokovnaPogodbaP22_7, "Pogodba, praznik 22-7", "");
        Define(ObracunParam.Delavnica_StrokovnaBrezPogodbeP22_7, "Brez pogodbe, praznik 22-7", "");

        // Delavnica_Programerska - Delavnik
        Define(ObracunParam.Delavnica_ProgramerskaPogodbaDel7_16, "Pogodba, delavnik 7-16", "");
        Define(ObracunParam.Delavnica_ProgramerskaBrezPogodbeDel7_16, "Brez pogodbe, delavnik 7-16", "");
        Define(ObracunParam.Delavnica_ProgramerskaPogodbaDel16_22, "Pogodba, delavnik 16-22", "");
        Define(ObracunParam.Delavnica_ProgramerskaBrezPogodbeDel16_22, "Brez pogodbe, delavnik 16-22", "");
        Define(ObracunParam.Delavnica_ProgramerskaPogodbaDel22_7, "Pogodba, delavnik 22-7", "");
        Define(ObracunParam.Delavnica_ProgramerskaBrezPogodbeDel22_7, "Brez pogodbe, delavnik 22-7", "");

        // Delavnica_Programerska - Vikend
        Define(ObracunParam.Delavnica_ProgramerskaPogodbaVik7_16, "Pogodba, vikend 7-16", "");
        Define(ObracunParam.Delavnica_ProgramerskaBrezPogodbeVik7_16, "Brez pogodbe, vikend 7-16", "");
        Define(ObracunParam.Delavnica_ProgramerskaPogodbaVik16_22, "Pogodba, vikend 16-22", "");
        Define(ObracunParam.Delavnica_ProgramerskaBrezPogodbeVik16_22, "Brez pogodbe, vikend 16-22", "");
        Define(ObracunParam.Delavnica_ProgramerskaPogodbaVik22_7, "Pogodba, vikend 22-7", "");
        Define(ObracunParam.Delavnica_ProgramerskaBrezPogodbeVik22_7, "Brez pogodbe, vikend 22-7", "");

        // Delavnica_Programerska - Praznik
        Define(ObracunParam.Delavnica_ProgramerskaPogodbaP7_16, "Pogodba, praznik 7-16", "");
        Define(ObracunParam.Delavnica_ProgramerskaBrezPogodbeP7_16, "Brez pogodbe, praznik 7-16", "");
        Define(ObracunParam.Delavnica_ProgramerskaPogodbaP16_22, "Pogodba, praznik 16-22", "");
        Define(ObracunParam.Delavnica_ProgramerskaBrezPogodbeP16_22, "Brez pogodbe, praznik 16-22", "");
        Define(ObracunParam.Delavnica_ProgramerskaPogodbaP22_7, "Pogodba, praznik 22-7", "");
        Define(ObracunParam.Delavnica_ProgramerskaBrezPogodbeP22_7, "Brez pogodbe, praznik 22-7", "");

        // Temno ozadje — seznam uporabnikov (loèeni z vejico)
        Define(ObracunParam.TemnoOzadje, "Uporabniki s temnim ozadjem", "");
    }

    private void Define(ObracunParam param, string legenda, object value)
    {
        _items[ToKey(param)] = new ObracunParameter(ToKey(param), legenda, value);
    }

    // === Enum-based accessor methods ===

    private void EnsureDefined(ObracunParam param)
    {
        var key = ToKey(param);
        if (!_items.ContainsKey(key))
            throw new InvalidOperationException(
                $"Parameter '{param}' (kljuè '{key}') ni definiran v DefineAll(). Dodajte Define({param}, ...) v ParametriService.DefineAll().");
    }

    public ObracunParameter? Get(ObracunParam param) { EnsureDefined(param); return Get(ToKey(param)); }
    public int GetInt(ObracunParam param) { EnsureDefined(param); return GetInt(ToKey(param)); }
    public double GetDouble(ObracunParam param) { EnsureDefined(param); return GetDouble(ToKey(param)); }
    public string? GetString(ObracunParam param) { EnsureDefined(param); return GetString(ToKey(param)); }
    public DateTime? GetDate(ObracunParam param) { EnsureDefined(param); return GetDate(ToKey(param)); }
    public bool GetBool(ObracunParam param, bool defaultValue = false) { EnsureDefined(param); return GetBool(ToKey(param), defaultValue); }

    public async Task SaveToDatabaseAsync(ObracunParam param, object vrednost)
    {
        EnsureDefined(param);
        await SaveToDatabaseAsync(ToKey(param), vrednost);
    }

    // === Internal string-based methods (used by LoadFromDatabaseAsync and enum wrappers) ===

    private ObracunParameter? Get(string naziv)
    {
        if (_items.TryGetValue(naziv, out var p)) return p;
        return null;
    }

    private object? GetValue(string naziv) => Get(naziv)?.Value;
    private int GetInt(string naziv) => Get(naziv)?.AsInt() ?? default;
    private double GetDouble(string naziv) => Get(naziv)?.AsDouble() ?? default;
    private string? GetString(string naziv) => Get(naziv)?.AsString();
    private DateTime? GetDate(string naziv) => Get(naziv) != null ? Get(naziv)!.AsDate() : (DateTime?)null;

    private bool GetBool(string naziv, bool defaultValue = false)
    {
        var p = Get(naziv);
        if (p == null) return defaultValue;
        return p.AsBool();
    }

    /// <summary>
    /// Vrne vse definirane parametre (za prikaz v UI)
    /// </summary>
    public IReadOnlyDictionary<string, ObracunParameter> GetAll() => _items;

    /// <summary>
    /// Naloži parametre iz Firebird baze (tabela OBRACUN_PARAMETER)
    /// </summary>
    public async Task LoadFromDatabaseAsync()
    {
        if (_loaded) return;

        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand("SELECT NAZIV, VREDNOST FROM OBRACUN_PARAMETER", connection);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var naziv = reader.GetString(0).Trim();
            var vrednost = reader.IsDBNull(1) ? null : reader.GetString(1);

            if (!_items.ContainsKey(naziv))
                continue;

            if (vrednost != null)
            {
                _items[naziv].UpdateFromString(vrednost);
            }
        }

        _loaded = true;
    }

    /// <summary>
    /// Shrani parameter v bazo. Èe parameter ne obstaja, ga ustvari.
    /// </summary>
    private async Task SaveToDatabaseAsync(string naziv, object vrednost)
    {
        await using var connection = _connectionManager.GetConnection();
        await connection.OpenAsync();

        await using var cmd = new FbCommand(
            "UPDATE OR INSERT INTO OBRACUN_PARAMETER (NAZIV, VREDNOST) VALUES (@NAZIV, @VREDNOST) MATCHING (NAZIV)",
            connection);

        cmd.Parameters.AddWithValue("@NAZIV", naziv);
        cmd.Parameters.AddWithValue("@VREDNOST", vrednost?.ToString() ?? "");

        await cmd.ExecuteNonQueryAsync();

        // Posodobi lokalno vrednost
        if (_items.ContainsKey(naziv))
        {
            _items[naziv].UpdateFromString(vrednost?.ToString() ?? "");
        }
    }

    /// <summary>
    /// Ali ima uporabnik vklopljeno temno ozadje.
    /// </summary>
    public bool ImaTemnoOzadje(string uporabniskoIme)
    {
        var seznam = GetString(ObracunParam.TemnoOzadje) ?? "";
        return seznam
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(uporabniskoIme, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Nastavi ali odstrani uporabnika iz seznama temnega ozadja in shrani v bazo.
    /// </summary>
    public async Task NastaviTemnoOzadjeAsync(string uporabniskoIme, bool temno)
    {
        var seznam = GetString(ObracunParam.TemnoOzadje) ?? "";
        var uporabniki = seznam
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (temno)
            uporabniki.Add(uporabniskoIme);
        else
            uporabniki.Remove(uporabniskoIme);

        var novaVrednost = string.Join(",", uporabniki);
        await SaveToDatabaseAsync(ObracunParam.TemnoOzadje, novaVrednost);
    }
}
