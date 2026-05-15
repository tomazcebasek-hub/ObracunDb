namespace ObracunDb.Data.DTOs;

/// <summary>
/// Filter za prikaz nalogov v popupu pregleda ur.
/// </summary>
public enum PregledUrFilter
{
    /// <summary>Vsi nalogi serviserja v obdobju.</summary>
    Vsi,
    /// <summary>Samo NOM nalogi.</summary>
    Nom,
    /// <summary>Samo nalogi za partner 23900 (Pos elektronček) in ne NOM.</summary>
    Partner23900,
    /// <summary>Stranke - vsi nalogi (ne NOM, ne 23900).</summary>
    StrankeVse,
    /// <summary>Stranke - samo nalogi z urami v pasu 07-16.</summary>
    Stranke_7_16,
    /// <summary>Stranke - samo nalogi z urami v pasu 16-22.</summary>
    Stranke_16_22,
    /// <summary>Stranke - samo nalogi z urami v pasu 22-07.</summary>
    Stranke_22_7
}
