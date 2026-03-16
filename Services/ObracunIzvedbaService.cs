using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using LinqToDB;
using ObracunDb.Data;
using ObracunDb.Data.Entities;
using ObracunDb.Services;

namespace ObracunDb.Services
{
    public class ObracunIzvedbaService
    {
        /// <summary>Enota artikla za obračun po četrtinah ure (15-minutni interval).</summary>
        public const string EnotaCetrtinUre = "Ura-m";

        private readonly FirebirdConnectionManager _connectionManager;
        private readonly ParametriService _parametri;

        public ObracunIzvedbaService(FirebirdConnectionManager connectionManager, ParametriService parametri)
        {
            _connectionManager = connectionManager;
            _parametri = parametri;
        }

        private ObracunLinqDb CreateDb()
        {
            return ObracunLinqDb.Create(_connectionManager.ConnectionString);
        }

        #region Helper razredi za od

        /// <summary>
        /// Posamezen vir minut iz partner_minute (OBRACUN_MINUTE).
        /// </summary>
        private class PartnerMinuteVir
        {
            public int IdPartnerMinute { get; set; }
            public int ZacetnoStanje { get; set; }
            public int PreostaloMinut { get; set; }
        }

        /// <summary>
        /// Posamezen vir minut iz predračuna.
        /// </summary>
        private class PredracunVir
        {
            public string PredracunStevilka { get; set; } = "";
            public int PredracunLeto { get; set; }
            public int ZacetnoStanje { get; set; }
            public int PreostaloMinut { get; set; }
        }

        /// <summary>
        /// Sklad minut za odštevanje z detajlnim sledenjem virov.
        /// </summary>
        private class MinutniSklad
        {
            public List<PartnerMinuteVir> PartnerMinuteViri { get; set; } = new();
            public List<PredracunVir> PredracunViri { get; set; } = new();
            public int RocnoPreostalo { get; set; }
            public int PogodbaPreostalo { get; set; }

            public int PartnerMinutePreostalo => PartnerMinuteViri.Sum(v => v.PreostaloMinut);
            public int PredracunPreostalo => PredracunViri.Sum(v => v.PreostaloMinut);
        }

        /// <summary>
        /// Rezultat odštevanja minut.
        /// </summary>
        private class OdstevanjeMinutRezultat
        {
            public int MinuteOdstetePartnerMinute { get; set; }
            public int MinuteOdstetePredracun { get; set; }
            public int MinuteOdsteteRocno { get; set; }
            public int MinuteOdstetePogodba { get; set; }
            public int MinuteZaObracun { get; set; }  // Koliko ostane za obračun
        }

        #endregion

        #region Odštevanje minut iz sklada

        /// <summary>
        /// Odšteje minute iz sklada po vrstnem redu: Ročno → Partner_minute → Predračun → Pogodba.
        /// Pri partner_minute in predračunih se prazni po posameznih virih.
        /// Če je povezaniPredracuni != null, se pri predračunih upoštevajo samo povezani viri.
        /// Če je uporabiPogodbo = false, se korak Pogodba preskoči (terenski nalogi).
        /// </summary>
        private static OdstevanjeMinutRezultat OdstejiMinuteIzSklada(MinutniSklad sklad, int minuteNaloga, HashSet<(string PredStevilka, int PredLeto)>? povezaniPredracuni = null, bool uporabiPogodbo = true)
        {
            var rezultat = new OdstevanjeMinutRezultat();
            var preostaleMinute = minuteNaloga;

            // 1. Ročno
            if (preostaleMinute > 0 && sklad.RocnoPreostalo > 0)
            {
                var odsteto = Math.Min(preostaleMinute, sklad.RocnoPreostalo);
                rezultat.MinuteOdsteteRocno = odsteto;
                sklad.RocnoPreostalo -= odsteto;
                preostaleMinute -= odsteto;
            }

            // 2. Partner_minute - prazni po posameznih virih
            foreach (var vir in sklad.PartnerMinuteViri)
            {
                if (preostaleMinute <= 0) break;
                if (vir.PreostaloMinut <= 0) continue;

                var odsteto = Math.Min(preostaleMinute, vir.PreostaloMinut);
                rezultat.MinuteOdstetePartnerMinute += odsteto;
                vir.PreostaloMinut -= odsteto;
                preostaleMinute -= odsteto;
            }

            // 3. Predračun - prazni po posameznih virih (samo povezani, če je podano)
            foreach (var vir in sklad.PredracunViri)
            {
                if (preostaleMinute <= 0) break;
                if (vir.PreostaloMinut <= 0) continue;

                // Če so podani povezani predračuni, preskoči nepovezane vire
                if (povezaniPredracuni != null && !povezaniPredracuni.Contains((vir.PredracunStevilka, vir.PredracunLeto)))
                    continue;

                var odsteto = Math.Min(preostaleMinute, vir.PreostaloMinut);
                rezultat.MinuteOdstetePredracun += odsteto;
                vir.PreostaloMinut -= odsteto;
                preostaleMinute -= odsteto;
            }

            // 4. Pogodba (samo za helpdesk naloge, ne za terenske)
            if (uporabiPogodbo && preostaleMinute > 0 && sklad.PogodbaPreostalo > 0)
            {
                var odsteto = Math.Min(preostaleMinute, sklad.PogodbaPreostalo);
                rezultat.MinuteOdstetePogodba = odsteto;
                sklad.PogodbaPreostalo -= odsteto;
                preostaleMinute -= odsteto;
            }

            rezultat.MinuteZaObracun = preostaleMinute;
            return rezultat;
        }

        #endregion

        public (bool Success, string Message, int RecordsProcessed, List<string> Log) IzvediObracun(int mesec, int leto, HashSet<DateTime>? prazniki = null)
        {
            var log = new List<string>();
            prazniki ??= new HashSet<DateTime>();

            try
            {
                log.Add($"=== Obračun za {mesec}/{leto} ===");

                if (prazniki.Count > 0)
                {
                    log.Add($"Prazniki: {string.Join(", ", prazniki.OrderBy(d => d).Select(d => d.ToString("dd.MM.yyyy")))}");
                }

                var connStr = _connectionManager.ConnectionString;
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    var errMsg = _connectionManager.HasConfigError 
                        ? $"Napaka konfiguracije: {_connectionManager.ConfigError}"
                        : "ConnectionString je prazen!";
                    log.Add($"NAPAKA: {errMsg}");
                    return (false, errMsg, 0, log);
                }

                using var db = CreateDb();
                var ctx = NaloziPodatke(db, mesec, leto, log, prazniki);

                PripraviTabele(ctx);

                var vsiPartnerji = ZberiPartnerje(ctx);
                log.Add($"Partnerjev za obračun: {vsiPartnerji.Count}");

                // Skupna razdelitev minut za vse partnerje
                var skupneMinute = new MinuteRazdelitev();

                // Manjkajoče šifre za obračun brez pogodbe (zberi enkrat)
                var manjkajoceSifre = new HashSet<string>();

                foreach (var partner in vsiPartnerji)
                {
                    var partnerData = PripraviPodatkeZaPartnerja(ctx, partner);
                    var result = ObdelajPartnerja(ctx, partnerData, manjkajoceSifre);
                    ShraniOsnutek(ctx, result);

                    // Prištej minute partnerja k skupnim
                    skupneMinute.Pristej(result.MinuteNalogov);
                }

                // Izpiši manjkajoče šifre (samo enkrat)
                if (manjkajoceSifre.Count > 0)
                {
                    log.Add("");
                    log.Add("============================================");
                    log.Add("=== NAPAKA: MANJKAJOČE ŠIFRE ARTIKLOV ===");
                    log.Add("============================================");
                    foreach (var sifra in manjkajoceSifre.OrderBy(s => s))
                    {
                        log.Add($"   - {sifra}");
                    }
                    log.Add("============================================");
                    log.Add("Prosim nastavite manjkajoče šifre v Parametri > Servisna.");
                }

                log.Add("");
                log.Add($"Konec: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");

                ShraniLog(db, mesec, leto, log);
                return (true, $"Obračun uspešno izveden. Pripravljenih osnutkov: {vsiPartnerji.Count}.", vsiPartnerji.Count, log);
            }
            catch (Exception ex)
            {
                log.Add("");
                log.Add($"NAPAKA: {ex.Message}");
                log.Add($"Tip: {ex.GetType().FullName}");
                log.Add($"StackTrace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    log.Add($"InnerException: {ex.InnerException.Message}");
                    log.Add($"InnerType: {ex.InnerException.GetType().FullName}");
                }
                log.Add($"Konec: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");

                try
                {
                    using var db = CreateDb();
                    ShraniLog(db, mesec, leto, log);
                }
                catch { }

                return (false, $"Napaka pri obračunu: {ex.Message}", 0, log);
            }
        }

        /// <summary>
        /// Izvede obračun samo za enega partnerja. Ohrani ročno vnešene postavke.
        /// </summary>
        public (bool Success, string Message) IzvedObracunZaPartnerja(int mesec, int leto, int partner, HashSet<DateTime>? prazniki = null)
        {
            prazniki ??= new HashSet<DateTime>();

            try
            {
                using var db = CreateDb();
                var log = new List<string>();
                log.Add($"=== Ponovni obračun za partnerja {partner}, {mesec}/{leto} ===");

                var ctx = NaloziPodatke(db, mesec, leto, log, prazniki);

                // Pobriši samo postavke za tega partnerja (razen ročnih)
                PripraviTabeleZaPartnerja(ctx, partner);

                // Obdelaj samo tega partnerja
                var manjkajoceSifre = new HashSet<string>();
                var partnerData = PripraviPodatkeZaPartnerja(ctx, partner);
                var result = ObdelajPartnerja(ctx, partnerData, manjkajoceSifre);
                ShraniOsnutek(ctx, result);

                if (manjkajoceSifre.Count > 0)
                {
                    var manjkajoceStr = string.Join(", ", manjkajoceSifre.OrderBy(s => s));
                    return (true, $"Obračun za partnerja {partner} izveden. OPOZORILO: Manjkajoče šifre: {manjkajoceStr}");
                }

                return (true, $"Obračun za partnerja {partner} uspešno izveden.");
            }
            catch (Exception ex)
            {
                return (false, $"Napaka pri obračunu: {ex.Message}");
            }
        }

        /// <summary>
        /// Pobriše podatke samo za enega partnerja (ohrani ročne postavke).
        /// </summary>
        private static void PripraviTabeleZaPartnerja(ObracunContext ctx, int partner)
        {
            // Izbriši postavke za tega partnerja (razen ročnih)
            ctx.Db.ObracunOsnutekPos
                .Where(p => p.Mesec == ctx.Mesec && p.Leto == ctx.Leto && p.Partner == partner && p.TipPostavke != TipPostavke.ROCNI)
                .Delete();

            // Izbriši osnutek za tega partnerja
            ctx.Db.ObracunOsnutek
                .Where(o => o.Mesec == ctx.Mesec && o.Leto == ctx.Leto && o.Partner == partner)
                .Delete();

            // Izbriši podrobnosti obračuna nalogov za tega partnerja
            ctx.Db.ObracunOsnutekNalogObracun
                .Where(n => n.Mesec == ctx.Mesec && n.Leto == ctx.Leto && n.Partner == partner)
                .Delete();

            // Izbriši porabo minut za tega partnerja (da se lahko ponovno izračuna)
            ctx.Db.ObracunPorabaMinut
                .Where(p => p.Mesec == ctx.Mesec && p.Leto == ctx.Leto && p.Partner == partner)
                .Delete();
        }

        #region Nalaganje podatkov

