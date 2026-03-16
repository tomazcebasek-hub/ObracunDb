namespace ObracunDb.Data.Entities;

/// <summary>
/// Enum za vse parametre iz tabele OBRACUN_PARAMETER.
/// Ime enum člana se ujema z NAZIV v bazi (razen izjem, ki jih mapira ParametriService.ToKey).
/// </summary>
public enum ObracunParam
{
    MesecObracuna,
    LetoObracuna,
    Praznik1,
    Praznik2,
    Praznik3,
    Praznik4,
    Praznik5,
    ProcentPopustaPogodbe,
    SifraKilometrina,
    TolerancaMinut,

    // FAW
    FawDatumRacuna,
    FawKomercialist,

    // VASCO API
    VascoApiUrl,
    VascoApiUporabnik,
    VascoApiGeslo,
    VascoApiDavcna,

    // Servisna - Delavnik
    ServisnaPogodbaDel7_16,
    ServisnaBrezPogodbeDel7_16,
    ServisnaPogodbaDel16_22,
    ServisnaBrezPogodbeDel16_22,
    ServisnaPogodbaDel22_7,
    ServisnaBrezPogodbeDel22_7,

    // Servisna - Vikend
    ServisnaPogodbaVik7_16,
    ServisnaBrezPogodbeVik7_16,
    ServisnaPogodbaVik16_22,
    ServisnaBrezPogodbeVik16_22,
    ServisnaPogodbaVik22_7,
    ServisnaBrezPogodbeVik22_7,

    // Servisna - Praznik
    ServisnaPogodbaP7_16,
    ServisnaBrezPogodbeP7_16,
    ServisnaPogodbaP16_22,
    ServisnaBrezPogodbeP16_22,
    ServisnaPogodbaP22_7,
    ServisnaBrezPogodbeP22_7,

    // Strokovna - Delavnik
    StrokovnaPogodbaDel7_16,
    StrokovnaBrezPogodbeDel7_16,
    StrokovnaPogodbaDel16_22,
    StrokovnaBrezPogodbeDel16_22,
    StrokovnaPogodbaDel22_7,
    StrokovnaBrezPogodbeDel22_7,

    // Strokovna - Vikend
    StrokovnaPogodbaVik7_16,
    StrokovnaBrezPogodbeVik7_16,
    StrokovnaPogodbaVik16_22,
    StrokovnaBrezPogodbeVik16_22,
    StrokovnaPogodbaVik22_7,
    StrokovnaBrezPogodbeVik22_7,

    // Strokovna - Praznik
    StrokovnaPogodbaP7_16,
    StrokovnaBrezPogodbeP7_16,
    StrokovnaPogodbaP16_22,
    StrokovnaBrezPogodbeP16_22,
    StrokovnaPogodbaP22_7,
    StrokovnaBrezPogodbeP22_7,

    // Programerska - Delavnik
    ProgramerskaPogodbaDel7_16,
    ProgramerskaBrezPogodbeDel7_16,
    ProgramerskaPogodbaDel16_22,
    ProgramerskaBrezPogodbeDel16_22,
    ProgramerskaPogodbaDel22_7,
    ProgramerskaBrezPogodbeDel22_7,

    // Programerska - Vikend
    ProgramerskaPogodbaVik7_16,
    ProgramerskaBrezPogodbeVik7_16,
    ProgramerskaPogodbaVik16_22,
    ProgramerskaBrezPogodbeVik16_22,
    ProgramerskaPogodbaVik22_7,
    ProgramerskaBrezPogodbeVik22_7,

    // Programerska - Praznik
    ProgramerskaPogodbaP7_16,
    ProgramerskaBrezPogodbeP7_16,
    ProgramerskaPogodbaP16_22,
    ProgramerskaBrezPogodbeP16_22,
    ProgramerskaPogodbaP22_7,
    ProgramerskaBrezPogodbeP22_7,

    // Teren_Servisna - Delavnik
    Teren_ServisnaPogodbaDel7_16,
    Teren_ServisnaBrezPogodbeDel7_16,
    Teren_ServisnaPogodbaDel16_22,
    Teren_ServisnaBrezPogodbeDel16_22,
    Teren_ServisnaPogodbaDel22_7,
    Teren_ServisnaBrezPogodbeDel22_7,

    // Teren_Servisna - Vikend
    Teren_ServisnaPogodbaVik7_16,
    Teren_ServisnaBrezPogodbeVik7_16,
    Teren_ServisnaPogodbaVik16_22,
    Teren_ServisnaBrezPogodbeVik16_22,
    Teren_ServisnaPogodbaVik22_7,
    Teren_ServisnaBrezPogodbeVik22_7,

    // Teren_Servisna - Praznik
    Teren_ServisnaPogodbaP7_16,
    Teren_ServisnaBrezPogodbeP7_16,
    Teren_ServisnaPogodbaP16_22,
    Teren_ServisnaBrezPogodbeP16_22,
    Teren_ServisnaPogodbaP22_7,
    Teren_ServisnaBrezPogodbeP22_7,

    // Teren_Strokovna - Delavnik
    Teren_StrokovnaPogodbaDel7_16,
    Teren_StrokovnaBrezPogodbeDel7_16,
    Teren_StrokovnaPogodbaDel16_22,
    Teren_StrokovnaBrezPogodbeDel16_22,
    Teren_StrokovnaPogodbaDel22_7,
    Teren_StrokovnaBrezPogodbeDel22_7,

    // Teren_Strokovna - Vikend
    Teren_StrokovnaPogodbaVik7_16,
    Teren_StrokovnaBrezPogodbeVik7_16,
    Teren_StrokovnaPogodbaVik16_22,
    Teren_StrokovnaBrezPogodbeVik16_22,
    Teren_StrokovnaPogodbaVik22_7,
    Teren_StrokovnaBrezPogodbeVik22_7,

