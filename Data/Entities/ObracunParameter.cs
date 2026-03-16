namespace ObracunDb.Data.Entities;

/// <summary>
/// Model za posamezen parameter iz tabele OBRACUN_PARAMETER
/// </summary>
public class ObracunParameter
{
    public string Naziv { get; }
    public string Legenda { get; }
    public object Value { get; set; }

    public ObracunParameter(string naziv, string legenda, object value)
    {
        Naziv = naziv;
        Legenda = legenda;
        Value = value;
    }

    public int AsInt() => Convert.ToInt32(Value);
    public double AsDouble() => Convert.ToDouble(Value);
    public string AsString() => Convert.ToString(Value) ?? "";
    public DateTime AsDate() => Convert.ToDateTime(Value);
    public bool AsBool() => Value?.ToString() == "1" || Value?.ToString()?.ToLower() == "true";

    public void UpdateFromString(string s)
    {
        if (Value is int)
        {
            Value = int.Parse(s);
            return;
        }
        if (Value is long)
        {
            Value = long.Parse(s);
            return;
        }
        if (Value is double || Value is float || Value is decimal)
        {
            Value = Convert.ToDouble(s);
            return;
        }
        if (Value is DateTime)
        {
            Value = DateTime.Parse(s);
            return;
        }
        // default to string
        Value = s;
    }
}