        private ObracunContext NaloziPodatke(ObracunLinqDb db, int mesec, int leto, List<string> log, HashSet<DateTime> prazniki)
        {
            var prviDan = new DateTime(leto, mesec, 1);
            var zadnjiDan = prviDan.AddMonths(1).AddDays(-1);
            var mesecStr = mesec.ToString("D2");

            // Že fakturirani nalogi (samo od leta 2026 naprej)
            var zeObracunani = db.FaDnNalog.Count(n => n.Leto >= 2026 && n.Fakturirana == 1 && n.Datum >= prviDan && n.Datum <= zadnjiDan);

            // Nalogi za obračun (samo od leta 2026 naprej, brez že fakturiranih)
            // Fakturirana=0: se obračuna, Fakturirana=1: se ignorira, ostalo: se naloži, a ne obračuna
            var nalogi = db.FaDnNalog
                .Where(n => n.Leto >= 2026 && n.Fakturirana != 1 && n.Datum >= prviDan && n.Datum <= zadnjiDan)
                .OrderBy(n => n.Partner).ThenBy(n => n.Leto).ThenBy(n => n.Stevilka)
                .ToList();

            // Ustvari manjkajoče OBRACUN_DN zapise
            var ustvarjenihObracunDn = NalogHelper.UstvariManjkajoceObracunDn(db, nalogi);
            if (ustvarjenihObracunDn > 0)
                log.Add($"Ustvarjenih OBRACUN_DN zapisov: {ustvarjenihObracunDn}");

            // Postavke nalogov
            var nalogiKljuci = nalogi.Select(n => (n.Stevilka, n.Leto)).ToHashSet();
            var postavkeNalogov = db.FaDnNalogPoz.ToList().Where(pn => nalogiKljuci.Contains((pn.Stevilka, pn.Leto))).ToList();

            // Aktivne pogodbe
            var aktivnePogodbe = db.FaPogodbe
                .Where(p => (p.VeljaDo == null || p.VeljaDo >= prviDan) && (p.PrviRacunOd == null || p.PrviRacunOd <= zadnjiDan))
                .ToList();

            // Postavke pogodb
            var postavkePogodb = (
                from poz in db.FaPogodbePoz
                join ap in db.FaPogodbe on new { poz.Stevilka, poz.Leto } equals new { ap.Stevilka, ap.Leto }
                where (ap.VeljaDo == null || ap.VeljaDo >= prviDan) && (ap.PrviRacunOd == null || ap.PrviRacunOd <= zadnjiDan)
                select poz
            ).ToList();

            // Ročne postavke
            var rocnePostavke = db.ObracunOsnutekPos
                .Where(p => p.Mesec == mesec && p.Leto == leto && p.TipPostavke == TipPostavke.ROCNI)
                .ToList();

            // Artikli
            var artikli = db.FaArtikel.ToDictionary(
                a => a.Sifra,
                a => new ArtikelInfo 
                { 
                    Sifra = a.Sifra, 
                    Naziv = a.PolniNaziv, 
                    Enota = a.Enota ?? "", 
                    ProdajnaCena = a.ProdajnaCena 
                }
            );

            // Predračuni
            // 1. Naloži predračune z datumom v tekočem ali preteklem mesecu
            var datumOd = new DateTime(leto, mesec, 1).AddMonths(-1);
            var datumDo = new DateTime(leto, mesec, 1).AddMonths(1).AddDays(-1);
            var vsiPredracuni = db.FaPredracun
                .Where(pr => pr.Datum >= datumOd && pr.Datum <= datumDo)
                .ToList();

            // 2. Naloži vsa plačila za te predračune
            var predracuniLeta = vsiPredracuni.Select(p => p.Leto).Distinct().ToList();
            var sqlPlacila = @"
                SELECT PREDRACUN_STEVILKA, PREDRACUN_LETO, SUM(ZNESEK + COALESCE(SCONTO, 0)) AS VSOTA
                FROM FA_RACUN_PLACILO
                WHERE PREDRACUN_STEVILKA IS NOT NULL 
                  AND PREDRACUN_LETO IS NOT NULL 
                  AND PREDRACUN_LETO IN (" + string.Join(",", predracuniLeta) + @")
                GROUP BY PREDRACUN_STEVILKA, PREDRACUN_LETO
                HAVING SUM(ZNESEK + COALESCE(SCONTO, 0)) > 0";
            
            var placilaPoPredracunih = new Dictionary<(string Stevilka, int Leto), decimal>();
            var cmdPlacila = db.Connection.CreateCommand();
            cmdPlacila.CommandText = sqlPlacila;

            using (var reader = cmdPlacila.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0) && !reader.IsDBNull(1) && !reader.IsDBNull(2))
                    {
                        var prStevilka = reader.GetValue(0).ToString()!.Trim();
                        var prLeto = Convert.ToInt32(reader.GetValue(1));
                        var vsota = Convert.ToDecimal(reader.GetValue(2));
                        placilaPoPredracunih[(prStevilka, prLeto)] = vsota;
                    }
                }
            }

            // 3. Filtriraj predračune: stanje=2 ali stanje=5 ALI plačani (vsota plačil > 0)
            var predracuni = vsiPredracuni
                .Where(pr => pr.Stanje == 2 || pr.Stanje == 5 || placilaPoPredracunih.ContainsKey((pr.Stevilka, pr.Leto)))
                .ToList();

            // 4. DEBUG: Izpiši plačane predračune
            var placaniPredracuni = predracuni
                .Where(pr => placilaPoPredracunih.ContainsKey((pr.Stevilka, pr.Leto)))
                .OrderBy(pr => pr.Leto).ThenBy(pr => pr.Stevilka)
                .ToList();

            // Postavke predračunov
            var predracuniKljuci = predracuni.Select(p => (p.Stevilka, p.Leto)).ToHashSet();
            var postavkePredracunov = db.FaPredracunKnjizba.ToList()
                .Where(pk => predracuniKljuci.Contains((pk.Stevilka, pk.Leto)))
                .ToList();

            // Minute artiklov
            var minuteArtiklov = db.ObracunPaketMinute.ToDictionary(m => m.Artikel, m => m.Minut);


            // OBRACUN_DN slovar
            var obracunDnSlovar = db.ObracunDn.ToList()
                .Where(o => nalogiKljuci.Contains((o.Stevilka, o.Leto)))
                .ToDictionary(o => (o.Stevilka, o.Leto));

            // šifra artikla za kilometrino
            var sifraKilometrina = _parametri.GetString(ObracunParam.SifraKilometrina) ?? "";

            // Servisne nastavitve za obračun
            var servisneNastavitve = new ServisneNastavitve
            {
                // Brez pogodbe
                BrezPogodbeDel7_16 = _parametri.GetString(ObracunParam.ServisnaBrezPogodbeDel7_16) ?? "",
                BrezPogodbeDel16_22 = _parametri.GetString(ObracunParam.ServisnaBrezPogodbeDel16_22) ?? "",
                BrezPogodbeDel22_7 = _parametri.GetString(ObracunParam.ServisnaBrezPogodbeDel22_7) ?? "",
                BrezPogodbeVik7_16 = _parametri.GetString(ObracunParam.ServisnaBrezPogodbeVik7_16) ?? "",
                BrezPogodbeVik16_22 = _parametri.GetString(ObracunParam.ServisnaBrezPogodbeVik16_22) ?? "",
                BrezPogodbeVik22_7 = _parametri.GetString(ObracunParam.ServisnaBrezPogodbeVik22_7) ?? "",
                BrezPogodbeP7_16 = _parametri.GetString(ObracunParam.ServisnaBrezPogodbeP7_16) ?? "",
                BrezPogodbeP16_22 = _parametri.GetString(ObracunParam.ServisnaBrezPogodbeP16_22) ?? "",
                BrezPogodbeP22_7 = _parametri.GetString(ObracunParam.ServisnaBrezPogodbeP22_7) ?? "",
                // S pogodbo (delavnik, vikend in praznik)
                PogodbaDel7_16 = _parametri.GetString(ObracunParam.ServisnaPogodbaDel7_16) ?? "",
                PogodbaDel16_22 = _parametri.GetString(ObracunParam.ServisnaPogodbaDel16_22) ?? "",
                PogodbaDel22_7 = _parametri.GetString(ObracunParam.ServisnaPogodbaDel22_7) ?? "",
                PogodbaVik7_16 = _parametri.GetString(ObracunParam.ServisnaPogodbaVik7_16) ?? "",
                PogodbaVik16_22 = _parametri.GetString(ObracunParam.ServisnaPogodbaVik16_22) ?? "",
                PogodbaVik22_7 = _parametri.GetString(ObracunParam.ServisnaPogodbaVik22_7) ?? "",
                PogodbaP7_16 = _parametri.GetString(ObracunParam.ServisnaPogodbaP7_16) ?? "",
                PogodbaP16_22 = _parametri.GetString(ObracunParam.ServisnaPogodbaP16_22) ?? "",
                PogodbaP22_7 = _parametri.GetString(ObracunParam.ServisnaPogodbaP22_7) ?? ""
            };

            var popustPogodbe = (decimal)_parametri.GetInt(ObracunParam.ProcentPopustaPogodbe);
            if (popustPogodbe > 0)
                log.Add($"Popust pogodbe: {popustPogodbe}%");

            var tolerancaMinut = _parametri.GetInt(ObracunParam.TolerancaMinut);
            if (tolerancaMinut > 0)
                log.Add($"Toleranca minut: {tolerancaMinut}");

            // PARTNER_MINUTE - naloži minute partnerjev, ki so veljavne za tekoči mesec/leto obračuna
            var partnerMinute = db.ObracunMinute
                .Where(m => m.ZacetekMesec != null && m.ZacetekLeto != null && m.VeljavnostMesecih > 0)
                .ToList()
                .Where(m => JeVeljavnaMinuta(m, mesec, leto))
                .ToList();

            // Preberi Že porabljene minute iz OBRACUN_PORABA_MINUT (agregirano po ID_OBRACUN_MINUTE)            // Beremo samo pretekle mesece (mesec/leto strogo manjši od tekočega)
            var zePorabljenePartnerMinute = db.ObracunPorabaMinut
                .Where(p => p.IdObracunMinute != null && p.Tip == TipPorabeMinut.PartnerMinute && (p.Leto < leto || (p.Leto == leto && p.Mesec < mesec)))
                .GroupBy(p => p.IdObracunMinute!.Value)
                .Select(g => new { IdObracunMinute = g.Key, SkupajPorabljeno = g.Sum(x => x.Kolicina) })
                .ToDictionary(x => x.IdObracunMinute, x => x.SkupajPorabljeno);

            // Preberi Že porabljene minute iz predračunov (mesec/leto strogo manjši od tekočega)
            var zePorabljenePredracuni = db.ObracunPorabaMinut
                .Where(p => p.PredracunStevilka != null && p.PredracunLeto != null && p.Tip == TipPorabeMinut.Predracun && (p.Leto < leto || (p.Leto == leto && p.Mesec < mesec)))
                .GroupBy(p => new { p.PredracunStevilka, p.PredracunLeto })
                .Select(g => new { g.Key.PredracunStevilka, g.Key.PredracunLeto, SkupajPorabljeno = g.Sum(x => x.Kolicina) })
                .ToDictionary(x => (x.PredracunStevilka!, x.PredracunLeto!.Value), x => x.SkupajPorabljeno);

            // Partnerji s pogodbo v prihodnosti (nimajo aktivne pogodbe, a imajo pogodbo, ki začne veljati po tekočem mesecu)
            var aktivniPartnerji = aktivnePogodbe.Select(p => p.Partner).Distinct().ToHashSet();
            var partnerjiSPrihodnjoPogodbo = db.FaPogodbe
                .Where(p => p.PrviRacunOd != null && p.PrviRacunOd > zadnjiDan
                    && (p.VeljaDo == null || p.VeljaDo > zadnjiDan))
                .ToList()
                .Where(p => !aktivniPartnerji.Contains(p.Partner))
                .Select(p => p.Partner)
                .Distinct()
                .ToHashSet();

            // Povezave nalog → predračuni (iz OBRACUN_DN_PREDRACUN)
            var nalogPredracunPovezave = db.ObracunDnPredracun
                .ToList()
                .Where(p => nalogiKljuci.Contains((p.Stevilka, p.Leto)))
                .GroupBy(p => (p.Stevilka, p.Leto))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(p => (p.PredracunStevilka, p.PredracunLeto)).ToHashSet());

            return new ObracunContext
            {
                Db = db,
                Mesec = mesec,
                Leto = leto,
                MesecStr = mesecStr,
                Log = log,
                Nalogi = nalogi,
                PostavkeNalogov = postavkeNalogov,
                AktivnePogodbe = aktivnePogodbe,
                PostavkePogodb = postavkePogodb,
                RocnePostavke = rocnePostavke,
                Predracuni = predracuni,
                PostavkePredracunov = postavkePredracunov,
                Artikli = artikli,
                MinuteArtiklov = minuteArtiklov,
                ObracunDnSlovar = obracunDnSlovar,
                PartnerMinute = partnerMinute,
                SifraKilometrina = sifraKilometrina,
                ServisneNastavitve = servisneNastavitve,
                PopustPogodbe = popustPogodbe,
                TolerancaMinut = tolerancaMinut,
                Prazniki = prazniki,
                ZePorabljenePartnerMinute = zePorabljenePartnerMinute,
                ZePorabljenePredracuni = zePorabljenePredracuni,
                PartnerjiSPrihodnjoPogodbo = partnerjiSPrihodnjoPogodbo,
                NalogPredracunPovezave = nalogPredracunPovezave
            };
        }

        #endregion

        #region Priprava tabel

        private static void PripraviTabele(ObracunContext ctx)
        {
            // Izbriši postavke (razen ročnih)
            ctx.Db.ObracunOsnutekPos
                .Where(p => p.Mesec == ctx.Mesec && p.Leto == ctx.Leto && p.TipPostavke != TipPostavke.ROCNI)
                .Delete();

            // Izbriši osnutke
            ctx.Db.ObracunOsnutek
                .Where(o => o.Mesec == ctx.Mesec && o.Leto == ctx.Leto)
                .Delete();

            // Izbriši podrobnosti obračuna nalogov
            ctx.Db.ObracunOsnutekNalogObracun
                .Where(n => n.Mesec == ctx.Mesec && n.Leto == ctx.Leto)
                .Delete();

            // Izbriši porabo minut za ta mesec (da se lahko ponovno izračuna)
            ctx.Db.ObracunPorabaMinut
                .Where(p => p.Mesec == ctx.Mesec && p.Leto == ctx.Leto)
                .Delete();
        }

        private static List<int> ZberiPartnerje(ObracunContext ctx)
        {
            var partnerjiIzNalogov = ctx.Nalogi.Select(n => n.Partner).Distinct();
            var partnerjiIzPogodb = ctx.AktivnePogodbe.Select(p => p.Partner).Distinct();
            var partnerjiIzRocnih = ctx.RocnePostavke.Select(p => p.Partner).Distinct();

            return partnerjiIzNalogov
                .Union(partnerjiIzPogodb)
                .Union(partnerjiIzRocnih)
                .Distinct()
                .OrderBy(p => p)
                .ToList();
        }

        private static PartnerObracunData PripraviPodatkeZaPartnerja(ObracunContext ctx, int partner)
        {
            var maxZsRocni = ctx.RocnePostavke
                .Where(p => p.Partner == partner)
                .Select(p => p.Zs)
                .DefaultIfEmpty(0)
                .Max();

            return new PartnerObracunData
            {
                Partner = partner,
                Pogodbe = ctx.AktivnePogodbe.Where(p => p.Partner == partner).ToList(),
                Nalogi = ctx.Nalogi.Where(n => n.Partner == partner).OrderBy(n => n.Datum).ThenBy(n => n.ZacetekUra).ToList(),
                Predracuni = ctx.Predracuni.Where(pr => pr.SifraKupca == partner).ToList(),
                RocnePostavke = ctx.RocnePostavke.Where(p => p.Partner == partner).ToList(),
                NaslednjZs = maxZsRocni + 1
            };
        }

        #endregion

        #region Obdelava partnerja

        private static PartnerObracunResult ObdelajPartnerja(ObracunContext ctx, PartnerObracunData data, HashSet<string> manjkajoceSifre)
        {
            var opis = new StringBuilder();
            var result = new PartnerObracunResult { Partner = data.Partner };
            var naslednjZs = data.NaslednjZs;

            // Obdelaj predračune - vrne seznam virov z minutami
            var (minutePredracuni, vseMinutePredracuni, zePorabPredracuni, predracunViri) = ObdelajPredracuneZViri(ctx, data, opis);
            result.MinutePredracuni = minutePredracuni;
            result.VseMinutePredracuni = vseMinutePredracuni;
            result.ZePorabljenePredracuni = zePorabPredracuni;

            // Obdelaj ročne postavke
            result.MinuteRocni = ObdelajRocnePostavke(ctx, data, opis);

            // Obdelaj pogodbe
            result.MinutePogodbe = ObdelajPogodbe(ctx, data, opis, ref naslednjZs);
            result.ImaPogodbo = data.Pogodbe.Count > 0 || ctx.PartnerjiSPrihodnjoPogodbo.Contains(data.Partner);
            if (data.Pogodbe.Count == 0 && ctx.PartnerjiSPrihodnjoPogodbo.Contains(data.Partner))
                opis.AppendLine($"--- Partner ima pogodbo v prihodnosti => pogodbena cena ---");
            result.LetnaPogodba = data.Pogodbe.Any(pogodba =>
                ctx.PostavkePogodb
                    .Where(poz => poz.Stevilka == pogodba.Stevilka && poz.Leto == pogodba.Leto)
                    .Any(poz =>
                    {
                        if (string.IsNullOrWhiteSpace(poz.Meseci)) return false;
                        var meseci = poz.Meseci.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(m => m.Trim()).ToList();
                        return meseci.Count == 1 && meseci[0] == ctx.MesecStr;
                    }));

            // Obdelaj PARTNER_MINUTE
            var partnerMinuteViri = ctx.PartnerMinute
                .Where(m => m.Partner == data.Partner)
                .Select(m => {
                    var zePorabljeno = ctx.ZePorabljenePartnerMinute.TryGetValue(m.Id, out var p) ? p : 0;
                    var preostalo = Math.Max(0, (int)m.Minut - zePorabljeno);
                    return new PartnerMinuteVir
                    {
                        IdPartnerMinute = m.Id,
                        ZacetnoStanje = (int)m.Minut,
                        PreostaloMinut = preostalo
                    };
                })
                .Where(v => v.PreostaloMinut > 0)
                .ToList();

            var vsePartnerMinute = ctx.PartnerMinute
                .Where(m => m.Partner == data.Partner)
                .Sum(m => (int)m.Minut);
            var zePorabPartnerMinute = ctx.PartnerMinute
                .Where(m => m.Partner == data.Partner)
                .Sum(m => ctx.ZePorabljenePartnerMinute.TryGetValue(m.Id, out var p) ? p : 0);

            result.MinutePartnerMinute = partnerMinuteViri.Sum(v => v.PreostaloMinut);
            result.VseMinutePartnerMinute = vsePartnerMinute;
            result.ZePorabljenePartnerMinute = zePorabPartnerMinute;
            if (result.MinutePartnerMinute > 0)
            {
                opis.AppendLine();
                opis.AppendLine($"--- Partner minute: {result.MinutePartnerMinute} (preostalo po odštevanju preteklih porab) ---");
            }

            // Ustvari sklad minut z viri
            var sklad = new MinutniSklad
            {
                PartnerMinuteViri = partnerMinuteViri,
                PredracunViri = predracunViri
            };

            // Obdelaj naloge (in kilometrino)
            var (imaNaloge, minuteNalogov, minuteObracunane, minuteNeobracunane, minuteKoriscene) = ObdelajNaloge(
                ctx, data, opis, ref naslednjZs, manjkajoceSifre, 
                sklad, result.MinuteRocni, result.MinutePogodbe);
            result.ImaNaloge = imaNaloge;
            result.MinuteNalogov = minuteNalogov;
            result.MinuteObracunane = minuteObracunane;
            result.MinuteNeobracunane = minuteNeobracunane;
            result.MinuteKoriscene = minuteKoriscene;

            // Shrani porabo minut v OBRACUN_PORABA_MINUT
            ShraniPoraboMinut(ctx, data.Partner, sklad);

            // Izpiši skupne minute
            if (result.MinuteVPlus > 0)
            {
                opis.AppendLine("------------------------------------");
                opis.AppendLine($"=== MinuteVPlus: {result.MinuteVPlus} (predračuni: {result.MinutePredracuni}, ročni: {result.MinuteRocni}, pogodbe: {result.MinutePogodbe}, partner_minute: {result.MinutePartnerMinute}) ===");
                opis.AppendLine($"=== MinuteKoriscene: {result.MinuteKoriscene} ===");
                opis.AppendLine("------------------------------------");
            }

            result.Opis = opis.ToString();
            return result;
        }

        /// <summary>
        /// Obdelaj predračune in vrne seznam virov z minutami (z upoštevanjem Že porabljenih).
        /// </summary>
        private static (int SkupnoMinut, int VseMinut, int ZePorabljeno, List<PredracunVir> Viri) ObdelajPredracuneZViri(ObracunContext ctx, PartnerObracunData data, StringBuilder opis)
        {
            int skupnoMinut = 0;
            int vseMinut = 0;
            int zePorabljeno = 0;
            var viri = new List<PredracunVir>();
            bool imaIzpis = false;

            foreach (var predracun in data.Predracuni)
            {
                var postavke = ctx.PostavkePredracunov
                    .Where(pk => pk.Stevilka == predracun.Stevilka && pk.Leto == predracun.Leto)
                    .ToList();

                int minutePredracuna = 0;
                var postavkeZaIzpis = new List<(string sifraStr, string nazivStr, string enotaStr, string kolicinaStr, string minutStr)>();

                foreach (var postavka in postavke)
                {
                    if (!string.IsNullOrEmpty(postavka.Sifra) && ctx.MinuteArtiklov.TryGetValue(postavka.Sifra, out var minutNaArtikel))
                    {
                        var kolicina = (int)(postavka.Kolicina ?? 0);
                        var minutPostavke = kolicina * minutNaArtikel;
                        minutePredracuna += minutPostavke;

                        ctx.Artikli.TryGetValue(postavka.Sifra, out var artikel);
                        var naziv = artikel?.Naziv ?? "";
                        var enota = artikel?.Enota ?? "";

                        postavkeZaIzpis.Add((
                            postavka.Sifra.PadRight(12),
                            naziv.Length > 25 ? naziv.Substring(0, 25) : naziv.PadRight(25),
                            enota.Length > 6 ? enota.Substring(0, 6) : enota.PadRight(6),
                            kolicina.ToString().PadLeft(5),
                            minutPostavke.ToString().PadLeft(6)
                        ));
                    }
                }

                if (minutePredracuna > 0)
                {
                    // Odštej Že porabljeno
                    var zePor = ctx.ZePorabljenePredracuni.TryGetValue((predracun.Stevilka, predracun.Leto), out var p) ? p : 0;
                    var preostalo = Math.Max(0, minutePredracuna - zePor);

                    vseMinut += minutePredracuna;
                    zePorabljeno += zePor;

                    if (!imaIzpis)
                    {
                        opis.AppendLine();
                        opis.AppendLine("--- Predračuni ---");
                        imaIzpis = true;
                    }

                    var datumStr = predracun.Datum?.ToString("dd.MM.yyyy") ?? "";
                    var zePorabStr = zePor > 0 ? $", Že porabljeno: {zePor}" : "";
                    opis.AppendLine($"Predračun {predracun.Stevilka}/{predracun.Leto}, datum: {datumStr}, minut: {minutePredracuna}{zePorabStr}, preostalo: {preostalo}");

                    foreach (var poz in postavkeZaIzpis)
                        opis.AppendLine($"   - {poz.sifraStr} {poz.nazivStr} {poz.enotaStr} kol: {poz.kolicinaStr}  min: {poz.minutStr}");

                    if (preostalo > 0)
                    {
                        viri.Add(new PredracunVir
                        {
                            PredracunStevilka = predracun.Stevilka,
                            PredracunLeto = predracun.Leto,
                            ZacetnoStanje = minutePredracuna,
                            PreostaloMinut = preostalo
                        });
                        skupnoMinut += preostalo;
                    }
                }
            }

            return (skupnoMinut, vseMinut, zePorabljeno, viri);
        }

        /// <summary>
        /// Shrani porabo minut v OBRACUN_PORABA_MINUT (razlika med začetnim stanjem in preostalim).
        /// </summary>
        private static void ShraniPoraboMinut(ObracunContext ctx, int partner, MinutniSklad sklad)
        {
            // Shrani porabo partner_minute
            // Poraba tega meseca = (preostalo_pred_obračunom) - (preostalo_po_obračunu)
            foreach (var vir in sklad.PartnerMinuteViri)
            {
                var zePorabljeno = ctx.ZePorabljenePartnerMinute.TryGetValue(vir.IdPartnerMinute, out var p) ? p : 0;
                var preostaloNaZacetku = vir.ZacetnoStanje - zePorabljeno;
                var porabaTegaMeseca = preostaloNaZacetku - vir.PreostaloMinut;

                if (porabaTegaMeseca > 0)
                {
                    ctx.Db.Insert(new ObracunPorabaMinut
                    {
                        Mesec = ctx.Mesec,
                        Leto = ctx.Leto,
                        Partner = partner,
                        Tip = TipPorabeMinut.PartnerMinute,
                        IdObracunMinute = vir.IdPartnerMinute,
                        Kolicina = porabaTegaMeseca
                    });
                }
            }

            // Shrani porabo predračunov
            foreach (var vir in sklad.PredracunViri)
            {
                var zePorabljeno = ctx.ZePorabljenePredracuni.TryGetValue((vir.PredracunStevilka, vir.PredracunLeto), out var p) ? p : 0;
                var preostaloNaZacetku = vir.ZacetnoStanje - zePorabljeno;
                var porabaTegaMeseca = preostaloNaZacetku - vir.PreostaloMinut;

                if (porabaTegaMeseca > 0)
                {
                    ctx.Db.Insert(new ObracunPorabaMinut
                    {
                        Mesec = ctx.Mesec,
                        Leto = ctx.Leto,
                        Partner = partner,
                        Tip = TipPorabeMinut.Predracun,
                        PredracunStevilka = vir.PredracunStevilka,
                        PredracunLeto = vir.PredracunLeto,
                        Kolicina = porabaTegaMeseca
                    });
                }
            }
        }

        private static int ObdelajPredracune(ObracunContext ctx, PartnerObracunData data, StringBuilder opis)
        {
            var (skupnoMinut, _, _, _) = ObdelajPredracuneZViri(ctx, data, opis);
            return skupnoMinut;
        }

        private static int ObdelajRocnePostavke(ObracunContext ctx, PartnerObracunData data, StringBuilder opis)
        {
            int skupnoMinut = 0;
            bool imaIzpis = false;

            foreach (var postavka in data.RocnePostavke)
            {
                if (!string.IsNullOrEmpty(postavka.Artikel) && ctx.MinuteArtiklov.TryGetValue(postavka.Artikel, out var minutNaArtikel))
                {
                    var kolicina = (int)(postavka.Kolicina ?? 0);
                    var minutPostavke = kolicina * minutNaArtikel;

                    if (!imaIzpis)
                    {
                        opis.AppendLine("--- Nalogi(r) ---");
                        imaIzpis = true;
                    }

                    skupnoMinut += minutPostavke;
                    opis.AppendLine($"   - {postavka.Artikel.PadRight(12)} kol: {kolicina.ToString().PadLeft(5)}  min: {minutPostavke.ToString().PadLeft(6)}");
                }
            }

            return skupnoMinut;
        }

        private static int ObdelajPogodbe(ObracunContext ctx, PartnerObracunData data, StringBuilder opis, ref int naslednjZs)
        {
            int vkljucenihMinut = 0;

            foreach (var pogodba in data.Pogodbe)
            {
                vkljucenihMinut += pogodba.StMinut ?? 0;

                // Izpis glave pogodbe
                var oznakaTip = pogodba.SifNaprejNazaj == 6 ? "-T" : "";
                var stPogodbe = $"{pogodba.Stevilka}/{pogodba.Leto}{oznakaTip}".PadRight(14);
                var veljavnostOd = pogodba.PrviRacunOd?.ToString("dd.MM.yyyy") ?? "";
                var veljavnostDo = pogodba.VeljaDo?.ToString("dd.MM.yyyy") ?? "";
                var minut = (pogodba.StMinut ?? 0).ToString().PadLeft(3);
                opis.AppendLine($"Pogodba {stPogodbe}, veljavnost od {veljavnostOd}:{veljavnostDo}, minut {minut}");

                var postavkePogodbe = ctx.PostavkePogodb
                    .Where(poz => poz.Stevilka == pogodba.Stevilka && poz.Leto == pogodba.Leto)
                    .ToList();

                if (postavkePogodbe.Count == 0)
                    opis.AppendLine($"      Opomba: {pogodba.Opomba}");

                // Opozorila za mesečne pogodbe z omejenimi meseci
                if (pogodba.NaKolikoMesecev == 1)
                {
                    foreach (var postavka in postavkePogodbe.Where(p => !string.IsNullOrWhiteSpace(p.Meseci)))
                    {
                        var stMesecev = postavka.Meseci!.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
                        if (stMesecev > 1 && stMesecev < 12)
                        {
                            ctx.Log.Add($"OPOZORILO: Pogodba {pogodba.Stevilka}/{pogodba.Leto} ima NaKolikoMesecev=1, vendar postavka (artikel: {postavka.Sifra}) ima omejene mesece: {postavka.Meseci}");
                        }
                    }
                }

                // Obdelaj postavke pogodbe
                foreach (var postavka in postavkePogodbe)
                {
                    ObdelajPostavkoPogodbe(ctx, pogodba, postavka, opis, ref naslednjZs, data.Partner);
                }
            }

            return vkljucenihMinut;
        }

        private static void ObdelajPostavkoPogodbe(ObracunContext ctx, FaPogodbe pogodba, FaPogodbePoz postavka, StringBuilder opis, ref int naslednjZs, int partner)
        {
            var jeVeljavna = JePostavkaVeljavnaZaMesec(postavka.Meseci, ctx.MesecStr);
            string? razlogNeveljavnosti = null;

            if (!jeVeljavna)
                razlogNeveljavnosti = $"[NE UPOŠTEVA SE - mesec {ctx.MesecStr} ni v {postavka.Meseci}]";

            //Za januar 2026
            if (ctx.Mesec == 1 && ctx.Leto == 2026)
            {
                jeVeljavna = false;
                razlogNeveljavnosti = $"[NE UPOŠTEVA SE - mesec 1, leto 2026]";
            }

            var sifra = postavka.Sifra ?? "";
            string naziv;
            string enota;
            if (sifra == "-")
            {
                naziv = postavka.Naziv ?? "";
                enota = "";
            }
            else
            {
                ctx.Artikli.TryGetValue(sifra, out var artikel);
                naziv = artikel?.Naziv ?? "";
                enota = artikel?.Enota ?? "";
            }
            var cena = postavka.ProdajnaCena ?? 0;
            var kolicina = postavka.Kolicina ?? 0;
            var rabat = postavka.Rabat1 ?? 0;
            var vrednost = kolicina * cena * (1 - rabat / 100);

            var sifraStr = sifra.Length > 12 ? sifra.Substring(0, 12) : sifra.PadRight(12);
            var nazivStr = naziv.Length > 40 ? naziv.Substring(0, 40) : naziv.PadRight(40);
            var enotaStr = enota.Length > 10 ? enota.Substring(0, 10) : enota.PadRight(10);
            var kolicinaStr = kolicina.ToString("N1").PadLeft(6);
            var cenaStr = cena.ToString("N2").PadLeft(9);
            var rabatStr = rabat.ToString("N1").PadLeft(5);
            var vrednostStr = vrednost.ToString("N2").PadLeft(10);

            if (jeVeljavna)
            {
                opis.AppendLine($"   - {sifraStr} {nazivStr} {enotaStr} {kolicinaStr} {cenaStr} {rabatStr} {vrednostStr}");

                ctx.Db.Insert(new ObracunOsnutekPos
                {
                    Mesec = ctx.Mesec,
                    Leto = ctx.Leto,
                    Partner = partner,
                    Zs = naslednjZs++,
                    Artikel = sifra,
                    Naziv = naziv,
                    Kolicina = kolicina,
                    Cena = cena,
                    Rabat = rabat,
                    TipPostavke = TipPostavke.POGODBA
                });
            }
            else
            {
                opis.AppendLine($"   {razlogNeveljavnosti}");
                opis.AppendLine($"   - {sifraStr} {nazivStr} {enotaStr} {kolicinaStr} {cenaStr} {rabatStr} {vrednostStr}");
            }
        }

        private static (bool ImaNaloge, MinuteRazdelitev Razdelitev, int MinuteObracunane, int MinuteNeobracunane, int MinuteKoriscene) ObdelajNaloge(
            ObracunContext ctx, 
            PartnerObracunData data, 
            StringBuilder opis, 
            ref int naslednjZs, 
            HashSet<string> manjkajoceSifre,
            MinutniSklad sklad,
            int minuteRocni,
            int minutePogodbe)
        {
            var skupnaRazdelitev = new MinuteRazdelitev();
            var obracunaneRazdelitev = new MinuteRazdelitev(); // Samo minute ki se obračunajo
            var obveznoRazdelitev = new MinuteRazdelitev(); // Minute iz nalogov z ObveznoZaracunaj (ne odštevaj dobroimetja)
            int minuteObracunane = 0; // Bruto minute, ki se naj bi obračunale (pred odštetjem dobroimetja)
            int minuteNeobracunane = 0;
            int minuteKoriscene = 0; // Dobroimetje, ki se odšteje (pogodbe, predračuni, ročno, partner_minute)
            bool imaPogodbo = data.Pogodbe.Count > 0 || ctx.PartnerjiSPrihodnjoPogodbo.Contains(data.Partner);
            bool imaHelpdeskNalogeZaObracun = false; // Ali ima partner helpdesk naloge ki se obračunajo (za uporabiPogodbo)

            // Izračunaj skupne minute v plus (predračuni, ročni, pogodbe, partner_minute)
            // minutePogodbe se doda pogojno po obdelavi nalogov (samo za helpdesk naloge)
            var minuteVPlus = sklad.PredracunPreostalo + minuteRocni + minutePogodbe + sklad.PartnerMinutePreostalo;

            if (data.Nalogi.Count == 0)
                return (false, skupnaRazdelitev, 0, 0, 0);

            opis.AppendLine();
            opis.AppendLine($"=== Nalogi: {data.Nalogi.Count} ===");

            // Nastavi ročno in pogodbe v sklad
            sklad.RocnoPreostalo = minuteRocni;
            sklad.PogodbaPreostalo = minutePogodbe;

            foreach (var nalog in data.Nalogi)
            {

                // Izračunaj trajanje: za naloge 1000000-1999999 uporabi količino artikla 047512,
                // sicer izračunaj iz razlike KonecUra - ZacetekUra
                int trajanje;
                if (int.TryParse(nalog.Stevilka, out var stevilkaInt) && stevilkaInt >= 1000000 && stevilkaInt <= 1999999)
                {
                    var postavka047512 = ctx.PostavkeNalogov
                        .FirstOrDefault(p => p.Stevilka == nalog.Stevilka && p.Leto == nalog.Leto && p.Sifra == "047512");
                    if (postavka047512 != null)
                    {
                        trajanje = (int)postavka047512.Kolicina;
                    }
                    else
                    {
                        var razlika = (nalog.KonecUra - nalog.ZacetekUra).TotalMinutes;
                        if (razlika < 0) razlika += 24 * 60;
                        trajanje = (int)razlika;
                    }
                }
                else
                {
                    var razlika = (nalog.KonecUra - nalog.ZacetekUra).TotalMinutes;
                    if (razlika < 0) razlika += 24 * 60;
                    trajanje = (int)razlika;
                }

                var zacetekStr = nalog.ZacetekUra.ToString("HH:mm");
                nalog.KonecUra = nalog.ZacetekUra.AddMinutes(trajanje);
                var konecStr = nalog.KonecUra.ToString("HH:mm");

                // Izračunaj razdelitev minut za ta nalog
                var razdelitevNaloga = MinuteCalculator.IzracunajRazdelitev(
                    nalog.Datum, nalog.ZacetekUra, nalog.KonecUra, ctx.Prazniki);
                skupnaRazdelitev.Pristej(razdelitevNaloga);

                // Pridobi nastavitve obračuna za ta nalog
                ctx.ObracunDnSlovar.TryGetValue((nalog.Stevilka, nalog.Leto), out var obracunDn);

                // Ločeno štej minute glede na KajObracunam
                var minutNaloga = razdelitevNaloga.SkupajMinut;
                var seObracunajoMinute = nalog.Fakturirana != 1 
                    && obracunDn != null 
                    && (obracunDn.KajObracunam == KajObracunam.KmMin || obracunDn.KajObracunam == KajObracunam.Min || obracunDn.KajObracunam == KajObracunam.ObveznoZaracunaj);
                var jeObveznoZaracunaj = seObracunajoMinute && obracunDn!.KajObracunam == KajObracunam.ObveznoZaracunaj;
                if (seObracunajoMinute)
                {
                    minuteObracunane += minutNaloga;
                    obracunaneRazdelitev.Pristej(razdelitevNaloga);
                    if (jeObveznoZaracunaj)
                        obveznoRazdelitev.Pristej(razdelitevNaloga);

                    // Preveri ali je helpdesk nalog (za konsistentno uporabo minutePogodbe)
                    var jeHelpdesk = nalog.Stevilka.Length == 7 && nalog.Stevilka.StartsWith("1");
                    if (jeHelpdesk)
                        imaHelpdeskNalogeZaObracun = true;
                }
                else
                {
                    minuteNeobracunane += minutNaloga;
                }

                if (data.Partner == 314910)
                    ctx.Log.Add($"[DEBUG MIN] Nalog {nalog.Stevilka}/{nalog.Leto}: trajanje={trajanje} min, razdelitev={razdelitevNaloga}, seObracunajo={seObracunajoMinute}, Fakturirana={nalog.Fakturirana}, ObracunDn={(obracunDn != null ? obracunDn.KajObracunam.ToText() : "NULL")}, obvezno={jeObveznoZaracunaj}");

                var obracunDnInfo = "";
                if (nalog.Fakturirana != 0)
                    obracunDnInfo += $", [FAKT={nalog.Fakturirana}]";
                if (obracunDn != null)
                {
                    obracunDnInfo = $", {obracunDn.KajObracunam.ToText()}";
                    if (obracunDn.MinuteKiSeNeObracunajo > 0)
                        obracunDnInfo += $", ne obr: {obracunDn.MinuteKiSeNeObracunajo} min";
                }

                // Izpiši nalog z razdelitvijo minut
                var tipDneva = MinuteCalculator.DolocitTipDneva(nalog.Datum, ctx.Prazniki);
                var tipDnevaStr = tipDneva switch { TipDneva.Vikend => " [VIK]", TipDneva.Praznik => " [PRA]", _ => "" };
                opis.AppendLine($"Nalog {nalog.Stevilka}/{nalog.Leto}, {nalog.Datum:dd.MM.yyyy}{tipDnevaStr} {zacetekStr}-{konecStr}, trajanje: {trajanje} min ({razdelitevNaloga}){obracunDnInfo}");

                // Izpiši postavke naloga
                var postavkeNaloga = ctx.PostavkeNalogov
                    .Where(pn => pn.Stevilka == nalog.Stevilka && pn.Leto == nalog.Leto)
                    .OrderBy(pn => pn.Zs)
                    .ToList();

                foreach (var postavka in postavkeNaloga)
                {
                    var sifra = postavka.Sifra ?? "";
                    ctx.Artikli.TryGetValue(sifra, out var artikel);
                    var naziv = artikel?.Naziv ?? "";
                    var enota = artikel?.Enota ?? "";

                    var kolicina = postavka.Kolicina;
                    var cena = postavka.Cena;
                    var rabat = postavka.Rabat1;
                    var vrednost = kolicina * cena * (1 - rabat / 100);

                    var sifraStr = sifra.Length > 12 ? sifra.Substring(0, 12) : sifra.PadRight(12);
                    var nazivStr = naziv.Length > 40 ? naziv.Substring(0, 40) : naziv.PadRight(40);
                    var enotaStr = enota.Length > 10 ? enota.Substring(0, 10) : enota.PadRight(10);
                    var kolicinaStr = kolicina.ToString("N1").PadLeft(6);
                    var cenaStr = cena.ToString("N2").PadLeft(9);
                    var rabatStr = rabat.ToString("N1").PadLeft(5);
                    var vrednostStr = vrednost.ToString("N2").PadLeft(10);

                    opis.AppendLine($"   - {sifraStr} {nazivStr} {enotaStr} {kolicinaStr} {cenaStr} {rabatStr} {vrednostStr}");

                    // Zapiši postavko v OBRACUN_OSNUTEK_POS (razen šifre 047512 in nalogov z Fakturirana<>0)
                    if (sifra != "047512" && nalog.Fakturirana != 1)
                    {
                        ctx.Db.Insert(new ObracunOsnutekPos
                        {
                            Mesec = ctx.Mesec,
                            Leto = ctx.Leto,
                            Partner = data.Partner,
                            Zs = naslednjZs++,
                            Artikel = sifra,
                            Naziv = naziv,
                            Kolicina = kolicina,
                            Cena = cena,
                            Rabat = rabat,
                            NalogStevilka = nalog.Stevilka,
                            NalogLeto = nalog.Leto,
                            TipPostavke = TipPostavke.NALOG
                        });
                    }
                }

                // === Zapiši vse časovne intervale v OBRACUN_OSNUTEK_NALOG_OBRACUN ===
                // ObveznoZaracunaj: ne odštevaj iz sklada (null)
                // Poišči povezane predračune za ta nalog
                ctx.NalogPredracunPovezave.TryGetValue((nalog.Stevilka, nalog.Leto), out var povezaniPredracuniNaloga);
                ZapisiNalogObracun(ctx, nalog, razdelitevNaloga, seObracunajoMinute ? 1 : 0, imaPogodbo, jeObveznoZaracunaj ? null : sklad, povezaniPredracuniNaloga, trajanje);

                // === Obdelaj kilometrino (samo za naloge z Fakturirana=0) ===
                if (nalog.Fakturirana != 1)
                    ObdelajKilometrino(ctx, nalog, obracunDn, opis, ref naslednjZs, data.Partner, manjkajoceSifre);
                else if (data.Partner == 314910)
                    ctx.Log.Add($"[DEBUG KM] Partner {data.Partner}, Nalog {nalog.Stevilka}/{nalog.Leto}: PRESKOČEN - Fakturirana={nalog.Fakturirana} (!=0)");
            } // po nalogih

            // Izpiši skupno razdelitev minut za partnerja
            if (skupnaRazdelitev.SkupajMinut > 0)
            {
                opis.AppendLine();
                opis.AppendLine($"--- Skupaj minute nalogov: {skupnaRazdelitev.SkupajMinut} ---");
                opis.AppendLine($"    Obračunane: {minuteObracunane} min, Neobračunane: {minuteNeobracunane} min");
                opis.AppendLine($"    {skupnaRazdelitev}");
            }

            // Če partner nima helpdesk nalogov za obračun, izključi minutePogodbe iz dobroimetja
            // (konsistentno z OdstejiMinuteIzSklada, ki za terenske naloge nastavi uporabiPogodbo=false)
            if (!imaHelpdeskNalogeZaObracun)
            {
                minuteVPlus -= minutePogodbe;
                sklad.PogodbaPreostalo = 0;
            }

            if (data.Partner == 314910)
            {
                ctx.Log.Add($"[DEBUG MIN] === SKUPAJ: obračunane={minuteObracunane}, neobračunane={minuteNeobracunane}, imaPogodbo={imaPogodbo}, imaHelpdesk={imaHelpdeskNalogeZaObracun}");
                ctx.Log.Add($"[DEBUG MIN] === DOBROIMETJE: minuteVPlus={minuteVPlus} (predračun={sklad.PredracunPreostalo}, ročni={minuteRocni}, pogodbe={minutePogodbe} (vključene={imaHelpdeskNalogeZaObracun}), partnerMinute={sklad.PartnerMinutePreostalo})");
                ctx.Log.Add($"[DEBUG MIN] === RAZDELITEV obračunane: {obracunaneRazdelitev}");
                ctx.Log.Add($"[DEBUG MIN] === RAZDELITEV obvezno: {obveznoRazdelitev}");
            }

            // === Za partnerje BREZ POGODBE ustvari postavke za obračunane minute ===
            if (!imaPogodbo && minuteObracunane > 0)
            {
                opis.AppendLine();
                opis.AppendLine("--- Obračun servisnih storitev (brez pogodbe) ---");

                if (data.Partner != 23900)
                {
                    // Odštej dobroimetje (predračuni, ročni, pogodbe) od delavniškiih minut
                    // ObveznoZaracunaj minute se ne odštevajo - izloči jih pred odštetjem
                    var preostaleMinuteVPlus = minuteVPlus;

                    if (data.Partner == 314910)
                        ctx.Log.Add($"[DEBUG MIN] ODŠTEVANJE DOBROIMETJA (brez pogodbe): minuteVPlus={minuteVPlus}, predračun={sklad.PredracunPreostalo}, ročni={minuteRocni}, pogodbe={minutePogodbe}, partnerMinute={sklad.PartnerMinutePreostalo}");

                    // Delavnik - dnevna (7-16)
                    var delavnikDnevnaZaObracun = obracunaneRazdelitev.Delavnik_Dnevna - obveznoRazdelitev.Delavnik_Dnevna;
                    if (preostaleMinuteVPlus > 0 && delavnikDnevnaZaObracun > 0)
                    {
                        var odsteto = Math.Min(delavnikDnevnaZaObracun, preostaleMinuteVPlus);
                        if (data.Partner == 314910)
                            ctx.Log.Add($"[DEBUG MIN]   Del7-16: obračunane={obracunaneRazdelitev.Delavnik_Dnevna}, obvezno={obveznoRazdelitev.Delavnik_Dnevna}, zaObračun={delavnikDnevnaZaObracun} → odšteto={odsteto}, preostalo={preostaleMinuteVPlus - odsteto}");
                        delavnikDnevnaZaObracun -= odsteto;
                        preostaleMinuteVPlus -= odsteto;
                        minuteKoriscene += odsteto;
                    }
                    else if (data.Partner == 314910)
                        ctx.Log.Add($"[DEBUG MIN]   Del7-16: obračunane={obracunaneRazdelitev.Delavnik_Dnevna}, obvezno={obveznoRazdelitev.Delavnik_Dnevna}, zaObračun={delavnikDnevnaZaObracun} → nič za odšteti (vPlus={preostaleMinuteVPlus})");

                    // Delavnik - popoldanska (16-22)
                    var delavnikPopoldanskaZaObracun = obracunaneRazdelitev.Delavnik_Popoldanska - obveznoRazdelitev.Delavnik_Popoldanska;
                    if (preostaleMinuteVPlus > 0 && delavnikPopoldanskaZaObracun > 0)
                    {
                        var odsteto = Math.Min(delavnikPopoldanskaZaObracun, preostaleMinuteVPlus);
                        if (data.Partner == 314910)
                            ctx.Log.Add($"[DEBUG MIN]   Del16-22: obračunane={obracunaneRazdelitev.Delavnik_Popoldanska}, obvezno={obveznoRazdelitev.Delavnik_Popoldanska}, zaObračun={delavnikPopoldanskaZaObracun} → odšteto={odsteto}, preostalo={preostaleMinuteVPlus - odsteto}");
                        delavnikPopoldanskaZaObracun -= odsteto;
                        preostaleMinuteVPlus -= odsteto;
                        minuteKoriscene += odsteto;
                    }
                    else if (data.Partner == 314910)
                        ctx.Log.Add($"[DEBUG MIN]   Del16-22: obračunane={obracunaneRazdelitev.Delavnik_Popoldanska}, obvezno={obveznoRazdelitev.Delavnik_Popoldanska}, zaObračun={delavnikPopoldanskaZaObracun} → nič za odšteti (vPlus={preostaleMinuteVPlus})");

                    // Delavnik - nočna (22-7)
                    var delavnikNocnaZaObracun = obracunaneRazdelitev.Delavnik_Nocna - obveznoRazdelitev.Delavnik_Nocna;
                    if (preostaleMinuteVPlus > 0 && delavnikNocnaZaObracun > 0)
                    {
                        var odsteto = Math.Min(delavnikNocnaZaObracun, preostaleMinuteVPlus);
                        if (data.Partner == 314910)
                            ctx.Log.Add($"[DEBUG MIN]   Del22-7: obračunane={obracunaneRazdelitev.Delavnik_Nocna}, obvezno={obveznoRazdelitev.Delavnik_Nocna}, zaObračun={delavnikNocnaZaObracun} → odšteto={odsteto}, preostalo={preostaleMinuteVPlus - odsteto}");
                        delavnikNocnaZaObracun -= odsteto;
                        preostaleMinuteVPlus -= odsteto;
                        minuteKoriscene += odsteto;
                    }
                    else if (data.Partner == 314910)
                        ctx.Log.Add($"[DEBUG MIN]   Del22-7: obračunane={obracunaneRazdelitev.Delavnik_Nocna}, obvezno={obveznoRazdelitev.Delavnik_Nocna}, zaObračun={delavnikNocnaZaObracun} → nič za odšteti (vPlus={preostaleMinuteVPlus})");

                    // Prištej obvezne minute nazaj (ObveznoZaracunaj - ne gredo skozi dobroimetje)
                    delavnikDnevnaZaObracun += obveznoRazdelitev.Delavnik_Dnevna;
                    delavnikPopoldanskaZaObracun += obveznoRazdelitev.Delavnik_Popoldanska;
                    delavnikNocnaZaObracun += obveznoRazdelitev.Delavnik_Nocna;

                    // Delavnik (z odštetim dobroimetjem)
                    // Vikend in Praznik (brez odštevanja dobroimetja)
                    // Uporabi toleranco minut
                    var (tDel7_16, tDel16_22, tDel22_7, tVik7_16, tVik16_22, tVik22_7, tPra7_16, tPra16_22, tPra22_7) = 
                        UpostevajiToleranco(ctx.TolerancaMinut,
                            delavnikDnevnaZaObracun, delavnikPopoldanskaZaObracun, delavnikNocnaZaObracun,
                            obracunaneRazdelitev.Vikend_Dnevna, obracunaneRazdelitev.Vikend_Popoldanska, obracunaneRazdelitev.Vikend_Nocna,
                            obracunaneRazdelitev.Praznik_Dnevna, obracunaneRazdelitev.Praznik_Popoldanska, obracunaneRazdelitev.Praznik_Nocna,
                            opis, data.Partner, ctx.Log);

                    if (data.Partner == 314910)
                    {
                        ctx.Log.Add($"[DEBUG MIN] BREZ POGODBE po toleranci ({ctx.TolerancaMinut} min): Del7-16={tDel7_16}, Del16-22={tDel16_22}, Del22-7={tDel22_7}, Vik7-16={tVik7_16}, Vik16-22={tVik16_22}, Vik22-7={tVik22_7}, Pra7-16={tPra7_16}, Pra16-22={tPra16_22}, Pra22-7={tPra22_7}");
                        ctx.Log.Add($"[DEBUG MIN] ŠIFRE brez pogodbe: Del7-16={ctx.ServisneNastavitve.GetSifraBrezPogodbe(TipDneva.Delavnik, CasovnaTarifa.Dnevna)}, Del16-22={ctx.ServisneNastavitve.GetSifraBrezPogodbe(TipDneva.Delavnik, CasovnaTarifa.Popoldanska)}, Del22-7={ctx.ServisneNastavitve.GetSifraBrezPogodbe(TipDneva.Delavnik, CasovnaTarifa.Nocna)}");
                        ctx.Log.Add($"[DEBUG MIN] KORIŠČENE minute dobroimetja: {minuteKoriscene}, preostaleVPlus={preostaleMinuteVPlus}");
                    }

                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Dnevna, tDel7_16, opis, ref naslednjZs, manjkajoceSifre);
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Popoldanska, tDel16_22, opis, ref naslednjZs, manjkajoceSifre);
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Nocna, tDel22_7, opis, ref naslednjZs, manjkajoceSifre);

                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Dnevna, tVik7_16, opis, ref naslednjZs, manjkajoceSifre);
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Popoldanska, tVik16_22, opis, ref naslednjZs, manjkajoceSifre);
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Nocna, tVik22_7, opis, ref naslednjZs, manjkajoceSifre);

                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Dnevna, tPra7_16, opis, ref naslednjZs, manjkajoceSifre);
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Popoldanska, tPra16_22, opis, ref naslednjZs, manjkajoceSifre);
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Nocna, tPra22_7, opis, ref naslednjZs, manjkajoceSifre);
                }
            }

            // === Za partnerje S POGODBO ustvari postavke za vse minute (delavnik, vikend, praznik) ===
            if (imaPogodbo && minuteObracunane > 0)
            {
                opis.AppendLine();
                opis.AppendLine($"--- Obračun servisnih storitev (pogodba, popust {ctx.PopustPogodbe}%) ---");

                // Odštej dobroimetje (predračuni, ročni, pogodbe) od delavniškiih minut
                // ObveznoZaracunaj minute se ne odštevajo - izloči jih pred odštetjem
                var preostaleMinuteVPlus = minuteVPlus;

                if (data.Partner == 314910)
                    ctx.Log.Add($"[DEBUG MIN] ODŠTEVANJE DOBROIMETJA (s pogodbo): minuteVPlus={minuteVPlus}, predračun={sklad.PredracunPreostalo}, ročni={minuteRocni}, pogodbe={minutePogodbe}, partnerMinute={sklad.PartnerMinutePreostalo}");

                // Delavnik - dnevna (7-16)
                var delavnikDnevnaZaObracun = obracunaneRazdelitev.Delavnik_Dnevna - obveznoRazdelitev.Delavnik_Dnevna;
                if (preostaleMinuteVPlus > 0 && delavnikDnevnaZaObracun > 0)
                {
                    var odsteto = Math.Min(delavnikDnevnaZaObracun, preostaleMinuteVPlus);
                    if (data.Partner == 314910)
                        ctx.Log.Add($"[DEBUG MIN]   Del7-16: obračunane={obracunaneRazdelitev.Delavnik_Dnevna}, obvezno={obveznoRazdelitev.Delavnik_Dnevna}, zaObračun={delavnikDnevnaZaObracun} → odšteto={odsteto}, preostalo={preostaleMinuteVPlus - odsteto}");
                    delavnikDnevnaZaObracun -= odsteto;
                    preostaleMinuteVPlus -= odsteto;
                    minuteKoriscene += odsteto;
                }
                else if (data.Partner == 314910)
                    ctx.Log.Add($"[DEBUG MIN]   Del7-16: obračunane={obracunaneRazdelitev.Delavnik_Dnevna}, obvezno={obveznoRazdelitev.Delavnik_Dnevna}, zaObračun={delavnikDnevnaZaObracun} → nič za odšteti (vPlus={preostaleMinuteVPlus})");

                // Delavnik - popoldanska (16-22)
                var delavnikPopoldanskaZaObracun = obracunaneRazdelitev.Delavnik_Popoldanska - obveznoRazdelitev.Delavnik_Popoldanska;
                if (preostaleMinuteVPlus > 0 && delavnikPopoldanskaZaObracun > 0)
                {
                    var odsteto = Math.Min(delavnikPopoldanskaZaObracun, preostaleMinuteVPlus);
                    if (data.Partner == 314910)
                        ctx.Log.Add($"[DEBUG MIN]   Del16-22: obračunane={obracunaneRazdelitev.Delavnik_Popoldanska}, obvezno={obveznoRazdelitev.Delavnik_Popoldanska}, zaObračun={delavnikPopoldanskaZaObracun} → odšteto={odsteto}, preostalo={preostaleMinuteVPlus - odsteto}");
                    delavnikPopoldanskaZaObracun -= odsteto;
                    preostaleMinuteVPlus -= odsteto;
                    minuteKoriscene += odsteto;
                }
                else if (data.Partner == 314910)
                    ctx.Log.Add($"[DEBUG MIN]   Del16-22: obračunane={obracunaneRazdelitev.Delavnik_Popoldanska}, obvezno={obveznoRazdelitev.Delavnik_Popoldanska}, zaObračun={delavnikPopoldanskaZaObracun} → nič za odšteti (vPlus={preostaleMinuteVPlus})");

                // Delavnik - nočna (22-7)
                var delavnikNocnaZaObracun = obracunaneRazdelitev.Delavnik_Nocna - obveznoRazdelitev.Delavnik_Nocna;
                if (preostaleMinuteVPlus > 0 && delavnikNocnaZaObracun > 0)
                {
                    var odsteto = Math.Min(delavnikNocnaZaObracun, preostaleMinuteVPlus);
                    if (data.Partner == 314910)
                        ctx.Log.Add($"[DEBUG MIN]   Del22-7: obračunane={obracunaneRazdelitev.Delavnik_Nocna}, obvezno={obveznoRazdelitev.Delavnik_Nocna}, zaObračun={delavnikNocnaZaObracun} → odšteto={odsteto}, preostalo={preostaleMinuteVPlus - odsteto}");
                    delavnikNocnaZaObracun -= odsteto;
                    preostaleMinuteVPlus -= odsteto;
                    minuteKoriscene += odsteto;
                }
                else if (data.Partner == 314910)
                    ctx.Log.Add($"[DEBUG MIN]   Del22-7: obračunane={obracunaneRazdelitev.Delavnik_Nocna}, obvezno={obveznoRazdelitev.Delavnik_Nocna}, zaObračun={delavnikNocnaZaObracun} → nič za odšteti (vPlus={preostaleMinuteVPlus})");

                // Prištej obvezne minute nazaj (ObveznoZaracunaj - ne gredo skozi dobroimetje)
                delavnikDnevnaZaObracun += obveznoRazdelitev.Delavnik_Dnevna;
                delavnikPopoldanskaZaObracun += obveznoRazdelitev.Delavnik_Popoldanska;
                delavnikNocnaZaObracun += obveznoRazdelitev.Delavnik_Nocna;

                // Delavnik (z odštetim dobroimetjem)
                // Vikend in Praznik
                // Uporabi toleranco minut
                var (tDel7_16, tDel16_22, tDel22_7, tVik7_16, tVik16_22, tVik22_7, tPra7_16, tPra16_22, tPra22_7) = 
                    UpostevajiToleranco(ctx.TolerancaMinut,
                        delavnikDnevnaZaObracun, delavnikPopoldanskaZaObracun, delavnikNocnaZaObracun,
                        obracunaneRazdelitev.Vikend_Dnevna, obracunaneRazdelitev.Vikend_Popoldanska, obracunaneRazdelitev.Vikend_Nocna,
                        obracunaneRazdelitev.Praznik_Dnevna, obracunaneRazdelitev.Praznik_Popoldanska, obracunaneRazdelitev.Praznik_Nocna,
                        opis, data.Partner, ctx.Log);

                if (data.Partner == 314910)
                {
                    ctx.Log.Add($"[DEBUG MIN] S POGODBO po toleranci ({ctx.TolerancaMinut} min): Del7-16={tDel7_16}, Del16-22={tDel16_22}, Del22-7={tDel22_7}, Vik7-16={tVik7_16}, Vik16-22={tVik16_22}, Vik22-7={tVik22_7}, Pra7-16={tPra7_16}, Pra16-22={tPra16_22}, Pra22-7={tPra22_7}");
                    ctx.Log.Add($"[DEBUG MIN] ŠIFRE pogodba: Del7-16={ctx.ServisneNastavitve.GetSifraPogodba(TipDneva.Delavnik, CasovnaTarifa.Dnevna)}, Del16-22={ctx.ServisneNastavitve.GetSifraPogodba(TipDneva.Delavnik, CasovnaTarifa.Popoldanska)}, Del22-7={ctx.ServisneNastavitve.GetSifraPogodba(TipDneva.Delavnik, CasovnaTarifa.Nocna)}");
                    ctx.Log.Add($"[DEBUG MIN] KORIŠČENE minute dobroimetja: {minuteKoriscene}, preostaleVPlus={preostaleMinuteVPlus}");
                }

                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Dnevna, tDel7_16, opis, ref naslednjZs, manjkajoceSifre);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Popoldanska, tDel16_22, opis, ref naslednjZs, manjkajoceSifre);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Nocna, tDel22_7, opis, ref naslednjZs, manjkajoceSifre);

                // Vikend
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Dnevna, tVik7_16, opis, ref naslednjZs, manjkajoceSifre);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Popoldanska, tVik16_22, opis, ref naslednjZs, manjkajoceSifre);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Nocna, tVik22_7, opis, ref naslednjZs, manjkajoceSifre);

                // Praznik
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Dnevna, tPra7_16, opis, ref naslednjZs, manjkajoceSifre);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Popoldanska, tPra16_22, opis, ref naslednjZs, manjkajoceSifre);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Nocna, tPra22_7, opis, ref naslednjZs, manjkajoceSifre);
            }

            // Preračunaj MinuteObracunane = bruto minute - koriščene minute
            minuteObracunane -= minuteKoriscene;

            return (true, skupnaRazdelitev, minuteObracunane, minuteNeobracunane, minuteKoriscene);
        }

        /// <summary>
        /// Uporabi toleranco minut na razdelitev po intervalih.
        /// Že je v nekem intervalu manj minut kot toleranca, se te minute prenesejo v interval z največ minutami.
        /// Že je skupno minut manj kot toleranca, se vse minute postavijo na 0 (se ne obračunajo).
        /// </summary>
        private static (int del7_16, int del16_22, int del22_7, int vik7_16, int vik16_22, int vik22_7, int pra7_16, int pra16_22, int pra22_7) 
            UpostevajiToleranco(
                int toleranca,
                int del7_16, int del16_22, int del22_7,
                int vik7_16, int vik16_22, int vik22_7,
                int pra7_16, int pra16_22, int pra22_7,
                StringBuilder opis, int partner, List<string> log)
        {
            if (toleranca <= 0)
                return (del7_16, del16_22, del22_7, vik7_16, vik16_22, vik22_7, pra7_16, pra16_22, pra22_7);

            var skupajMinut = del7_16 + del16_22 + del22_7 + vik7_16 + vik16_22 + vik22_7 + pra7_16 + pra16_22 + pra22_7;

            // Že je skupno minut manj kot toleranca, ne obračunaj nič
            if (skupajMinut > 0 && skupajMinut < toleranca)
            {
                opis.AppendLine($"   TOLERANCA: Skupno {skupajMinut} min < {toleranca} min ? se ne obračuna");
                log.Add($"[TOLERANCA] Partner {partner}: Skupno {skupajMinut} min < {toleranca} min ? se ne obračuna");
                return (0, 0, 0, 0, 0, 0, 0, 0, 0);
            }

            // Poišči intervale s premalo minutami in jih prenesi v največji interval
            var intervali = new (string Naziv, int Minute)[]
            {
                ("Del 7-16", del7_16), ("Del 16-22", del16_22), ("Del 22-7", del22_7),
                ("Vik 7-16", vik7_16), ("Vik 16-22", vik16_22), ("Vik 22-7", vik22_7),
                ("Pra 7-16", pra7_16), ("Pra 16-22", pra16_22), ("Pra 22-7", pra22_7)
            };

            // Najdi interval z največ minutami
            var maxIdx = 0;
            for (int i = 1; i < intervali.Length; i++)
            {
                if (intervali[i].Minute > intervali[maxIdx].Minute)
                    maxIdx = i;
            }

            // Prenesi minute iz intervalov s premalo minutami v največji interval
            int preneseno = 0;
            for (int i = 0; i < intervali.Length; i++)
            {
                if (i == maxIdx) continue;
                if (intervali[i].Minute > 0 && intervali[i].Minute < toleranca)
                {
                    opis.AppendLine($"   TOLERANCA: {intervali[i].Naziv}: {intervali[i].Minute} min < {toleranca} min ? preneseno v {intervali[maxIdx].Naziv}");
                    preneseno += intervali[i].Minute;
                    intervali[i] = (intervali[i].Naziv, 0);
                }
            }

            if (preneseno > 0)
            {
                intervali[maxIdx] = (intervali[maxIdx].Naziv, intervali[maxIdx].Minute + preneseno);
            }

            return (intervali[0].Minute, intervali[1].Minute, intervali[2].Minute,
                    intervali[3].Minute, intervali[4].Minute, intervali[5].Minute,
                    intervali[6].Minute, intervali[7].Minute, intervali[8].Minute);
        }

        /// <summary>
        /// Ustvari postavko za obračun servisnih storitev brez pogodbe.
        /// </summary>
        private static void UstvariPostavkoBrezPogodbe(ObracunContext ctx, int partner, TipDneva tipDneva, CasovnaTarifa tarifa, int minute, StringBuilder opis, ref int naslednjZs, HashSet<string> manjkajoceSifre)
        {
            if (minute <= 0)
                return;

            // Pridobi šifro artikla za to obdobje
            var sifra = ctx.ServisneNastavitve.GetSifraBrezPogodbe(tipDneva, tarifa);
            if (string.IsNullOrEmpty(sifra))
            {
                var obdobje = DoloObdobjeNaziv(tipDneva, tarifa);
                manjkajoceSifre.Add($"Brez pogodbe, {obdobje}");
                return;
            }

            UstvariServisnoPostavko(ctx, partner, tipDneva, tarifa, minute, sifra, 0, opis, ref naslednjZs, manjkajoceSifre, "Servis");
        }

        /// <summary>
        /// Ustvari postavko za obračun servisnih storitev s pogodbo.
        /// </summary>
        private static void UstvariPostavkoPogodba(ObracunContext ctx, int partner, TipDneva tipDneva, CasovnaTarifa tarifa, int minute, StringBuilder opis, ref int naslednjZs, HashSet<string> manjkajoceSifre)
        {
            if (minute <= 0)
                return;

            // Pridobi šifro artikla za to obdobje
            var sifra = ctx.ServisneNastavitve.GetSifraPogodba(tipDneva, tarifa);
            if (string.IsNullOrEmpty(sifra))
            {
                var obdobje = DoloObdobjeNaziv(tipDneva, tarifa);
                manjkajoceSifre.Add($"Pogodba, {obdobje}");
                return;
            }

            UstvariServisnoPostavko(ctx, partner, tipDneva, tarifa, minute, sifra, ctx.PopustPogodbe, opis, ref naslednjZs, manjkajoceSifre, "Servis (pog.)");
        }

        /// <summary>
        /// Skupna metoda za ustvarjanje servisne postavke.
        /// </summary>
        private static void UstvariServisnoPostavko(ObracunContext ctx, int partner, TipDneva tipDneva, CasovnaTarifa tarifa, int minute, string sifra, decimal rabat, StringBuilder opis, ref int naslednjZs, HashSet<string> manjkajoceSifre, string nazivPrefix)
        {
            // Pridobi podatke o artiklu
            if (!ctx.Artikli.TryGetValue(sifra, out var artikel))
            {
                manjkajoceSifre.Add($"Artikel {sifra} ne obstaja v šifrantu");
                return;
            }

            // Izračunaj količino v urah glede na interval (enota artikla)
            // Vedno se obračunavajo URE, interval določa zaokroževanje:
            // - Interval Ura-m (15 minut): 1-15 min = 0.25 ure, 16-30 min = 0.5 ure, 31-45 min = 0.75 ure, 46-60 min = 1 ura, ...
            // - Interval URA (60 minut): 1-60 min = 1 ura, 61-120 min = 2 uri, ...
            decimal kolicina;
            string opisEnote;
            if (artikel.Enota.Equals(EnotaCetrtinUre, StringComparison.OrdinalIgnoreCase))
            {
                // Interval 15 minut - zaokroži na četrtine ure
                var cetrtine = (decimal)Math.Ceiling(minute / 15.0);
                kolicina = cetrtine * 0.25m;
                opisEnote = $"{minute} min = {kolicina:N2} ur ({cetrtine} x 15 min)";
            }
            else
            {
                // Interval 60 minut (URA) - vsaka začeta ura
                kolicina = (decimal)Math.Ceiling(minute / 60.0);
                opisEnote = $"{minute} min = {kolicina:N0} ur";
            }

            var cena = artikel.ProdajnaCena;
            var vrednost = kolicina * cena * (1 - rabat / 100);

            // Naziv obdobja
            var obdobjeNaziv = DoloObdobjeNaziv(tipDneva, tarifa);

            // Izpiši v opis
            var sifraStr = sifra.Length > 12 ? sifra.Substring(0, 12) : sifra.PadRight(12);
            var nazivStr = artikel.Naziv.Length > 40 ? artikel.Naziv.Substring(0, 40) : artikel.Naziv.PadRight(40);
            var enotaStr = artikel.Enota.Length > 10 ? artikel.Enota.Substring(0, 10) : artikel.Enota.PadRight(10);
            var kolicinaStr = kolicina.ToString("N1").PadLeft(6);
            var cenaStr = cena.ToString("N2").PadLeft(9);
            var rabatStr = rabat.ToString("N1").PadLeft(5);
            var vrednostStr = vrednost.ToString("N2").PadLeft(10);

            // Shrani postavko v bazo
            ctx.Db.Insert(new ObracunOsnutekPos
            {
                Mesec = ctx.Mesec,
                Leto = ctx.Leto,
                Partner = partner,
                Zs = naslednjZs++,
                Artikel = sifra,
                Naziv = $"{nazivPrefix} {obdobjeNaziv}",
                Kolicina = kolicina,
                Cena = cena,
                Rabat = rabat,
                TipPostavke = TipPostavke.NALOG
            });
        }

        /// <summary>
        /// Vrne naziv obdobja za izpis.
        /// </summary>
        private static string DoloObdobjeNaziv(TipDneva tipDneva, CasovnaTarifa tarifa)
        {
            return (tipDneva, tarifa) switch
            {
                (TipDneva.Delavnik, CasovnaTarifa.Dnevna) => "Delavnik 7-16",
                (TipDneva.Delavnik, CasovnaTarifa.Popoldanska) => "Delavnik 16-22",
                (TipDneva.Delavnik, CasovnaTarifa.Nocna) => "Delavnik 22-7",
                (TipDneva.Vikend, CasovnaTarifa.Dnevna) => "Vikend 7-16",
                (TipDneva.Vikend, CasovnaTarifa.Popoldanska) => "Vikend 16-22",
                (TipDneva.Vikend, CasovnaTarifa.Nocna) => "Vikend 22-7",
                (TipDneva.Praznik, CasovnaTarifa.Dnevna) => "Praznik 7-16",
                (TipDneva.Praznik, CasovnaTarifa.Popoldanska) => "Praznik 16-22",
                (TipDneva.Praznik, CasovnaTarifa.Nocna) => "Praznik 22-7",
                _ => $"{tipDneva} {tarifa}"
            };
        }

        /// <summary>
        /// DEBUG: Izpiše prispevek naloga k postavkam.
        /// </summary>
        private static void IzpisiPrispevekNaloga(ObracunContext ctx, MinuteRazdelitev razdelitev, bool imaPogodbo)
        {
            // Vse minute prispevajo (tako za pogodbo kot brez)
            var postavke = new List<string>();

            // Delavnik
            if (razdelitev.Delavnik_Dnevna > 0) postavke.Add($"Del 7-16: {razdelitev.Delavnik_Dnevna} min");
            if (razdelitev.Delavnik_Popoldanska > 0) postavke.Add($"Del 16-22: {razdelitev.Delavnik_Popoldanska} min");
            if (razdelitev.Delavnik_Nocna > 0) postavke.Add($"Del 22-7: {razdelitev.Delavnik_Nocna} min");
            // Vikend
            if (razdelitev.Vikend_Dnevna > 0) postavke.Add($"Vik 7-16: {razdelitev.Vikend_Dnevna} min");
            if (razdelitev.Vikend_Popoldanska > 0) postavke.Add($"Vik 16-22: {razdelitev.Vikend_Popoldanska} min");
            if (razdelitev.Vikend_Nocna > 0) postavke.Add($"Vik 22-7: {razdelitev.Vikend_Nocna} min");
            // Praznik
            if (razdelitev.Praznik_Dnevna > 0) postavke.Add($"Pra 7-16: {razdelitev.Praznik_Dnevna} min");
            if (razdelitev.Praznik_Popoldanska > 0) postavke.Add($"Pra 16-22: {razdelitev.Praznik_Popoldanska} min");
            if (razdelitev.Praznik_Nocna > 0) postavke.Add($"Pra 22-7: {razdelitev.Praznik_Nocna} min");

            if (postavke.Count > 0)
            {
                var pogodbaSufiks = imaPogodbo ? " (pogodba)" : "";
                ctx.Log.Add($"[DEBUG]      -> Prispevek{pogodbaSufiks}: {string.Join(", ", postavke)}");
            }
        }


        /// <summary>
        /// Zapiše razdelitev minut naloga v tabelo OBRACUN_OSNUTEK_NALOG_OBRACUN z odštevanjem.
        /// </summary>
        private static void ZapisiNalogObracun(ObracunContext ctx, FaDnNalog nalog, MinuteRazdelitev razdelitev, int obracunam, bool imaPogodbo, MinutniSklad? sklad, HashSet<(string PredStevilka, int PredLeto)>? povezaniPredracuni, int trajanjeNaloga)
        {
            // Za vsako kategorijo minut zapiši zapis (samo delavniškie minute se odštevajo)
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Delavnik, CasovnaTarifa.Dnevna, razdelitev.Delavnik_Dnevna, obracunam, imaPogodbo, sklad, povezaniPredracuni, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Delavnik, CasovnaTarifa.Popoldanska, razdelitev.Delavnik_Popoldanska, obracunam, imaPogodbo, sklad, povezaniPredracuni, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Delavnik, CasovnaTarifa.Nocna, razdelitev.Delavnik_Nocna, obracunam, imaPogodbo, sklad, povezaniPredracuni, trajanjeNaloga);

            // Vikend in praznik (brez odštevanja - null vrednosti za odštete minute)
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Vikend, CasovnaTarifa.Dnevna, razdelitev.Vikend_Dnevna, obracunam, imaPogodbo, null, null, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Vikend, CasovnaTarifa.Popoldanska, razdelitev.Vikend_Popoldanska, obracunam, imaPogodbo, null, null, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Vikend, CasovnaTarifa.Nocna, razdelitev.Vikend_Nocna, obracunam, imaPogodbo, null, null, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Praznik, CasovnaTarifa.Dnevna, razdelitev.Praznik_Dnevna, obracunam, imaPogodbo, null, null, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Praznik, CasovnaTarifa.Popoldanska, razdelitev.Praznik_Popoldanska, obracunam, imaPogodbo, null, null, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Praznik, CasovnaTarifa.Nocna, razdelitev.Praznik_Nocna, obracunam, imaPogodbo, null, null, trajanjeNaloga);
        }

        /// <summary>
        /// Zapiše eno kategorijo minut v tabelo OBRACUN_OSNUTEK_NALOG_OBRACUN.
        /// </summary>
        private static void ZapisiKategorijoNalogObracun(ObracunContext ctx, FaDnNalog nalog, TipDneva tipDneva, CasovnaTarifa tarifa, int minute, int obracunam, bool imaPogodbo, MinutniSklad? sklad, HashSet<(string PredStevilka, int PredLeto)>? povezaniPredracuni, int trajanjeNaloga)
        {
            if (minute <= 0)
                return;

            // Pridobi šifro artikla glede na pogodbo
            string sifra;
            if (imaPogodbo)
            {
                if (tipDneva == TipDneva.Delavnik)
                    sifra = ctx.ServisneNastavitve.GetSifraBrezPogodbe(tipDneva, tarifa);
                else
                    sifra = ctx.ServisneNastavitve.GetSifraPogodba(tipDneva, tarifa);
            }
            else
            {
                sifra = ctx.ServisneNastavitve.GetSifraBrezPogodbe(tipDneva, tarifa);
            }

            if (string.IsNullOrEmpty(sifra))
                return;

            // Pridobi podatke o artiklu
            ctx.Artikli.TryGetValue(sifra, out var artikel);
            var cena = artikel?.ProdajnaCena ?? 0;

            // Izračunaj količino v urah glede na interval
            decimal kolicina;
            if (artikel != null && artikel.Enota.Equals(EnotaCetrtinUre, StringComparison.OrdinalIgnoreCase))
            {
                var cetrtine = (decimal)Math.Ceiling(minute / 15.0);
                kolicina = cetrtine * 0.25m;
            }
            else
            {
                kolicina = (decimal)Math.Ceiling(minute / 60.0);
            }

            // Odštej minute iz sklada (samo za delavniškie minute in če se obračunajo)
            OdstevanjeMinutRezultat? odstevanje = null;
            if (sklad != null && obracunam == 1)
            {
                // Terenski nalogi (številka se ne začne z 1) ne koristijo dobroimetja iz pogodbe
                var jeHelpdesk = nalog.Stevilka.Length == 7 && nalog.Stevilka.StartsWith("1");
                odstevanje = OdstejiMinuteIzSklada(sklad, minute, povezaniPredracuni, uporabiPogodbo: jeHelpdesk);
            }

            // Izračunaj fakturirane minute (po odštevanju dobroimetja)
            int minuteFakturirane = 0;
            if (obracunam == 1 && odstevanje != null)
            {
                minuteFakturirane = odstevanje.MinuteZaObracun;
            }
            else if (obracunam == 1)
            {
                // Že ni odštevanja (vikend/praznik), so fakturirane = bruto minute
                minuteFakturirane = minute;
            }

            // Zapiši v tabelo
            ctx.Db.Insert(new ObracunOsnutekNalogObracun
            {
                Mesec = ctx.Mesec,
                Leto = ctx.Leto,
                Partner = nalog.Partner,
                StevilkaNaloga = nalog.Stevilka,
                LetoNaloga = nalog.Leto,
                Obracunam = obracunam,
                SifraArtikla = sifra,
                SifraKomercialista = nalog.Potnik,
                Kolicina = kolicina,
                ProdajnaCena = cena,
                MinuteOdstetePartnerMinute = odstevanje?.MinuteOdstetePartnerMinute,
                MinuteOdstetePredracun = odstevanje?.MinuteOdstetePredracun,
                MinuteOdsteteRocno = odstevanje?.MinuteOdsteteRocno,
                MinuteOdstetePogodba = odstevanje?.MinuteOdstetePogodba,
                MinuteNalog = trajanjeNaloga,
                KolicinaFakturirana = minuteFakturirane
            });
        }

        private static void ObdelajKilometrino(ObracunContext ctx, FaDnNalog nalog, ObracunDn? obracunDn, StringBuilder opis, ref int naslednjZs, int partner, HashSet<string> manjkajoceSifre)
        {
            var jeDebug = partner == 314910;

            // Preveri ali se kilometri obračunajo (KajObracunam mora biti KmMin ali Km)
            if (obracunDn == null)
            {
                if (jeDebug) ctx.Log.Add($"[DEBUG KM] Partner {partner}, Nalog {nalog.Stevilka}/{nalog.Leto}: PRESKOČEN - obracunDn je NULL");
                return;
            }

            if (obracunDn.KajObracunam != KajObracunam.KmMin && obracunDn.KajObracunam != KajObracunam.Km && obracunDn.KajObracunam != KajObracunam.ObveznoZaracunaj)
            {
                if (jeDebug) ctx.Log.Add($"[DEBUG KM] Partner {partner}, Nalog {nalog.Stevilka}/{nalog.Leto}: PRESKOČEN - KajObracunam={obracunDn.KajObracunam} (ni KmMin/Km/ObveznoZaracunaj)");
                return;
            }

            // Preveri ali je šifra kilometrine nastavljena
            if (string.IsNullOrEmpty(ctx.SifraKilometrina))
            {
                if (jeDebug) ctx.Log.Add($"[DEBUG KM] Partner {partner}, Nalog {nalog.Stevilka}/{nalog.Leto}: PRESKOČEN - SifraKilometrina ni nastavljena");
                manjkajoceSifre.Add("Šifra kilometrine ni nastavljena v Parametri > Servisna");
                return;
            }

            // Preveri ali se številka ne obračuna (helper)
            if (!NalogHelper.SeObracunaKilometrina(nalog.Stevilka))
            {
                if (jeDebug) ctx.Log.Add($"[DEBUG KM] Partner {partner}, Nalog {nalog.Stevilka}/{nalog.Leto}: PRESKOČEN - SeObracunaKilometrina=false (helpdesk nalog)");
                return;
            }

            // Pridobi kilometre iz naloga (SIF30)
            var km = (decimal)nalog.Sif30;
            if (km <= 0)
            {
                if (jeDebug) ctx.Log.Add($"[DEBUG KM] Partner {partner}, Nalog {nalog.Stevilka}/{nalog.Leto}: PRESKOČEN - SIF30={nalog.Sif30} (km <= 0)");
                return;
            }

            // Polovična kilometrina: SIF29 = 1
            var jePolovicna = nalog.Sif29 == 1;
            if (jePolovicna)
                km = km / 2;

            // Pridobi podatke o artiklu
            ctx.Artikli.TryGetValue(ctx.SifraKilometrina, out var artikel);
            var cena = artikel?.ProdajnaCena ?? 0;
            var naziv = $"Kilometrina DN:{nalog.Stevilka}";
            var vrednost = km * cena;

            // Izpiši v opis
            var sifraStr = ctx.SifraKilometrina.Length > 12 ? ctx.SifraKilometrina.Substring(0, 12) : ctx.SifraKilometrina.PadRight(12);
            var nazivStr = naziv.Length > 40 ? naziv.Substring(0, 40) : naziv.PadRight(40);
            var enotaStr = "km".PadRight(10);
            var kolicinaStr = km.ToString("N1").PadLeft(6);
            var cenaStr = cena.ToString("N2").PadLeft(9);
            var rabatStr = "0,0".PadLeft(5);
            var vrednostStr = vrednost.ToString("N2").PadLeft(10);

            var polovicnaOznaka = jePolovicna ? " (1/2)" : "";
            if (jeDebug) ctx.Log.Add($"[DEBUG KM] Partner {partner}, Nalog {nalog.Stevilka}/{nalog.Leto}: ZARAČUNAM - SIF30={nalog.Sif30}, km={km}, SIF29={nalog.Sif29}, polovična={jePolovicna}, cena={cena}");
            opis.AppendLine($"   + {sifraStr} {nazivStr} {enotaStr} {kolicinaStr} {cenaStr} {rabatStr} {vrednostStr}{polovicnaOznaka}");

            // Shrani postavko v bazo
            ctx.Db.Insert(new ObracunOsnutekPos
            {
                Mesec = ctx.Mesec,
                Leto = ctx.Leto,
                Partner = partner,
                Zs = naslednjZs++,
                Artikel = ctx.SifraKilometrina,
                Naziv = naziv,
                Kolicina = km,
                Cena = cena,
                Rabat = 0,
                NalogStevilka = nalog.Stevilka,
                NalogLeto = nalog.Leto,
                TipPostavke = TipPostavke.NALOG
            });
        }

        #endregion

        #region Shranjevanje

        private static void ShraniOsnutek(ObracunContext ctx, PartnerObracunResult result)
        {
            ctx.Db.Insert(new ObracunOsnutek
            {
                Mesec = ctx.Mesec,
                Leto = ctx.Leto,
                Partner = result.Partner,
                ImaPogodbo = result.ImaPogodbo ? 1 : 0,
                LetnaPogodba = result.LetnaPogodba ? 1 : 0,
                ImaPredracun = result.MinutePredracuni > 0 ? 1 : 0,
                ImaNaloge = result.ImaNaloge ? 1 : 0,
                Opis = result.Opis,
                MinuteObracunane = result.MinuteObracunane,
                MinuteNeobracunane = result.MinuteNeobracunane,
                MinuteKoriscene = result.MinuteKoriscene,
                PlusMinutePartnerMinute = result.MinutePartnerMinute,
                PlusMinutePredracun = result.MinutePredracuni,
                PlusMinuteRocno = result.MinuteRocni,
                PlusMinutePogodba = result.MinutePogodbe,
                VseMinutePredracun = result.VseMinutePredracuni,
                ZePorabljenePredracun = result.ZePorabljenePredracuni,
                VseMinutePartnerMinute = result.VseMinutePartnerMinute,
                ZePorabljenePartnerMinute = result.ZePorabljenePartnerMinute
            });
        }

        private void ShraniLog(ObracunLinqDb db, int mesec, int leto, List<string> log)
        {
            try
            {
                var logText = string.Join(Environment.NewLine, log);

                db.ObracunLog.Where(l => l.Mesec == mesec && l.Leto == leto).Delete();

                db.Insert(new ObracunLog
                {
                    Mesec = mesec,
                    Leto = leto,
                    Datum = DateTime.Now,
                    LogData = logText
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Napaka pri shranjevanju loga: {ex.Message}");
            }
        }

        #endregion

        #region Pomožne metode

        private static bool JePostavkaVeljavnaZaMesec(string? meseci, string mesecStr)
        {
            if (string.IsNullOrWhiteSpace(meseci))
                return true;

            var mesecList = meseci.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                  .Select(m => m.Trim())
                                  .ToList();

            return mesecList.Contains(mesecStr);
        }

        #endregion

        #region Javne metode za log

        public (string? LogText, DateTime? Datum) NaloziLog(int mesec, int leto)
        {
            try
            {
                using var db = CreateDb();
                var logZapis = db.ObracunLog.FirstOrDefault(l => l.Mesec == mesec && l.Leto == leto);

                if (logZapis != null)
                    return (logZapis.LogData, logZapis.Datum);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Napaka pri nalaganju loga: {ex.Message}");
            }

            return (null, null);
        }

        public async Task<string?> GetObracunLogAsync(int leto, int mesec)
        {
            return await Task.Run(() => NaloziLog(mesec, leto).LogText);
        }

        #endregion

        #region Helper metode

        /// <summary>
        /// Preveri ali je minuta veljavna za določen mesec/leto obračuna.
        /// Minuta je veljavna, če je mesec/leto obračuna znotraj obdobja veljavnosti.
        /// </summary>
        private static bool JeVeljavnaMinuta(PartnerMinute minuta, int mesecObracuna, int letoObracuna)
        {
            if (!minuta.ZacetekMesec.HasValue || !minuta.ZacetekLeto.HasValue)
                return false;

            // Izračunaj mesec/leto začetka
            var zacetekDatum = new DateTime(minuta.ZacetekLeto.Value, minuta.ZacetekMesec.Value, 1);
            
            // Izračunaj mesec/leto konca (začetek + veljavnost)
            var konecDatum = zacetekDatum.AddMonths(minuta.VeljavnostMesecih);
            
            // Mesec/leto obračuna
            var obracunDatum = new DateTime(letoObracuna, mesecObracuna, 1);
            
            // Preveri ali je obračun znotraj obdobja [začetek, konec)
            return obracunDatum >= zacetekDatum && obracunDatum < konecDatum;
        }

        #endregion
    }
}