    // Teren_Strokovna - Praznik
    Teren_StrokovnaPogodbaP7_16,
    Teren_StrokovnaBrezPogodbeP7_16,
    Teren_StrokovnaPogodbaP16_22,
    Teren_StrokovnaBrezPogodbeP16_22,
    Teren_StrokovnaPogodbaP22_7,
    Teren_StrokovnaBrezPogodbeP22_7,

    // Teren_Programerska - Delavnik
    Teren_ProgramerskaPogodbaDel7_16,
    Teren_ProgramerskaBrezPogodbeDel7_16,
    Teren_ProgramerskaPogodbaDel16_22,
    Teren_ProgramerskaBrezPogodbeDel16_22,
    Teren_ProgramerskaPogodbaDel22_7,
    Teren_ProgramerskaBrezPogodbeDel22_7,

    // Teren_Programerska - Vikend
    Teren_ProgramerskaPogodbaVik7_16,
    Teren_ProgramerskaBrezPogodbeVik7_16,
    Teren_ProgramerskaPogodbaVik16_22,
    Teren_ProgramerskaBrezPogodbeVik16_22,
    Teren_ProgramerskaPogodbaVik22_7,
    Teren_ProgramerskaBrezPogodbeVik22_7,

    // Teren_Programerska - Praznik
    Teren_ProgramerskaPogodbaP7_16,
    Teren_ProgramerskaBrezPogodbeP7_16,
    Teren_ProgramerskaPogodbaP16_22,
    Teren_ProgramerskaBrezPogodbeP16_22,
    Teren_ProgramerskaPogodbaP22_7,
    Teren_ProgramerskaBrezPogodbeP22_7,

    // Delavnica_Servisna - Delavnik
    Delavnica_ServisnaPogodbaDel7_16,
    Delavnica_ServisnaBrezPogodbeDel7_16,
    Delavnica_ServisnaPogodbaDel16_22,
    Delavnica_ServisnaBrezPogodbeDel16_22,
    Delavnica_ServisnaPogodbaDel22_7,
    Delavnica_ServisnaBrezPogodbeDel22_7,

    // Delavnica_Servisna - Vikend
    Delavnica_ServisnaPogodbaVik7_16,
    Delavnica_ServisnaBrezPogodbeVik7_16,
    Delavnica_ServisnaPogodbaVik16_22,
    Delavnica_ServisnaBrezPogodbeVik16_22,
    Delavnica_ServisnaPogodbaVik22_7,
    Delavnica_ServisnaBrezPogodbeVik22_7,

    // Delavnica_Servisna - Praznik
    Delavnica_ServisnaPogodbaP7_16,
    Delavnica_ServisnaBrezPogodbeP7_16,
    Delavnica_ServisnaPogodbaP16_22,
    Delavnica_ServisnaBrezPogodbeP16_22,
    Delavnica_ServisnaPogodbaP22_7,
    Delavnica_ServisnaBrezPogodbeP22_7,

    // Delavnica_Strokovna - Delavnik
    Delavnica_StrokovnaPogodbaDel7_16,
    Delavnica_StrokovnaBrezPogodbeDel7_16,
    Delavnica_StrokovnaPogodbaDel16_22,
    Delavnica_StrokovnaBrezPogodbeDel16_22,
    Delavnica_StrokovnaPogodbaDel22_7,
    Delavnica_StrokovnaBrezPogodbeDel22_7,

    // Delavnica_Strokovna - Vikend
    Delavnica_StrokovnaPogodbaVik7_16,
    Delavnica_StrokovnaBrezPogodbeVik7_16,
    Delavnica_StrokovnaPogodbaVik16_22,
    Delavnica_StrokovnaBrezPogodbeVik16_22,
    Delavnica_StrokovnaPogodbaVik22_7,
    Delavnica_StrokovnaBrezPogodbeVik22_7,

    // Delavnica_Strokovna - Praznik
    Delavnica_StrokovnaPogodbaP7_16,
    Delavnica_StrokovnaBrezPogodbeP7_16,
    Delavnica_StrokovnaPogodbaP16_22,
    Delavnica_StrokovnaBrezPogodbeP16_22,
    Delavnica_StrokovnaPogodbaP22_7,
    Delavnica_StrokovnaBrezPogodbeP22_7,

    // Delavnica_Programerska - Delavnik
    Delavnica_ProgramerskaPogodbaDel7_16,
    Delavnica_ProgramerskaBrezPogodbeDel7_16,
    Delavnica_ProgramerskaPogodbaDel16_22,
    Delavnica_ProgramerskaBrezPogodbeDel16_22,
    Delavnica_ProgramerskaPogodbaDel22_7,
    Delavnica_ProgramerskaBrezPogodbeDel22_7,

    // Delavnica_Programerska - Vikend
    Delavnica_ProgramerskaPogodbaVik7_16,
    Delavnica_ProgramerskaBrezPogodbeVik7_16,
    Delavnica_ProgramerskaPogodbaVik16_22,
    Delavnica_ProgramerskaBrezPogodbeVik16_22,
    Delavnica_ProgramerskaPogodbaVik22_7,
    Delavnica_ProgramerskaBrezPogodbeVik22_7,

    // Delavnica_Programerska - Praznik
    Delavnica_ProgramerskaPogodbaP7_16,
    Delavnica_ProgramerskaBrezPogodbeP7_16,
    Delavnica_ProgramerskaPogodbaP16_22,
    Delavnica_ProgramerskaBrezPogodbeP16_22,
    Delavnica_ProgramerskaPogodbaP22_7,
    Delavnica_ProgramerskaBrezPogodbeP22_7,

    // Temno ozadje
    TemnoOzadje
}
