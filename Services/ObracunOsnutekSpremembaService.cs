using LinqToDB;
using ObracunDb.Data;
using ObracunDb.Data.Entities;

namespace ObracunDb.Services;

public class ObracunOsnutekSpremembaDto
{
    public int Id { get; set; }
    public int Mesec { get; set; }
    public int Leto { get; set; }
    public int Partner { get; set; }
    public string? NazivPartnerja { get; set; }
    public string Artikel { get; set; } = string.Empty;
    public string? NazivArtikla { get; set; }
    public decimal Kolicina { get; set; }
    public string? Opomba { get; set; }
    public string Uporabnik { get; set; } = string.Empty;
    public DateTime DatumVnosa { get; set; }
}

public class ObracunOsnutekSpremembaService
{
    private readonly FirebirdConnectionManager _connectionManager;
    private readonly AuthService _auth;

    public ObracunOsnutekSpremembaService(FirebirdConnectionManager connectionManager, AuthService auth)
    {
        _connectionManager = connectionManager;
        _auth = auth;
    }

    private ObracunLinqDb CreateDb() => ObracunLinqDb.Create(_connectionManager.ConnectionString);

    /// <summary>
    /// Spremembe za doloceni mesec/leto in partnerja.
    /// </summary>
    public List<ObracunOsnutekSpremembaDto> GetForPartner(int mesec, int leto, int partner)
    {
        using var db = CreateDb();
        var query = from s in db.ObracunOsnutekSprememba
                    where s.Mesec == mesec && s.Leto == leto && s.Partner == partner
                    join a in db.FaArtikel on s.Artikel equals a.Sifra into ja
                    from a in ja.DefaultIfEmpty()
                    orderby s.DatumVnosa descending
                    select new ObracunOsnutekSpremembaDto
                    {
                        Id = s.Id,
                        Mesec = s.Mesec,
                        Leto = s.Leto,
                        Partner = s.Partner,
                        Artikel = s.Artikel,
                        NazivArtikla = a == null ? null : a.Naziv,
                        Kolicina = s.Kolicina,
                        Opomba = s.Opomba,
                        Uporabnik = s.Uporabnik,
                        DatumVnosa = s.DatumVnosa
                    };
        return query.ToList();
    }

    /// <summary>
    /// Vse spremembe za doloceni mesec/leto (vsi partnerji).
    /// </summary>
    public List<ObracunOsnutekSpremembaDto> GetForObdobje(int mesec, int leto)
    {
        using var db = CreateDb();
        var query = from s in db.ObracunOsnutekSprememba
                    where s.Mesec == mesec && s.Leto == leto
                    join a in db.FaArtikel on s.Artikel equals a.Sifra into ja
                    from a in ja.DefaultIfEmpty()
                    join p in db.Partner on s.Partner equals p.Sifra into jp
                    from p in jp.DefaultIfEmpty()
                    orderby s.Partner, s.DatumVnosa descending
                    select new ObracunOsnutekSpremembaDto
                    {
                        Id = s.Id,
                        Mesec = s.Mesec,
                        Leto = s.Leto,
                        Partner = s.Partner,
                        NazivPartnerja = p == null ? null : p.Naziv,
                        Artikel = s.Artikel,
                        NazivArtikla = a == null ? null : a.Naziv,
                        Kolicina = s.Kolicina,
                        Opomba = s.Opomba,
                        Uporabnik = s.Uporabnik,
                        DatumVnosa = s.DatumVnosa
                    };
        return query.ToList();
    }

    public int Insert(int mesec, int leto, int partner, string artikel, decimal kolicina, string? opomba)
    {
        using var db = CreateDb();
        var entity = new ObracunOsnutekSprememba
        {
            Mesec = mesec,
            Leto = leto,
            Partner = partner,
            Artikel = artikel,
            Kolicina = kolicina,
            Opomba = string.IsNullOrWhiteSpace(opomba) ? null : opomba.Trim(),
            Uporabnik = _auth.CurrentUser?.UporabniskoIme ?? "?",
            DatumVnosa = DateTime.Now
        };
        return db.InsertWithInt32Identity(entity);
    }

    public void Update(int id, string artikel, decimal kolicina, string? opomba)
    {
        using var db = CreateDb();
        db.ObracunOsnutekSprememba
            .Where(s => s.Id == id)
            .Set(s => s.Artikel, artikel)
            .Set(s => s.Kolicina, kolicina)
            .Set(s => s.Opomba, string.IsNullOrWhiteSpace(opomba) ? null : opomba.Trim())
            .Update();
    }

    public void Delete(int id)
    {
        using var db = CreateDb();
        db.ObracunOsnutekSprememba.Where(s => s.Id == id).Delete();
    }
}
