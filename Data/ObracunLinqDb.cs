using System.Data.Common;
using FirebirdSql.Data.FirebirdClient;
using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.Firebird;
using ObracunDb.Data.Entities;

namespace ObracunDb.Data;

/// <summary>
/// LinqToDB DataConnection za ObracunIzvedbaService.
/// </summary>
public class ObracunLinqDb : DataConnection
{
    private ObracunLinqDb(FbConnection connection, string connectionString)
        : base(FirebirdTools.GetDataProvider(FirebirdVersion.AutoDetect, connectionString, connection, null), connection)
    {
    }

    public static ObracunLinqDb Create(string connectionString)
    {
        var conn = new FbConnection(connectionString);
        conn.Open();
        return new ObracunLinqDb(conn, connectionString);
    }

    public ITable<FaDnNalog> FaDnNalog => this.GetTable<FaDnNalog>();
    public ITable<FaDnNalogPoz> FaDnNalogPoz => this.GetTable<FaDnNalogPoz>();
    public ITable<FaPogodbe> FaPogodbe => this.GetTable<FaPogodbe>();
    public ITable<FaPogodbePoz> FaPogodbePoz => this.GetTable<FaPogodbePoz>();
    public ITable<FaArtikel> FaArtikel => this.GetTable<FaArtikel>();
    public ITable<FaPredracun> FaPredracun => this.GetTable<FaPredracun>();
    public ITable<FaPredracunKnjizba> FaPredracunKnjizba => this.GetTable<FaPredracunKnjizba>();
    public ITable<ObracunOsnutek> ObracunOsnutek => this.GetTable<ObracunOsnutek>();
    public ITable<ObracunOsnutekPos> ObracunOsnutekPos => this.GetTable<ObracunOsnutekPos>();
    public ITable<ObracunOsnutekNalogObracun> ObracunOsnutekNalogObracun => this.GetTable<ObracunOsnutekNalogObracun>();
    public ITable<ObracunDn> ObracunDn => this.GetTable<ObracunDn>();
    public ITable<PartnerMinute> ObracunMinute => this.GetTable<PartnerMinute>();
    public ITable<ObracunPorabaMinut> ObracunPorabaMinut => this.GetTable<ObracunPorabaMinut>();
    public ITable<ObracunLog> ObracunLog => this.GetTable<ObracunLog>();
    public ITable<ObracunPaketMinute> ObracunPaketMinute => this.GetTable<ObracunPaketMinute>();
    public ITable<Partner> Partner => this.GetTable<Partner>();
    public ITable<FaKomercialist> FaKomercialist => this.GetTable<FaKomercialist>();
    public ITable<ObracunDnPredracun> ObracunDnPredracun => this.GetTable<ObracunDnPredracun>();
}
