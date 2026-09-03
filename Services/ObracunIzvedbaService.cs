using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Extensions.Hosting;
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
        private readonly IHostEnvironment _environment;

        public ObracunIzvedbaService(FirebirdConnectionManager connectionManager, ParametriService parametri, IHostEnvironment environment)
        {
            _connectionManager = connectionManager;
            _parametri = parametri;
            _environment = environment;
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

        public (bool Success, string Message, int RecordsProcessed, List<string> Log) IzvediObracun(int mesec, int leto, HashSet<DateTime>? prazniki = null, int debugPartner = 0)
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

                // Log sprememb količin (čez vse partnerje)
                var spremembeLog = new List<string>();

                // DEBUG: prestrezi opis za izbranega debug partnerja (0 = brez debug)
                string? debugOpis = null;
                bool debugPartnerObdelan = false;
                PartnerObracunResult? debugResult = null;

                foreach (var partner in vsiPartnerji)
                {
                    var partnerData = PripraviPodatkeZaPartnerja(ctx, partner);
                    var result = ObdelajPartnerja(ctx, partnerData, manjkajoceSifre);

                    // DEBUG: za izbranega debug partnerja zapiši insert + preveri rezultat (zapiši v Opis, da bo vidno v zadnjem bloku)
                    if (debugPartner > 0 && partner == debugPartner)
                    {
                        var dbgSb = new System.Text.StringBuilder(result.Opis ?? "");
                        try
                        {
                            dbgSb.AppendLine();
                            dbgSb.AppendLine($"[INSERT-DEBUG] Pred ShraniOsnutek: Opis dolžina = {result.Opis?.Length ?? 0} znakov");
                            dbgSb.AppendLine($"[INSERT-DEBUG] MinuteObracunane={result.MinuteObracunane}, MinuteNeobracunane={result.MinuteNeobracunane}, ImaNaloge={result.ImaNaloge}, ImaPogodbo={result.ImaPogodbo}");

                            var preObstaja = ctx.Db.ObracunOsnutek.Any(o => o.Mesec == ctx.Mesec && o.Leto == ctx.Leto && o.Partner == partner);
                            dbgSb.AppendLine($"[INSERT-DEBUG] Pred insertom obstaja v OBRACUN_OSNUTEK: {preObstaja}");

                            ShraniOsnutek(ctx, result);

                            var poObstaja = ctx.Db.ObracunOsnutek.Any(o => o.Mesec == ctx.Mesec && o.Leto == ctx.Leto && o.Partner == partner);
                            dbgSb.AppendLine($"[INSERT-DEBUG] Po insertu obstaja v OBRACUN_OSNUTEK: {poObstaja}");

                            var stPostavk = ctx.Db.ObracunOsnutekPos.Count(p => p.Mesec == ctx.Mesec && p.Leto == ctx.Leto && p.Partner == partner);
                            dbgSb.AppendLine($"[INSERT-DEBUG] Število postavk v OBRACUN_OSNUTEK_POS: {stPostavk}");
                        }
                        catch (Exception exDbg)
                        {
                            dbgSb.AppendLine($"[INSERT-DEBUG] !!! NAPAKA pri ShraniOsnutek: {exDbg.GetType().FullName}: {exDbg.Message}");
                            var inner = exDbg.InnerException;
                            int level = 1;
                            while (inner != null)
                            {
                                dbgSb.AppendLine($"[INSERT-DEBUG] !!! Inner({level}): {inner.GetType().FullName}: {inner.Message}");
                                inner = inner.InnerException;
                                level++;
                            }
                            dbgSb.AppendLine($"[INSERT-DEBUG] !!! Stack: {exDbg.StackTrace}");
                        }
                        result.Opis = dbgSb.ToString();
                    }
                    else
                    {
                        ShraniOsnutek(ctx, result);
                    }

                    // Uporabi ročne spremembe količin (Sprememba količine)
                    UporabiSpremembeKolicin(ctx, partner, spremembeLog);

                    // Prištej minute partnerja k skupnim
                    skupneMinute.Pristej(result.MinuteNalogov);

                    if (debugPartner > 0 && partner == debugPartner)
                    {
                        debugPartnerObdelan = true;
                        debugOpis = result.Opis;
                        debugResult = result;
                    }
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

                // DEBUG: izpis za izbranega debug partnerja (samo če je nastavljen, > 0)
                if (debugPartner > 0)
                {
                    log.Add("");
                    log.Add("============================================");
                    log.Add($"=== DEBUG za partnerja {debugPartner} ===");
                    log.Add("============================================");
                    if (!vsiPartnerji.Contains(debugPartner))
                    {
                        log.Add($"Partner {debugPartner} NI v seznamu partnerjev za obračun.");
                        log.Add("Razlog: nima nalogov, aktivnih pogodb in ročnih postavk za ta mesec.");
                        var imaNaloge = ctx.Nalogi.Any(n => n.Partner == debugPartner);
                        var imaPogodbo = ctx.AktivnePogodbe.Any(p => p.Partner == debugPartner);
                        var imaRocno = ctx.RocnePostavke.Any(p => p.Partner == debugPartner);
                        log.Add($"  - Ima naloge v ctx.Nalogi: {imaNaloge}");
                        log.Add($"  - Ima aktivne pogodbe: {imaPogodbo}");
                        log.Add($"  - Ima ročne postavke: {imaRocno}");
                    }
                    else if (debugPartnerObdelan && debugResult != null)
                    {
                        log.Add($"ImaPogodbo: {debugResult.ImaPogodbo}, LetnaPogodba: {debugResult.LetnaPogodba}");
                        log.Add($"ImaNaloge: {debugResult.ImaNaloge}");
                        log.Add($"MinuteNalogov: {debugResult.MinuteNalogov.SkupajMinut}, MinuteObracunane: {debugResult.MinuteObracunane}, MinuteNeobracunane: {debugResult.MinuteNeobracunane}, MinuteKoriscene: {debugResult.MinuteKoriscene}");
                        log.Add($"MinutePredracuni: {debugResult.MinutePredracuni}, MinuteRocni: {debugResult.MinuteRocni}, MinutePogodbe: {debugResult.MinutePogodbe}, MinutePartnerMinute: {debugResult.MinutePartnerMinute}");
                        log.Add("--- Opis obračuna ---");
                        if (!string.IsNullOrEmpty(debugOpis))
                        {
                            foreach (var vrstica in debugOpis.Split('\n'))
                                log.Add(vrstica.TrimEnd('\r'));
                        }
                        else
                        {
                            log.Add("(brez opisa)");
                        }
                    }
                    else
                    {
                        log.Add("Partner je v seznamu, vendar ni bil obdelan (interna napaka).");
                    }
                    log.Add("============================================");
                }

                ShraniLog(db, mesec, leto, log);

                // Prekini obračun, če je bila kot postavka računa (OBRACUN_OSNUTEK_POS) zapisana šifra artikla,
                // ki ne obstaja v FA_ARTIKEL. Enaka kontrola kot pri prenosu v FAW.
                var uporabljeneSifre = db.ObracunOsnutekPos
                    .Where(p => p.Mesec == mesec && p.Leto == leto)
                    .Select(p => p.Artikel)
                    .ToList()
                    .Select(s => s?.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s) && s != "-")
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (uporabljeneSifre.Count > 0)
                {
                    var obstojeceSifre = db.FaArtikel
                        .Where(a => uporabljeneSifre.Contains(a.Sifra))
                        .Select(a => a.Sifra)
                        .ToList()
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var manjkajociArtikli = uporabljeneSifre
                        .Where(s => !obstojeceSifre.Contains(s))
                        .OrderBy(s => s)
                        .ToList();

                    if (manjkajociArtikli.Count > 0)
                    {
                        var manjkajociStr = string.Join(", ", manjkajociArtikli);
                        var manjkajociSet = manjkajociArtikli.ToHashSet(StringComparer.OrdinalIgnoreCase);

                        // Poišči partnerje, ki imajo manjkajočo šifro med postavkami računa
                        var prizadetiPartnerji = db.ObracunOsnutekPos
                            .Where(p => p.Mesec == mesec && p.Leto == leto)
                            .ToList()
                            .Where(p => !string.IsNullOrWhiteSpace(p.Artikel) && manjkajociSet.Contains(p.Artikel!.Trim()))
                            .Select(p => p.Partner)
                            .Distinct()
                            .OrderBy(p => p)
                            .ToList();

                        log.Add("");
                        log.Add("============================================");
                        log.Add("=== NAPAKA: ARTIKLI NE OBSTAJAJO V FA_ARTIKEL ===");
                        log.Add("============================================");
                        foreach (var sifra in manjkajociArtikli)
                            log.Add($"   - Artikel '{sifra}' ne obstaja v šifrantu (FA_ARTIKEL)");
                        log.Add("============================================");

                        // Za vsakega prizadetega partnerja izpiši VSE postavke njegovega računa
                        foreach (var prizadetiPartner in prizadetiPartnerji)
                        {
                            var postavkePartnerja = db.ObracunOsnutekPos
                                .Where(p => p.Mesec == mesec && p.Leto == leto && p.Partner == prizadetiPartner)
                                .OrderBy(p => p.Zs)
                                .ToList();

                            log.Add("");
                            log.Add($"Partner {prizadetiPartner} — postavke računa ({postavkePartnerja.Count}):");
                            foreach (var p in postavkePartnerja)
                            {
                                var izvor = OpisiIzvorPostavke(p);
                                var manjkaOznaka = !string.IsNullOrWhiteSpace(p.Artikel) && manjkajociSet.Contains(p.Artikel!.Trim())
                                    ? "  <<< MANJKA V FA_ARTIKEL"
                                    : "";
                                log.Add($"    Zs={p.Zs}, Artikel='{p.Artikel}', Naziv='{p.Naziv}', Kolicina={p.Kolicina}, Cena={p.Cena}, Rabat={p.Rabat}, Izvor={izvor}{manjkaOznaka}");
                            }

                            // Povzetek: pri katerem dodajanju je nastala manjkajoča postavka
                            var manjkajocePostavke = postavkePartnerja
                                .Where(p => !string.IsNullOrWhiteSpace(p.Artikel) && manjkajociSet.Contains(p.Artikel!.Trim()))
                                .ToList();
                            foreach (var mp in manjkajocePostavke)
                            {
                                log.Add($"    >>> Manjkajoč artikel '{mp.Artikel}' (Zs={mp.Zs}) je bil dodan pri: {OpisiIzvorPostavke(mp)}");
                            }
                        }
                        log.Add("============================================");

                        var partnerjiStr = prizadetiPartnerji.Count > 0
                            ? $" (partner: {string.Join(", ", prizadetiPartnerji)})"
                            : "";
                        var napaka = $"Obračun prekinjen: postavka računa vsebuje artikel, ki ne obstaja v FA_ARTIKEL: {manjkajociStr}{partnerjiStr}.";
                        log.Add(napaka);
                        ShraniLog(db, mesec, leto, log);
                        return (false, napaka, 0, log);
                    }
                }

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
            List<(int Index, double Ms, int? Rows, string Sql)>? sqlDebugEntries = null;
            var sqlDebugIndex = 0;
            Stopwatch? sqlDebugSw = null;

            try
            {
                using var db = CreateDb();

                // DEBUG: SQL tracing (samo Development/Visual Studio)
                if (_environment.IsDevelopment())
                {
                    sqlDebugEntries = new();
                    sqlDebugSw = Stopwatch.StartNew();
                    db.TraceSwitchConnection = new TraceSwitch("debug", "debug") { Level = TraceLevel.Info };
                    db.OnTraceConnection = info =>
                    {
                        if (info.TraceInfoStep == TraceInfoStep.AfterExecute)
                        {
                            sqlDebugIndex++;
                            sqlDebugEntries.Add((sqlDebugIndex, info.ExecutionTime?.TotalMilliseconds ?? 0, info.RecordsAffected, info.SqlText ?? ""));
                        }
                    };
                }

                var log = new List<string>();
                log.Add($"=== Ponovni obračun za partnerja {partner}, {mesec}/{leto} ===");

                var ctx = NaloziPodatke(db, mesec, leto, log, prazniki, samoPartner: partner);

                // Pobriši samo postavke za tega partnerja (razen ročnih)
                PripraviTabeleZaPartnerja(ctx, partner);

                // Obdelaj samo tega partnerja
                var manjkajoceSifre = new HashSet<string>();
                var partnerData = PripraviPodatkeZaPartnerja(ctx, partner);

                var result = ObdelajPartnerja(ctx, partnerData, manjkajoceSifre);

                ShraniOsnutek(ctx, result);

                // V Development okolju zapiši podrobnosti obračuna v datoteko
                if (_environment.IsDevelopment())
                    ZapisiIzracunVDatoteko(db, ctx, result, partner, mesec, leto);

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
            finally
            {
                if (sqlDebugEntries != null)
                {
                    sqlDebugSw?.Stop();
                    var sb = new StringBuilder();
                    sb.AppendLine($"SQL DEBUG — Partner: {partner}, Mesec: {mesec}/{leto}");
                    sb.AppendLine($"Datum: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
                    sb.AppendLine(new string('=', 80));
                    sb.AppendLine();

                    // Tabela padajoče po trajanju
                    sb.AppendLine($"{"#",5} {"Trajanje (ms)",15} {"Vrstic",10}");
                    sb.AppendLine($"{new string('-', 5),5} {new string('-', 15),15} {new string('-', 10),10}");
                    foreach (var e in sqlDebugEntries.OrderByDescending(e => e.Ms))
                        sb.AppendLine($"{e.Index,5} {e.Ms,15:F1} {(e.Rows.HasValue ? e.Rows.Value.ToString() : "n/a"),10}");
                    sb.AppendLine($"{new string('-', 5),5} {new string('-', 15),15} {new string('-', 10),10}");
                    sb.AppendLine($"{"SKUPAJ",-5} {sqlDebugSw?.ElapsedMilliseconds,15}");
                    sb.AppendLine();

                    // SQL podrobnosti
                    foreach (var e in sqlDebugEntries)
                    {
                        sb.AppendLine($"--- #{e.Index} ({e.Ms:F1} ms, {(e.Rows.HasValue ? e.Rows.Value + " vrstic" : "n/a")}) ---");
                        sb.AppendLine(e.Sql);
                        sb.AppendLine();
                    }

                    try { File.WriteAllText(@"c:\vs\debug.txt", sb.ToString()); }
                    catch { /* ignore */ }
                }
            }
        }

        /// <summary>
        /// Zapiše podrobnosti obračuna v izracun.txt (samo Development).
        /// </summary>
        private static void ZapisiIzracunVDatoteko(ObracunLinqDb db, ObracunContext ctx, PartnerObracunResult result, int partner, int mesec, int leto)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("================================================================================");
                sb.AppendLine($"IZRAČUN OBRAČUNA — Partner: {partner}, Mesec: {mesec}, Leto: {leto}");
                sb.AppendLine($"Datum izračuna: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
                sb.AppendLine("================================================================================");
                sb.AppendLine();

                // 1) Celoten opis obdelave (predračuni, pogodbe, nalogi, dobroimetje, toleranca, servisne storitve)
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine("PODROBNOSTI OBDELAVE");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine(result.Opis);
                sb.AppendLine();

                // 2) Povzetek minut
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine("POVZETEK MINUT");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine($"Ima pogodbo:              {(result.ImaPogodbo ? "DA" : "NE")}");
                if (result.ImaPogodbo)
                    sb.AppendLine($"Letna pogodba:            {(result.LetnaPogodba ? "DA" : "NE")}");
                sb.AppendLine($"Ima naloge:               {(result.ImaNaloge ? "DA" : "NE")}");
                sb.AppendLine();
                sb.AppendLine("Dobroimetje (minute v plus):");
                sb.AppendLine($"  Predračuni:             {result.MinutePredracuni,6} min (skupno: {result.VseMinutePredracuni}, že porabljeno prej: {result.ZePorabljenePredracuni})");
                sb.AppendLine($"  Ročno vnešene:          {result.MinuteRocni,6} min");
                sb.AppendLine($"  Pogodbe:                {result.MinutePogodbe,6} min");
                sb.AppendLine($"  Partner minute:         {result.MinutePartnerMinute,6} min (skupno: {result.VseMinutePartnerMinute}, že porabljeno prej: {result.ZePorabljenePartnerMinute})");
                sb.AppendLine($"  ─────────────────────────────");
                sb.AppendLine($"  SKUPAJ dobroimetje:     {result.MinuteVPlus,6} min");
                sb.AppendLine();
                sb.AppendLine("Minute nalogov:");
                sb.AppendLine($"  Razdelitev:             {result.MinuteNalogov}");
                sb.AppendLine($"  Obračunane (za zaračun):{result.MinuteObracunane,6} min");
                sb.AppendLine($"  Neobračunane:           {result.MinuteNeobracunane,6} min");
                sb.AppendLine($"  Koriščene (iz dobro.):  {result.MinuteKoriscene,6} min");
                sb.AppendLine();

                // 3) Končne postavke računa (OBRACUN_OSNUTEK_POS)
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
                sb.AppendLine("KONČNE POSTAVKE RAČUNA (OBRACUN_OSNUTEK_POS)");
                sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");

                var postavke = db.ObracunOsnutekPos
                    .Where(p => p.Mesec == mesec && p.Leto == leto && p.Partner == partner)
                    .OrderBy(p => p.Zs)
                    .ToList();

                if (postavke.Count == 0)
                {
                    sb.AppendLine("  (ni postavk)");
                }
                else
                {
                    sb.AppendLine($"  {"Zs",4} {"Tip",-8} {"Artikel",-12} {"Naziv",-35} {"Količina",10} {"Cena",10} {"Rabat",6} {"Vrednost",12}  {"Nalog/Pogodba"}");
                    sb.AppendLine($"  {"----",4} {"--------",-8} {"------------",-12} {"-----------------------------------",-35} {"----------",10} {"----------",10} {"------",6} {"------------",12}  {"--------------"}");

                    decimal skupnaVrednost = 0;
                    foreach (var pos in postavke)
                    {
                        var kolicina = pos.Kolicina ?? 0;
                        var cena = pos.Cena ?? 0;
                        var rabat = pos.Rabat ?? 0;
                        var vrednost = kolicina * cena * (1 - rabat / 100);
                        skupnaVrednost += vrednost;

                        var tipStr = pos.TipPostavke switch
                        {
                            TipPostavke.ROCNI => "ROČNI",
                            TipPostavke.POGODBA => "POGODBA",
                            TipPostavke.NALOG => "NALOG",
                            _ => "?"
                        };
                        var naziv = (pos.Naziv ?? "").Trim();
                        if (naziv.Length > 35) naziv = naziv.Substring(0, 35);

                        var izvorStr = pos.TipPostavke switch
                        {
                            TipPostavke.NALOG when !string.IsNullOrEmpty(pos.NalogStevilka) => $"Nalog {pos.NalogStevilka}/{pos.NalogLeto}",
                            TipPostavke.POGODBA when pos.PogodbaStevilka > 0 => $"Pogodba {pos.PogodbaStevilka}/{pos.PogodbaLeto}",
                            _ => ""
                        };

                        sb.AppendLine($"  {pos.Zs,4} {tipStr,-8} {(pos.Artikel ?? ""),-12} {naziv,-35} {kolicina,10:N2} {cena,10:N2} {rabat,6:N1} {vrednost,12:N2}  {izvorStr}");
                    }

                    sb.AppendLine($"  {"",4} {"",8} {"",12} {"",35} {"",10} {"",10} {"",6} {"============",12}");
                    sb.AppendLine($"  {"",4} {"",8} {"",12} {"SKUPAJ VREDNOST:",-35} {"",10} {"",10} {"",6} {skupnaVrednost,12:N2}");
                    sb.AppendLine($"  Število postavk: {postavke.Count}");
                }

                sb.AppendLine();
                sb.AppendLine("================================================================================");
                sb.AppendLine("KONEC IZRAČUNA");
                sb.AppendLine("================================================================================");

                File.WriteAllText(@"c:\vs\obracundb\izracun.txt", sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // V Development okolju ne prekinjaj obračuna zaradi napake pri pisanju datoteke
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

        private ObracunContext NaloziPodatke(ObracunLinqDb db, int mesec, int leto, List<string> log, HashSet<DateTime> prazniki, int? samoPartner = null)
        {
            var prviDan = new DateTime(leto, mesec, 1);
            var zadnjiDan = prviDan.AddMonths(1).AddDays(-1);
            var mesecStr = mesec.ToString("D2");

            // Že fakturirani nalogi (samo od leta 2026 naprej)
            var zeObracunani = db.FaDnNalog.Count(n => n.Leto >= 2026 && n.Fakturirana == 1 && n.Datum >= prviDan && n.Datum <= zadnjiDan);

            // Nalogi za obračun (samo od leta 2026 naprej, brez že fakturiranih)
            // Fakturirana=0: se obračuna, Fakturirana=1: se ignorira, ostalo: se naloži, a ne obračuna
            var nalogiQuery = db.FaDnNalog
                .Where(n => n.Leto >= 2026 && n.Fakturirana != 1 && n.Datum >= prviDan && n.Datum <= zadnjiDan);
            if (samoPartner.HasValue)
                nalogiQuery = nalogiQuery.Where(n => n.Partner == samoPartner.Value);
            var nalogi = nalogiQuery
                .OrderBy(n => n.Partner).ThenBy(n => n.Leto).ThenBy(n => n.Stevilka)
                .ToList();

            // Ustvari manjkajoče OBRACUN_DN zapise
            var ustvarjenihObracunDn = NalogHelper.UstvariManjkajoceObracunDn(db, nalogi);
            if (ustvarjenihObracunDn > 0)
                log.Add($"Ustvarjenih OBRACUN_DN zapisov: {ustvarjenihObracunDn}");

            // Postavke nalogov
            var nalogiKljuci = nalogi.Select(n => (n.Stevilka, n.Leto)).ToHashSet();
            var nalogiStevilke = nalogi.Select(n => n.Stevilka).Distinct().ToList();
            var nalogiLeta = nalogi.Select(n => n.Leto).Distinct().ToList();
            var postavkeNalogov = nalogiStevilke.Count > 0
                ? db.FaDnNalogPoz
                    .Where(pn => nalogiStevilke.Contains(pn.Stevilka) && nalogiLeta.Contains(pn.Leto))
                    .ToList()
                    .Where(pn => nalogiKljuci.Contains((pn.Stevilka, pn.Leto)))
                    .ToList()
                : new List<FaDnNalogPoz>();

            // Aktivne pogodbe
            var pogodbeQuery = db.FaPogodbe
                .Where(p => (p.VeljaDo == null || p.VeljaDo >= prviDan) && (p.PrviRacunOd == null || p.PrviRacunOd <= zadnjiDan));
            if (samoPartner.HasValue)
                pogodbeQuery = pogodbeQuery.Where(p => p.Partner == samoPartner.Value);
            var aktivnePogodbe = pogodbeQuery.ToList();

            // Postavke pogodb
            var postavkePogodbeQuery = 
                from poz in db.FaPogodbePoz
                join ap in db.FaPogodbe on new { poz.Stevilka, poz.Leto } equals new { ap.Stevilka, ap.Leto }
                where (ap.VeljaDo == null || ap.VeljaDo >= prviDan) && (ap.PrviRacunOd == null || ap.PrviRacunOd <= zadnjiDan)
                select new { poz, ap.Partner };
            var postavkePogodb = (samoPartner.HasValue
                ? postavkePogodbeQuery.Where(x => x.Partner == samoPartner.Value).Select(x => x.poz)
                : postavkePogodbeQuery.Select(x => x.poz)
            ).ToList();

            // Ročne postavke
            var rocneQuery = db.ObracunOsnutekPos
                .Where(p => p.Mesec == mesec && p.Leto == leto && p.TipPostavke == TipPostavke.ROCNI);
            if (samoPartner.HasValue)
                rocneQuery = rocneQuery.Where(p => p.Partner == samoPartner.Value);
            var rocnePostavke = rocneQuery.ToList();

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
            // 1. Naloži predračune z datumom od 1.1.2026 naprej do konca tekočega meseca
            var datumOd = new DateTime(2026, 1, 1);
            var datumDo = new DateTime(leto, mesec, 1).AddMonths(1).AddDays(-1);
            var predracuniQuery = db.FaPredracun
                .Where(pr => pr.Datum >= datumOd && pr.Datum <= datumDo);
            if (samoPartner.HasValue)
                predracuniQuery = predracuniQuery.Where(pr => pr.SifraKupca == samoPartner.Value);
            var vsiPredracuni = predracuniQuery.ToList();

            // 2. Naloži vsa plačila za te predračune
            var predracuniLeta = vsiPredracuni.Select(p => p.Leto).Distinct().ToList();
            var placilaPoPredracunih = new Dictionary<(string Stevilka, int Leto), decimal>();

            if (predracuniLeta.Count > 0)
            {
                var sqlPlacila = @"
                    SELECT PREDRACUN_STEVILKA, PREDRACUN_LETO, SUM(ZNESEK + COALESCE(SCONTO, 0)) AS VSOTA
                    FROM FA_RACUN_PLACILO
                    WHERE PREDRACUN_STEVILKA IS NOT NULL 
                      AND PREDRACUN_LETO IS NOT NULL 
                      AND PREDRACUN_LETO IN (" + string.Join(",", predracuniLeta) + @")
                    GROUP BY PREDRACUN_STEVILKA, PREDRACUN_LETO
                    HAVING SUM(ZNESEK + COALESCE(SCONTO, 0)) > 0";

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
            }

            // 3. Filtriraj predračune: stanje=2 ali stanje=5 ALI plačani (vsota plačil > 0)
            var predracuni = vsiPredracuni
                .Where(pr => pr.Stanje == 2 || pr.Stanje == 5 || placilaPoPredracunih.ContainsKey((pr.Stevilka, pr.Leto)))
                .ToList();

            // 4. Plačani predračuni
            var placaniPredracuni = predracuni
                .Where(pr => placilaPoPredracunih.ContainsKey((pr.Stevilka, pr.Leto)))
                .OrderBy(pr => pr.Leto).ThenBy(pr => pr.Stevilka)
                .ToList();

            // Postavke predračunov
            var predracuniKljuci = predracuni.Select(p => (p.Stevilka, p.Leto)).ToHashSet();
            var predracuniStevilke = predracuni.Select(p => p.Stevilka).Distinct().ToList();
            var predracuniLeta2 = predracuni.Select(p => p.Leto).Distinct().ToList();
            var postavkePredracunov = predracuniStevilke.Count > 0
                ? db.FaPredracunKnjizba
                    .Where(pk => predracuniStevilke.Contains(pk.Stevilka) && predracuniLeta2.Contains(pk.Leto))
                    .ToList()
                    .Where(pk => predracuniKljuci.Contains((pk.Stevilka, pk.Leto)))
                    .ToList()
                : new List<FaPredracunKnjizba>();

            // Minute artiklov
            var minuteArtiklov = db.ObracunPaketMinute.ToDictionary(m => m.Artikel, m => m.Minut);


            // OBRACUN_DN slovar
            Dictionary<(string, int), ObracunDn> obracunDnSlovar;
            if (nalogiStevilke.Count == 0)
            {
                obracunDnSlovar = new Dictionary<(string, int), ObracunDn>();
            }
            else if (samoPartner.HasValue)
            {
                // Za enega partnerja: JOIN z FA_DN_NALOG za filtriranje po partnerju
                obracunDnSlovar = (from o in db.ObracunDn
                    join n in db.FaDnNalog on new { o.Stevilka, o.Leto } equals new { n.Stevilka, n.Leto }
                    where n.Partner == samoPartner.Value
                        && n.Leto >= 2026 && n.Fakturirana != 1
                        && n.Datum >= prviDan && n.Datum <= zadnjiDan
                    select o)
                    .ToDictionary(o => (o.Stevilka, o.Leto));
            }
            else
            {
                obracunDnSlovar = db.ObracunDn
                    .Where(o => nalogiStevilke.Contains(o.Stevilka) && nalogiLeta.Contains(o.Leto))
                    .ToList()
                    .Where(o => nalogiKljuci.Contains((o.Stevilka, o.Leto)))
                    .ToDictionary(o => (o.Stevilka, o.Leto));
            }

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

            var terenServisneNastavitve = new ServisneNastavitve
            {
                BrezPogodbeDel7_16 = _parametri.GetString(ObracunParam.Teren_ServisnaBrezPogodbeDel7_16) ?? "",
                BrezPogodbeDel16_22 = _parametri.GetString(ObracunParam.Teren_ServisnaBrezPogodbeDel16_22) ?? "",
                BrezPogodbeDel22_7 = _parametri.GetString(ObracunParam.Teren_ServisnaBrezPogodbeDel22_7) ?? "",
                BrezPogodbeVik7_16 = _parametri.GetString(ObracunParam.Teren_ServisnaBrezPogodbeVik7_16) ?? "",
                BrezPogodbeVik16_22 = _parametri.GetString(ObracunParam.Teren_ServisnaBrezPogodbeVik16_22) ?? "",
                BrezPogodbeVik22_7 = _parametri.GetString(ObracunParam.Teren_ServisnaBrezPogodbeVik22_7) ?? "",
                BrezPogodbeP7_16 = _parametri.GetString(ObracunParam.Teren_ServisnaBrezPogodbeP7_16) ?? "",
                BrezPogodbeP16_22 = _parametri.GetString(ObracunParam.Teren_ServisnaBrezPogodbeP16_22) ?? "",
                BrezPogodbeP22_7 = _parametri.GetString(ObracunParam.Teren_ServisnaBrezPogodbeP22_7) ?? "",
                PogodbaDel7_16 = _parametri.GetString(ObracunParam.Teren_ServisnaPogodbaDel7_16) ?? "",
                PogodbaDel16_22 = _parametri.GetString(ObracunParam.Teren_ServisnaPogodbaDel16_22) ?? "",
                PogodbaDel22_7 = _parametri.GetString(ObracunParam.Teren_ServisnaPogodbaDel22_7) ?? "",
                PogodbaVik7_16 = _parametri.GetString(ObracunParam.Teren_ServisnaPogodbaVik7_16) ?? "",
                PogodbaVik16_22 = _parametri.GetString(ObracunParam.Teren_ServisnaPogodbaVik16_22) ?? "",
                PogodbaVik22_7 = _parametri.GetString(ObracunParam.Teren_ServisnaPogodbaVik22_7) ?? "",
                PogodbaP7_16 = _parametri.GetString(ObracunParam.Teren_ServisnaPogodbaP7_16) ?? "",
                PogodbaP16_22 = _parametri.GetString(ObracunParam.Teren_ServisnaPogodbaP16_22) ?? "",
                PogodbaP22_7 = _parametri.GetString(ObracunParam.Teren_ServisnaPogodbaP22_7) ?? ""
            };

            var popustPogodbe = (decimal)_parametri.GetInt(ObracunParam.ProcentPopustaPogodbe);
            if (popustPogodbe > 0)
                log.Add($"Popust pogodbe: {popustPogodbe}%");

            var tolerancaMinut = _parametri.GetInt(ObracunParam.TolerancaMinut);
            if (tolerancaMinut > 0)
                log.Add($"Toleranca minut: {tolerancaMinut}");

            // PARTNER_MINUTE - naloži minute partnerjev, ki so veljavne za tekoči mesec/leto obračuna
            var partnerMinuteQuery = db.ObracunMinute
                .Where(m => m.ZacetekMesec != null && m.ZacetekLeto != null && m.VeljavnostMesecih > 0);
            if (samoPartner.HasValue)
                partnerMinuteQuery = partnerMinuteQuery.Where(m => m.Partner == samoPartner.Value);
            var partnerMinute = partnerMinuteQuery
                .ToList()
                .Where(m => JeVeljavnaMinuta(m, mesec, leto))
                .ToList();

            // Preberi Že porabljene minute iz OBRACUN_PORABA_MINUT (agregirano po ID_OBRACUN_MINUTE)            // Beremo samo pretekle mesece (mesec/leto strogo manjši od tekočega)
            var zePorabljeneQuery = db.ObracunPorabaMinut
                .Where(p => p.IdObracunMinute != null && p.Tip == TipPorabeMinut.PartnerMinute && (p.Leto < leto || (p.Leto == leto && p.Mesec < mesec)));
            if (samoPartner.HasValue)
                zePorabljeneQuery = zePorabljeneQuery.Where(p => p.Partner == samoPartner.Value);
            var zePorabljenePartnerMinute = zePorabljeneQuery
                .GroupBy(p => p.IdObracunMinute!.Value)
                .Select(g => new { IdObracunMinute = g.Key, SkupajPorabljeno = g.Sum(x => x.Kolicina) })
                .ToDictionary(x => x.IdObracunMinute, x => x.SkupajPorabljeno);

            // Preberi Že porabljene minute iz predračunov (mesec/leto strogo manjši od tekočega)
            var zePorabljenePrQuery = db.ObracunPorabaMinut
                .Where(p => p.PredracunStevilka != null && p.PredracunLeto != null && p.Tip == TipPorabeMinut.Predracun && (p.Leto < leto || (p.Leto == leto && p.Mesec < mesec)));
            if (samoPartner.HasValue)
                zePorabljenePrQuery = zePorabljenePrQuery.Where(p => p.Partner == samoPartner.Value);
            var zePorabljenePredracuni = zePorabljenePrQuery
                .GroupBy(p => new { p.PredracunStevilka, p.PredracunLeto })
                .Select(g => new { g.Key.PredracunStevilka, g.Key.PredracunLeto, SkupajPorabljeno = g.Sum(x => x.Kolicina) })
                .ToDictionary(x => (x.PredracunStevilka!, x.PredracunLeto!.Value), x => x.SkupajPorabljeno);

            // Partnerji s pogodbo v prihodnosti (nimajo aktivne pogodbe, a imajo pogodbo, ki začne veljati po tekočem mesecu)
            var aktivniPartnerji = aktivnePogodbe.Select(p => p.Partner).Distinct().ToHashSet();
            HashSet<int> partnerjiSPrihodnjoPogodbo;
            if (samoPartner.HasValue)
            {
                var imaPrihodnjo = !aktivniPartnerji.Contains(samoPartner.Value) && db.FaPogodbe
                    .Any(p => p.Partner == samoPartner.Value && p.PrviRacunOd != null && p.PrviRacunOd > zadnjiDan
                        && (p.VeljaDo == null || p.VeljaDo > zadnjiDan));
                partnerjiSPrihodnjoPogodbo = imaPrihodnjo ? new HashSet<int> { samoPartner.Value } : new HashSet<int>();
            }
            else
            {
                partnerjiSPrihodnjoPogodbo = db.FaPogodbe
                    .Where(p => p.PrviRacunOd != null && p.PrviRacunOd > zadnjiDan
                        && (p.VeljaDo == null || p.VeljaDo > zadnjiDan))
                    .ToList()
                    .Where(p => !aktivniPartnerji.Contains(p.Partner))
                    .Select(p => p.Partner)
                    .Distinct()
                    .ToHashSet();
            }

            // Povezave nalog → predračuni (iz OBRACUN_DN_PREDRACUN)
            var nalogPredracunPovezave = nalogiStevilke.Count > 0
                ? db.ObracunDnPredracun
                    .Where(p => nalogiStevilke.Contains(p.Stevilka) && nalogiLeta.Contains(p.Leto))
                    .ToList()
                    .Where(p => nalogiKljuci.Contains((p.Stevilka, p.Leto)))
                    .GroupBy(p => (p.Stevilka, p.Leto))
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(p => (p.PredracunStevilka, p.PredracunLeto)).ToHashSet())
                : new Dictionary<(string, int), HashSet<(string, int)>>();

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
                TerenServisneNastavitve = terenServisneNastavitve,
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
                    TipPostavke = TipPostavke.POGODBA,
                    PogodbaStevilka = pogodba.Stevilka,
                    PogodbaLeto = pogodba.Leto
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
            var terenskaRazdelitev = new MinuteRazdelitev(); // Minute iz terenskih nalogov ki se obračunajo (ne koristijo pogodb)
            var obveznoTerenskaRazdelitev = new MinuteRazdelitev(); // Minute iz terenskih ObveznoZaracunaj nalogov
            int minuteObracunane = 0;
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
                var minuteKiSeNeObracunajo = obracunDn?.MinuteKiSeNeObracunajo ?? 0;
                if (minuteKiSeNeObracunajo > minutNaloga)
                    throw new InvalidOperationException($"Nalog {nalog.Stevilka}/{nalog.Leto}: minute, ki se ne obračunajo ({minuteKiSeNeObracunajo}), so večje od trajanja naloga ({minutNaloga}).");

                if (minuteKiSeNeObracunajo > 0 && obracunDn != null && (obracunDn.KajObracunam == KajObracunam.Nic || obracunDn.KajObracunam == KajObracunam.Km))
                    throw new InvalidOperationException($"Nalog {nalog.Stevilka}/{nalog.Leto}: pri 'Nič' ali 'Samo km' ne smejo biti vnesene minute, ki se ne obračunajo.");

                var neobracunanaRazdelitevNaloga = minuteKiSeNeObracunajo > 0
                    ? razdelitevNaloga.VzemiMinut(minuteKiSeNeObracunajo)
                    : new MinuteRazdelitev();
                var obracunanaRazdelitevNaloga = minuteKiSeNeObracunajo > 0
                    ? razdelitevNaloga.Odstej(neobracunanaRazdelitevNaloga)
                    : razdelitevNaloga;
                var minutZaObracun = obracunanaRazdelitevNaloga.SkupajMinut;
                var seObracunajoMinute = nalog.Fakturirana != 1 
                    && obracunDn != null 
                    && (obracunDn.KajObracunam == KajObracunam.KmMin || obracunDn.KajObracunam == KajObracunam.Min || obracunDn.KajObracunam == KajObracunam.ObveznoZaracunaj);
                var jeObveznoZaracunaj = seObracunajoMinute && obracunDn!.KajObracunam == KajObracunam.ObveznoZaracunaj;
                if (seObracunajoMinute)
                {
                    minuteObracunane += minutZaObracun;
                    minuteNeobracunane += neobracunanaRazdelitevNaloga.SkupajMinut;
                    obracunaneRazdelitev.Pristej(obracunanaRazdelitevNaloga);
                    if (jeObveznoZaracunaj)
                        obveznoRazdelitev.Pristej(obracunanaRazdelitevNaloga);

                    // Preveri ali je helpdesk nalog (za konsistentno uporabo minutePogodbe)
                    var jeHelpdesk = nalog.Stevilka.Length == 7 && nalog.Stevilka.StartsWith("1");
                    if (jeHelpdesk)
                        imaHelpdeskNalogeZaObracun = true;
                    else if (jeObveznoZaracunaj)
                        obveznoTerenskaRazdelitev.Pristej(obracunanaRazdelitevNaloga);
                    else
                        terenskaRazdelitev.Pristej(obracunanaRazdelitevNaloga);
                }
                else
                {
                    minuteNeobracunane += minutNaloga;
                }

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
                if (seObracunajoMinute)
                {
                    if (neobracunanaRazdelitevNaloga.SkupajMinut > 0)
                        ZapisiNalogObracun(ctx, nalog, neobracunanaRazdelitevNaloga, 0, imaPogodbo, null, null, trajanje);
                    if (obracunanaRazdelitevNaloga.SkupajMinut > 0)
                        ZapisiNalogObracun(ctx, nalog, obracunanaRazdelitevNaloga, 1, imaPogodbo, jeObveznoZaracunaj ? null : sklad, povezaniPredracuniNaloga, trajanje);
                }
                else
                {
                    ZapisiNalogObracun(ctx, nalog, razdelitevNaloga, 0, imaPogodbo, null, null, trajanje);
                }

                // === Obdelaj kilometrino (samo za naloge z Fakturirana=0) ===
                if (nalog.Fakturirana != 1)
                    ObdelajKilometrino(ctx, nalog, obracunDn, opis, ref naslednjZs, data.Partner, manjkajoceSifre);
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

            // === Za partnerje BREZ POGODBE
            if (!imaPogodbo && minuteObracunane > 0)
            {
                opis.AppendLine();
                opis.AppendLine("--- Obračun servisnih storitev (brez pogodbe) ---");

                if (data.Partner != 23900)
                {
                    // Odštej dobroimetje od delavniških minut
                    // Terenski nalogi NE koristijo dobroimetja iz pogodb (samo predračune, ročno, partner_minute)
                    var kreditiPogodb = imaHelpdeskNalogeZaObracun ? minutePogodbe : 0;
                    var kreditiBrezPogodb = minuteVPlus - kreditiPogodb;

                    // Delavnik - dnevna (7-16)
                    var helpdeskDnevna = obracunaneRazdelitev.Delavnik_Dnevna - obveznoRazdelitev.Delavnik_Dnevna - terenskaRazdelitev.Delavnik_Dnevna;
                    var terenskaDnevna = terenskaRazdelitev.Delavnik_Dnevna;
                    if (kreditiBrezPogodb > 0 && helpdeskDnevna > 0)
                    {
                        var odsteto = Math.Min(helpdeskDnevna, kreditiBrezPogodb);
                        helpdeskDnevna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiPogodb > 0 && helpdeskDnevna > 0)
                    {
                        var odsteto = Math.Min(helpdeskDnevna, kreditiPogodb);
                        helpdeskDnevna -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiBrezPogodb > 0 && terenskaDnevna > 0)
                    {
                        var odsteto = Math.Min(terenskaDnevna, kreditiBrezPogodb);
                        terenskaDnevna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    var delavnikDnevnaZaObracun = helpdeskDnevna + terenskaDnevna;

                    // Delavnik - popoldanska (16-22)
                    var helpdeskPopoldanska = obracunaneRazdelitev.Delavnik_Popoldanska - obveznoRazdelitev.Delavnik_Popoldanska - terenskaRazdelitev.Delavnik_Popoldanska;
                    var terenskaPopoldanska = terenskaRazdelitev.Delavnik_Popoldanska;
                    if (kreditiBrezPogodb > 0 && helpdeskPopoldanska > 0)
                    {
                        var odsteto = Math.Min(helpdeskPopoldanska, kreditiBrezPogodb);
                        helpdeskPopoldanska -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiPogodb > 0 && helpdeskPopoldanska > 0)
                    {
                        var odsteto = Math.Min(helpdeskPopoldanska, kreditiPogodb);
                        helpdeskPopoldanska -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiBrezPogodb > 0 && terenskaPopoldanska > 0)
                    {
                        var odsteto = Math.Min(terenskaPopoldanska, kreditiBrezPogodb);
                        terenskaPopoldanska -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    var delavnikPopoldanskaZaObracun = helpdeskPopoldanska + terenskaPopoldanska;

                    // Delavnik - nočna (22-7)
                    var helpdeskNocna = obracunaneRazdelitev.Delavnik_Nocna - obveznoRazdelitev.Delavnik_Nocna - terenskaRazdelitev.Delavnik_Nocna;
                    var terenskaNocna = terenskaRazdelitev.Delavnik_Nocna;
                    if (kreditiBrezPogodb > 0 && helpdeskNocna > 0)
                    {
                        var odsteto = Math.Min(helpdeskNocna, kreditiBrezPogodb);
                        helpdeskNocna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiPogodb > 0 && helpdeskNocna > 0)
                    {
                        var odsteto = Math.Min(helpdeskNocna, kreditiPogodb);
                        helpdeskNocna -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiBrezPogodb > 0 && terenskaNocna > 0)
                    {
                        var odsteto = Math.Min(terenskaNocna, kreditiBrezPogodb);
                        terenskaNocna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    var delavnikNocnaZaObracun = helpdeskNocna + terenskaNocna;

                    // Prištej obvezne minute nazaj (ObveznoZaracunaj - ne gredo skozi dobroimetje)
                    delavnikDnevnaZaObracun += obveznoRazdelitev.Delavnik_Dnevna;
                    delavnikPopoldanskaZaObracun += obveznoRazdelitev.Delavnik_Popoldanska;
                    delavnikNocnaZaObracun += obveznoRazdelitev.Delavnik_Nocna;

                    // Vikend - dnevna (7-16)
                    var helpdeskVikDnevna = obracunaneRazdelitev.Vikend_Dnevna - obveznoRazdelitev.Vikend_Dnevna - terenskaRazdelitev.Vikend_Dnevna;
                    var terenskaVikDnevna = terenskaRazdelitev.Vikend_Dnevna;
                    if (kreditiBrezPogodb > 0 && helpdeskVikDnevna > 0)
                    {
                        var odsteto = Math.Min(helpdeskVikDnevna, kreditiBrezPogodb);
                        helpdeskVikDnevna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiPogodb > 0 && helpdeskVikDnevna > 0)
                    {
                        var odsteto = Math.Min(helpdeskVikDnevna, kreditiPogodb);
                        helpdeskVikDnevna -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiBrezPogodb > 0 && terenskaVikDnevna > 0)
                    {
                        var odsteto = Math.Min(terenskaVikDnevna, kreditiBrezPogodb);
                        terenskaVikDnevna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    var vikendDnevnaZaObracun = helpdeskVikDnevna + terenskaVikDnevna + obveznoRazdelitev.Vikend_Dnevna;

                    // Vikend - popoldanska (16-22)
                    var helpdeskVikPopoldanska = obracunaneRazdelitev.Vikend_Popoldanska - obveznoRazdelitev.Vikend_Popoldanska - terenskaRazdelitev.Vikend_Popoldanska;
                    var terenskaVikPopoldanska = terenskaRazdelitev.Vikend_Popoldanska;
                    if (kreditiBrezPogodb > 0 && helpdeskVikPopoldanska > 0)
                    {
                        var odsteto = Math.Min(helpdeskVikPopoldanska, kreditiBrezPogodb);
                        helpdeskVikPopoldanska -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiPogodb > 0 && helpdeskVikPopoldanska > 0)
                    {
                        var odsteto = Math.Min(helpdeskVikPopoldanska, kreditiPogodb);
                        helpdeskVikPopoldanska -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiBrezPogodb > 0 && terenskaVikPopoldanska > 0)
                    {
                        var odsteto = Math.Min(terenskaVikPopoldanska, kreditiBrezPogodb);
                        terenskaVikPopoldanska -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    var vikendPopoldanskaZaObracun = helpdeskVikPopoldanska + terenskaVikPopoldanska + obveznoRazdelitev.Vikend_Popoldanska;

                    // Vikend - nočna (22-7)
                    var helpdeskVikNocna = obracunaneRazdelitev.Vikend_Nocna - obveznoRazdelitev.Vikend_Nocna - terenskaRazdelitev.Vikend_Nocna;
                    var terenskaVikNocna = terenskaRazdelitev.Vikend_Nocna;
                    if (kreditiBrezPogodb > 0 && helpdeskVikNocna > 0)
                    {
                        var odsteto = Math.Min(helpdeskVikNocna, kreditiBrezPogodb);
                        helpdeskVikNocna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiPogodb > 0 && helpdeskVikNocna > 0)
                    {
                        var odsteto = Math.Min(helpdeskVikNocna, kreditiPogodb);
                        helpdeskVikNocna -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiBrezPogodb > 0 && terenskaVikNocna > 0)
                    {
                        var odsteto = Math.Min(terenskaVikNocna, kreditiBrezPogodb);
                        terenskaVikNocna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    var vikendNocnaZaObracun = helpdeskVikNocna + terenskaVikNocna + obveznoRazdelitev.Vikend_Nocna;

                    // Praznik - dnevna (7-16)
                    var helpdeskPraDnevna = obracunaneRazdelitev.Praznik_Dnevna - obveznoRazdelitev.Praznik_Dnevna - terenskaRazdelitev.Praznik_Dnevna;
                    var terenskaPraDnevna = terenskaRazdelitev.Praznik_Dnevna;
                    if (kreditiBrezPogodb > 0 && helpdeskPraDnevna > 0)
                    {
                        var odsteto = Math.Min(helpdeskPraDnevna, kreditiBrezPogodb);
                        helpdeskPraDnevna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiPogodb > 0 && helpdeskPraDnevna > 0)
                    {
                        var odsteto = Math.Min(helpdeskPraDnevna, kreditiPogodb);
                        helpdeskPraDnevna -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiBrezPogodb > 0 && terenskaPraDnevna > 0)
                    {
                        var odsteto = Math.Min(terenskaPraDnevna, kreditiBrezPogodb);
                        terenskaPraDnevna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    var praznikDnevnaZaObracun = helpdeskPraDnevna + terenskaPraDnevna + obveznoRazdelitev.Praznik_Dnevna;

                    // Praznik - popoldanska (16-22)
                    var helpdeskPraPopoldanska = obracunaneRazdelitev.Praznik_Popoldanska - obveznoRazdelitev.Praznik_Popoldanska - terenskaRazdelitev.Praznik_Popoldanska;
                    var terenskaPraPopoldanska = terenskaRazdelitev.Praznik_Popoldanska;
                    if (kreditiBrezPogodb > 0 && helpdeskPraPopoldanska > 0)
                    {
                        var odsteto = Math.Min(helpdeskPraPopoldanska, kreditiBrezPogodb);
                        helpdeskPraPopoldanska -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiPogodb > 0 && helpdeskPraPopoldanska > 0)
                    {
                        var odsteto = Math.Min(helpdeskPraPopoldanska, kreditiPogodb);
                        helpdeskPraPopoldanska -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiBrezPogodb > 0 && terenskaPraPopoldanska > 0)
                    {
                        var odsteto = Math.Min(terenskaPraPopoldanska, kreditiBrezPogodb);
                        terenskaPraPopoldanska -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    var praznikPopoldanskaZaObracun = helpdeskPraPopoldanska + terenskaPraPopoldanska + obveznoRazdelitev.Praznik_Popoldanska;

                    // Praznik - nočna (22-7)
                    var helpdeskPraNocna = obracunaneRazdelitev.Praznik_Nocna - obveznoRazdelitev.Praznik_Nocna - terenskaRazdelitev.Praznik_Nocna;
                    var terenskaPraNocna = terenskaRazdelitev.Praznik_Nocna;
                    if (kreditiBrezPogodb > 0 && helpdeskPraNocna > 0)
                    {
                        var odsteto = Math.Min(helpdeskPraNocna, kreditiBrezPogodb);
                        helpdeskPraNocna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiPogodb > 0 && helpdeskPraNocna > 0)
                    {
                        var odsteto = Math.Min(helpdeskPraNocna, kreditiPogodb);
                        helpdeskPraNocna -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    if (kreditiBrezPogodb > 0 && terenskaPraNocna > 0)
                    {
                        var odsteto = Math.Min(terenskaPraNocna, kreditiBrezPogodb);
                        terenskaPraNocna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                    }
                    // HD skupne minute: helpdesk + obvezno helpdesk
                    var hdDel7_16 = helpdeskDnevna + obveznoRazdelitev.Delavnik_Dnevna - obveznoTerenskaRazdelitev.Delavnik_Dnevna;
                    var hdDel16_22 = helpdeskPopoldanska + obveznoRazdelitev.Delavnik_Popoldanska - obveznoTerenskaRazdelitev.Delavnik_Popoldanska;
                    var hdDel22_7 = helpdeskNocna + obveznoRazdelitev.Delavnik_Nocna - obveznoTerenskaRazdelitev.Delavnik_Nocna;
                    var hdVik7_16 = helpdeskVikDnevna + obveznoRazdelitev.Vikend_Dnevna - obveznoTerenskaRazdelitev.Vikend_Dnevna;
                    var hdVik16_22 = helpdeskVikPopoldanska + obveznoRazdelitev.Vikend_Popoldanska - obveznoTerenskaRazdelitev.Vikend_Popoldanska;
                    var hdVik22_7 = helpdeskVikNocna + obveznoRazdelitev.Vikend_Nocna - obveznoTerenskaRazdelitev.Vikend_Nocna;
                    var hdPra7_16 = helpdeskPraDnevna + obveznoRazdelitev.Praznik_Dnevna - obveznoTerenskaRazdelitev.Praznik_Dnevna;
                    var hdPra16_22 = helpdeskPraPopoldanska + obveznoRazdelitev.Praznik_Popoldanska - obveznoTerenskaRazdelitev.Praznik_Popoldanska;
                    var hdPra22_7 = helpdeskPraNocna + obveznoRazdelitev.Praznik_Nocna - obveznoTerenskaRazdelitev.Praznik_Nocna;

                    // Teren skupne minute: terenski + obvezno terenski
                    var terenDel7_16 = terenskaDnevna + obveznoTerenskaRazdelitev.Delavnik_Dnevna;
                    var terenDel16_22 = terenskaPopoldanska + obveznoTerenskaRazdelitev.Delavnik_Popoldanska;
                    var terenDel22_7 = terenskaNocna + obveznoTerenskaRazdelitev.Delavnik_Nocna;
                    var terenVik7_16 = terenskaVikDnevna + obveznoTerenskaRazdelitev.Vikend_Dnevna;
                    var terenVik16_22 = terenskaVikPopoldanska + obveznoTerenskaRazdelitev.Vikend_Popoldanska;
                    var terenVik22_7 = terenskaVikNocna + obveznoTerenskaRazdelitev.Vikend_Nocna;
                    var terenPra7_16 = terenskaPraDnevna + obveznoTerenskaRazdelitev.Praznik_Dnevna;
                    var terenPra16_22 = terenskaPraPopoldanska + obveznoTerenskaRazdelitev.Praznik_Popoldanska;
                    var terenPra22_7 = terenskaPraNocna + obveznoTerenskaRazdelitev.Praznik_Nocna;

                    // Uporabi toleranco minut - HD
                    var (tHdDel7_16, tHdDel16_22, tHdDel22_7, tHdVik7_16, tHdVik16_22, tHdVik22_7, tHdPra7_16, tHdPra16_22, tHdPra22_7) = 
                        UpostevajiToleranco(ctx.TolerancaMinut,
                            hdDel7_16, hdDel16_22, hdDel22_7,
                            hdVik7_16, hdVik16_22, hdVik22_7,
                            hdPra7_16, hdPra16_22, hdPra22_7,
                            opis, data.Partner, ctx.Log);

                    // Uporabi toleranco minut - Teren
                    var (tTerenDel7_16, tTerenDel16_22, tTerenDel22_7, tTerenVik7_16, tTerenVik16_22, tTerenVik22_7, tTerenPra7_16, tTerenPra16_22, tTerenPra22_7) = 
                        UpostevajiToleranco(ctx.TolerancaMinut,
                            terenDel7_16, terenDel16_22, terenDel22_7,
                            terenVik7_16, terenVik16_22, terenVik22_7,
                            terenPra7_16, terenPra16_22, terenPra22_7,
                            opis, data.Partner, ctx.Log);

                    if (data.Partner == 428000)
                    {
                        opis.AppendLine($"   DEBUG (brez pogodbe): TolerancaMinut={ctx.TolerancaMinut}, kreditiBrezPogodb={kreditiBrezPogodb}, kreditiPogodb={kreditiPogodb}");
                        opis.AppendLine($"   DEBUG HD pred toleranco:  Del 7-16={hdDel7_16}, 16-22={hdDel16_22}, 22-7={hdDel22_7} | Vik={hdVik7_16}/{hdVik16_22}/{hdVik22_7} | Pra={hdPra7_16}/{hdPra16_22}/{hdPra22_7}");
                        opis.AppendLine($"   DEBUG HD po toleranci:    Del 7-16={tHdDel7_16}, 16-22={tHdDel16_22}, 22-7={tHdDel22_7} | Vik={tHdVik7_16}/{tHdVik16_22}/{tHdVik22_7} | Pra={tHdPra7_16}/{tHdPra16_22}/{tHdPra22_7}");
                        opis.AppendLine($"   DEBUG Teren pred tol:     Del 7-16={terenDel7_16}, 16-22={terenDel16_22}, 22-7={terenDel22_7} | Vik={terenVik7_16}/{terenVik16_22}/{terenVik22_7} | Pra={terenPra7_16}/{terenPra16_22}/{terenPra22_7}");
                        opis.AppendLine($"   DEBUG Teren po tol:       Del 7-16={tTerenDel7_16}, 16-22={tTerenDel16_22}, 22-7={tTerenDel22_7} | Vik={tTerenVik7_16}/{tTerenVik16_22}/{tTerenVik22_7} | Pra={tTerenPra7_16}/{tTerenPra16_22}/{tTerenPra22_7}");
                        opis.AppendLine($"   DEBUG ŠIFRE HD:    Del 7-16='{ctx.ServisneNastavitve.GetSifraBrezPogodbe(TipDneva.Delavnik, CasovnaTarifa.Dnevna)}', 16-22='{ctx.ServisneNastavitve.GetSifraBrezPogodbe(TipDneva.Delavnik, CasovnaTarifa.Popoldanska)}', 22-7='{ctx.ServisneNastavitve.GetSifraBrezPogodbe(TipDneva.Delavnik, CasovnaTarifa.Nocna)}'");
                        opis.AppendLine($"   DEBUG ŠIFRE Teren: Del 7-16='{ctx.TerenServisneNastavitve.GetSifraBrezPogodbe(TipDneva.Delavnik, CasovnaTarifa.Dnevna)}', 16-22='{ctx.TerenServisneNastavitve.GetSifraBrezPogodbe(TipDneva.Delavnik, CasovnaTarifa.Popoldanska)}', 22-7='{ctx.TerenServisneNastavitve.GetSifraBrezPogodbe(TipDneva.Delavnik, CasovnaTarifa.Nocna)}'");
                    }

                    // HD postavke (HD Servisna šifre artiklov)
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Dnevna, tHdDel7_16, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Popoldanska, tHdDel16_22, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Nocna, tHdDel22_7, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);

                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Dnevna, tHdVik7_16, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Popoldanska, tHdVik16_22, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Nocna, tHdVik22_7, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);

                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Dnevna, tHdPra7_16, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Popoldanska, tHdPra16_22, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Nocna, tHdPra22_7, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);

                    // Teren postavke (Teren Servisna šifre artiklov)
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Dnevna, tTerenDel7_16, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve, "Teren");
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Popoldanska, tTerenDel16_22, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve, "Teren");
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Nocna, tTerenDel22_7, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve, "Teren");

                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Dnevna, tTerenVik7_16, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve, "Teren");
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Popoldanska, tTerenVik16_22, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve, "Teren");
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Nocna, tTerenVik22_7, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve, "Teren");

                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Dnevna, tTerenPra7_16, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve, "Teren");
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Popoldanska, tTerenPra16_22, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve, "Teren");
                    UstvariPostavkoBrezPogodbe(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Nocna, tTerenPra22_7, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve, "Teren");
                }
                else
                {
                    opis.AppendLine($"   ! PRESKOČENO: partner == 23900 (interni) – ne ustvari servisnih postavk");
                }
            }

            // === Za partnerje S POGODBO ustvari postavke za vse minute (delavnik, vikend, praznik) ===
            if (imaPogodbo && minuteObracunane > 0)
            {
                opis.AppendLine();
                opis.AppendLine($"--- Obračun servisnih storitev (pogodba, popust {ctx.PopustPogodbe}%) ---");

                // Odštej dobroimetje od delavniških minut
                // Terenski nalogi NE koristijo dobroimetja iz pogodb (samo predračune, ročno, partner_minute)
                var kreditiPogodb = imaHelpdeskNalogeZaObracun ? minutePogodbe : 0;
                var kreditiBrezPogodb = minuteVPlus - kreditiPogodb;

                // Delavnik - dnevna (7-16)
                var helpdeskDnevna = obracunaneRazdelitev.Delavnik_Dnevna - obveznoRazdelitev.Delavnik_Dnevna - terenskaRazdelitev.Delavnik_Dnevna;
                var terenskaDnevna = terenskaRazdelitev.Delavnik_Dnevna;
                if (kreditiBrezPogodb > 0 && helpdeskDnevna > 0)
                {
                    var odsteto = Math.Min(helpdeskDnevna, kreditiBrezPogodb);
                    helpdeskDnevna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiPogodb > 0 && helpdeskDnevna > 0)
                {
                    var odsteto = Math.Min(helpdeskDnevna, kreditiPogodb);
                    helpdeskDnevna -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiBrezPogodb > 0 && terenskaDnevna > 0)
                {
                    var odsteto = Math.Min(terenskaDnevna, kreditiBrezPogodb);
                    terenskaDnevna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                var delavnikDnevnaZaObracun = helpdeskDnevna + terenskaDnevna;

                // Delavnik - popoldanska (16-22)
                var helpdeskPopoldanska = obracunaneRazdelitev.Delavnik_Popoldanska - obveznoRazdelitev.Delavnik_Popoldanska - terenskaRazdelitev.Delavnik_Popoldanska;
                var terenskaPopoldanska = terenskaRazdelitev.Delavnik_Popoldanska;
                if (kreditiBrezPogodb > 0 && helpdeskPopoldanska > 0)
                {
                    var odsteto = Math.Min(helpdeskPopoldanska, kreditiBrezPogodb);
                    helpdeskPopoldanska -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiPogodb > 0 && helpdeskPopoldanska > 0)
                {
                    var odsteto = Math.Min(helpdeskPopoldanska, kreditiPogodb);
                    helpdeskPopoldanska -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiBrezPogodb > 0 && terenskaPopoldanska > 0)
                {
                    var odsteto = Math.Min(terenskaPopoldanska, kreditiBrezPogodb);
                    terenskaPopoldanska -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                var delavnikPopoldanskaZaObracun = helpdeskPopoldanska + terenskaPopoldanska;

                // Delavnik - nočna (22-7)
                var helpdeskNocna = obracunaneRazdelitev.Delavnik_Nocna - obveznoRazdelitev.Delavnik_Nocna - terenskaRazdelitev.Delavnik_Nocna;
                var terenskaNocna = terenskaRazdelitev.Delavnik_Nocna;
                if (kreditiBrezPogodb > 0 && helpdeskNocna > 0)
                {
                    var odsteto = Math.Min(helpdeskNocna, kreditiBrezPogodb);
                    helpdeskNocna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiPogodb > 0 && helpdeskNocna > 0)
                {
                    var odsteto = Math.Min(helpdeskNocna, kreditiPogodb);
                    helpdeskNocna -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiBrezPogodb > 0 && terenskaNocna > 0)
                {
                    var odsteto = Math.Min(terenskaNocna, kreditiBrezPogodb);
                    terenskaNocna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                var delavnikNocnaZaObracun = helpdeskNocna + terenskaNocna;

                // Prištej obvezne minute nazaj (ObveznoZaracunaj - ne gredo skozi dobroimetje)
                delavnikDnevnaZaObracun += obveznoRazdelitev.Delavnik_Dnevna;
                delavnikPopoldanskaZaObracun += obveznoRazdelitev.Delavnik_Popoldanska;
                delavnikNocnaZaObracun += obveznoRazdelitev.Delavnik_Nocna;

                // Vikend - dnevna (7-16)
                var helpdeskVikDnevna = obracunaneRazdelitev.Vikend_Dnevna - obveznoRazdelitev.Vikend_Dnevna - terenskaRazdelitev.Vikend_Dnevna;
                var terenskaVikDnevna = terenskaRazdelitev.Vikend_Dnevna;
                if (kreditiBrezPogodb > 0 && helpdeskVikDnevna > 0)
                {
                    var odsteto = Math.Min(helpdeskVikDnevna, kreditiBrezPogodb);
                    helpdeskVikDnevna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiPogodb > 0 && helpdeskVikDnevna > 0)
                {
                    var odsteto = Math.Min(helpdeskVikDnevna, kreditiPogodb);
                    helpdeskVikDnevna -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiBrezPogodb > 0 && terenskaVikDnevna > 0)
                {
                    var odsteto = Math.Min(terenskaVikDnevna, kreditiBrezPogodb);
                    terenskaVikDnevna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                var vikendDnevnaZaObracun = helpdeskVikDnevna + terenskaVikDnevna + obveznoRazdelitev.Vikend_Dnevna;

                // Vikend - popoldanska (16-22)
                var helpdeskVikPopoldanska = obracunaneRazdelitev.Vikend_Popoldanska - obveznoRazdelitev.Vikend_Popoldanska - terenskaRazdelitev.Vikend_Popoldanska;
                var terenskaVikPopoldanska = terenskaRazdelitev.Vikend_Popoldanska;
                if (kreditiBrezPogodb > 0 && helpdeskVikPopoldanska > 0)
                {
                    var odsteto = Math.Min(helpdeskVikPopoldanska, kreditiBrezPogodb);
                    helpdeskVikPopoldanska -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiPogodb > 0 && helpdeskVikPopoldanska > 0)
                {
                    var odsteto = Math.Min(helpdeskVikPopoldanska, kreditiPogodb);
                    helpdeskVikPopoldanska -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiBrezPogodb > 0 && terenskaVikPopoldanska > 0)
                {
                    var odsteto = Math.Min(terenskaVikPopoldanska, kreditiBrezPogodb);
                    terenskaVikPopoldanska -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                var vikendPopoldanskaZaObracun = helpdeskVikPopoldanska + terenskaVikPopoldanska + obveznoRazdelitev.Vikend_Popoldanska;

                // Vikend - nočna (22-7)
                var helpdeskVikNocna = obracunaneRazdelitev.Vikend_Nocna - obveznoRazdelitev.Vikend_Nocna - terenskaRazdelitev.Vikend_Nocna;
                var terenskaVikNocna = terenskaRazdelitev.Vikend_Nocna;
                if (kreditiBrezPogodb > 0 && helpdeskVikNocna > 0)
                {
                    var odsteto = Math.Min(helpdeskVikNocna, kreditiBrezPogodb);
                    helpdeskVikNocna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiPogodb > 0 && helpdeskVikNocna > 0)
                {
                    var odsteto = Math.Min(helpdeskVikNocna, kreditiPogodb);
                    helpdeskVikNocna -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiBrezPogodb > 0 && terenskaVikNocna > 0)
                {
                    var odsteto = Math.Min(terenskaVikNocna, kreditiBrezPogodb);
                    terenskaVikNocna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                var vikendNocnaZaObracun = helpdeskVikNocna + terenskaVikNocna + obveznoRazdelitev.Vikend_Nocna;

                // Praznik - dnevna (7-16)
                var helpdeskPraDnevna = obracunaneRazdelitev.Praznik_Dnevna - obveznoRazdelitev.Praznik_Dnevna - terenskaRazdelitev.Praznik_Dnevna;
                var terenskaPraDnevna = terenskaRazdelitev.Praznik_Dnevna;
                if (kreditiBrezPogodb > 0 && helpdeskPraDnevna > 0)
                {
                    var odsteto = Math.Min(helpdeskPraDnevna, kreditiBrezPogodb);
                    helpdeskPraDnevna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiPogodb > 0 && helpdeskPraDnevna > 0)
                {
                    var odsteto = Math.Min(helpdeskPraDnevna, kreditiPogodb);
                    helpdeskPraDnevna -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiBrezPogodb > 0 && terenskaPraDnevna > 0)
                {
                    var odsteto = Math.Min(terenskaPraDnevna, kreditiBrezPogodb);
                    terenskaPraDnevna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                var praznikDnevnaZaObracun = helpdeskPraDnevna + terenskaPraDnevna + obveznoRazdelitev.Praznik_Dnevna;

                // Praznik - popoldanska (16-22)
                var helpdeskPraPopoldanska = obracunaneRazdelitev.Praznik_Popoldanska - obveznoRazdelitev.Praznik_Popoldanska - terenskaRazdelitev.Praznik_Popoldanska;
                var terenskaPraPopoldanska = terenskaRazdelitev.Praznik_Popoldanska;
                if (kreditiBrezPogodb > 0 && helpdeskPraPopoldanska > 0)
                {
                    var odsteto = Math.Min(helpdeskPraPopoldanska, kreditiBrezPogodb);
                    helpdeskPraPopoldanska -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiPogodb > 0 && helpdeskPraPopoldanska > 0)
                {
                    var odsteto = Math.Min(helpdeskPraPopoldanska, kreditiPogodb);
                    helpdeskPraPopoldanska -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiBrezPogodb > 0 && terenskaPraPopoldanska > 0)
                {
                    var odsteto = Math.Min(terenskaPraPopoldanska, kreditiBrezPogodb);
                    terenskaPraPopoldanska -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                var praznikPopoldanskaZaObracun = helpdeskPraPopoldanska + terenskaPraPopoldanska + obveznoRazdelitev.Praznik_Popoldanska;

                // Praznik - nočna (22-7)
                var helpdeskPraNocna = obracunaneRazdelitev.Praznik_Nocna - obveznoRazdelitev.Praznik_Nocna - terenskaRazdelitev.Praznik_Nocna;
                var terenskaPraNocna = terenskaRazdelitev.Praznik_Nocna;
                if (kreditiBrezPogodb > 0 && helpdeskPraNocna > 0)
                {
                    var odsteto = Math.Min(helpdeskPraNocna, kreditiBrezPogodb);
                    helpdeskPraNocna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiPogodb > 0 && helpdeskPraNocna > 0)
                {
                    var odsteto = Math.Min(helpdeskPraNocna, kreditiPogodb);
                    helpdeskPraNocna -= odsteto; kreditiPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                if (kreditiBrezPogodb > 0 && terenskaPraNocna > 0)
                {
                    var odsteto = Math.Min(terenskaPraNocna, kreditiBrezPogodb);
                    terenskaPraNocna -= odsteto; kreditiBrezPogodb -= odsteto; minuteKoriscene += odsteto;
                }
                // HD skupne minute: helpdesk + obvezno helpdesk
                var hdDel7_16 = helpdeskDnevna + obveznoRazdelitev.Delavnik_Dnevna - obveznoTerenskaRazdelitev.Delavnik_Dnevna;
                var hdDel16_22 = helpdeskPopoldanska + obveznoRazdelitev.Delavnik_Popoldanska - obveznoTerenskaRazdelitev.Delavnik_Popoldanska;
                var hdDel22_7 = helpdeskNocna + obveznoRazdelitev.Delavnik_Nocna - obveznoTerenskaRazdelitev.Delavnik_Nocna;
                var hdVik7_16 = helpdeskVikDnevna + obveznoRazdelitev.Vikend_Dnevna - obveznoTerenskaRazdelitev.Vikend_Dnevna;
                var hdVik16_22 = helpdeskVikPopoldanska + obveznoRazdelitev.Vikend_Popoldanska - obveznoTerenskaRazdelitev.Vikend_Popoldanska;
                var hdVik22_7 = helpdeskVikNocna + obveznoRazdelitev.Vikend_Nocna - obveznoTerenskaRazdelitev.Vikend_Nocna;
                var hdPra7_16 = helpdeskPraDnevna + obveznoRazdelitev.Praznik_Dnevna - obveznoTerenskaRazdelitev.Praznik_Dnevna;
                var hdPra16_22 = helpdeskPraPopoldanska + obveznoRazdelitev.Praznik_Popoldanska - obveznoTerenskaRazdelitev.Praznik_Popoldanska;
                var hdPra22_7 = helpdeskPraNocna + obveznoRazdelitev.Praznik_Nocna - obveznoTerenskaRazdelitev.Praznik_Nocna;

                // Teren skupne minute: terenski + obvezno terenski
                var terenDel7_16 = terenskaDnevna + obveznoTerenskaRazdelitev.Delavnik_Dnevna;
                var terenDel16_22 = terenskaPopoldanska + obveznoTerenskaRazdelitev.Delavnik_Popoldanska;
                var terenDel22_7 = terenskaNocna + obveznoTerenskaRazdelitev.Delavnik_Nocna;
                var terenVik7_16 = terenskaVikDnevna + obveznoTerenskaRazdelitev.Vikend_Dnevna;
                var terenVik16_22 = terenskaVikPopoldanska + obveznoTerenskaRazdelitev.Vikend_Popoldanska;
                var terenVik22_7 = terenskaVikNocna + obveznoTerenskaRazdelitev.Vikend_Nocna;
                var terenPra7_16 = terenskaPraDnevna + obveznoTerenskaRazdelitev.Praznik_Dnevna;
                var terenPra16_22 = terenskaPraPopoldanska + obveznoTerenskaRazdelitev.Praznik_Popoldanska;
                var terenPra22_7 = terenskaPraNocna + obveznoTerenskaRazdelitev.Praznik_Nocna;

                // Uporabi toleranco minut - HD
                var (tHdDel7_16, tHdDel16_22, tHdDel22_7, tHdVik7_16, tHdVik16_22, tHdVik22_7, tHdPra7_16, tHdPra16_22, tHdPra22_7) = 
                    UpostevajiToleranco(ctx.TolerancaMinut,
                        hdDel7_16, hdDel16_22, hdDel22_7,
                        hdVik7_16, hdVik16_22, hdVik22_7,
                        hdPra7_16, hdPra16_22, hdPra22_7,
                        opis, data.Partner, ctx.Log);

                // Uporabi toleranco minut - Teren
                var (tTerenDel7_16, tTerenDel16_22, tTerenDel22_7, tTerenVik7_16, tTerenVik16_22, tTerenVik22_7, tTerenPra7_16, tTerenPra16_22, tTerenPra22_7) = 
                    UpostevajiToleranco(ctx.TolerancaMinut,
                        terenDel7_16, terenDel16_22, terenDel22_7,
                        terenVik7_16, terenVik16_22, terenVik22_7,
                        terenPra7_16, terenPra16_22, terenPra22_7,
                        opis, data.Partner, ctx.Log);

                // HD postavke (HD Servisna šifre artiklov)
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Dnevna, tHdDel7_16, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Popoldanska, tHdDel16_22, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Nocna, tHdDel22_7, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);

                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Dnevna, tHdVik7_16, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Popoldanska, tHdVik16_22, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Nocna, tHdVik22_7, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);

                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Dnevna, tHdPra7_16, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Popoldanska, tHdPra16_22, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Nocna, tHdPra22_7, opis, ref naslednjZs, manjkajoceSifre, ctx.ServisneNastavitve);

                // Teren postavke (Teren Servisna šifre artiklov)
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Dnevna, tTerenDel7_16, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Popoldanska, tTerenDel16_22, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Delavnik, CasovnaTarifa.Nocna, tTerenDel22_7, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve);

                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Dnevna, tTerenVik7_16, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Popoldanska, tTerenVik16_22, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Vikend, CasovnaTarifa.Nocna, tTerenVik22_7, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve);

                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Dnevna, tTerenPra7_16, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Popoldanska, tTerenPra16_22, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve);
                UstvariPostavkoPogodba(ctx, data.Partner, TipDneva.Praznik, CasovnaTarifa.Nocna, tTerenPra22_7, opis, ref naslednjZs, manjkajoceSifre, ctx.TerenServisneNastavitve);
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
        private static void UstvariPostavkoBrezPogodbe(ObracunContext ctx, int partner, TipDneva tipDneva, CasovnaTarifa tarifa, int minute, StringBuilder opis, ref int naslednjZs, HashSet<string> manjkajoceSifre, ServisneNastavitve nastavitve, string tipNastavitev = "Servis")
        {
            if (minute <= 0)
                return;

            // Pridobi šifro artikla za to obdobje
            var sifra = nastavitve.GetSifraBrezPogodbe(tipDneva, tarifa);
            if (string.IsNullOrEmpty(sifra))
            {
                var obdobje = DoloObdobjeNaziv(tipDneva, tarifa);
                manjkajoceSifre.Add($"Brez pogodbe, {obdobje}");
                opis.AppendLine($"   ! PRESKOČENO ({tipNastavitev}, {obdobje}, {minute} min): manjka šifra v nastavitvah 'Brez pogodbe'");
                return;
            }

            UstvariServisnoPostavko(ctx, partner, tipDneva, tarifa, minute, sifra, 0, opis, ref naslednjZs, manjkajoceSifre, tipNastavitev);
        }

        /// <summary>
        /// Ustvari postavko za obračun servisnih storitev s pogodbo.
        /// </summary>
        private static void UstvariPostavkoPogodba(ObracunContext ctx, int partner, TipDneva tipDneva, CasovnaTarifa tarifa, int minute, StringBuilder opis, ref int naslednjZs, HashSet<string> manjkajoceSifre, ServisneNastavitve nastavitve)
        {
            if (minute <= 0)
                return;

            // Pridobi šifro artikla za to obdobje
            var sifra = nastavitve.GetSifraPogodba(tipDneva, tarifa);
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
                opis.AppendLine($"   ! PRESKOČENO ({nazivPrefix} {DoloObdobjeNaziv(tipDneva, tarifa)}, {minute} min): artikel '{sifra}' ne obstaja v šifrantu");
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

            opis.AppendLine($"   - {sifraStr} {nazivStr} {enotaStr} {kolicinaStr} {cenaStr} {rabatStr} {vrednostStr}  ({opisEnote})");

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
        /// Zapiše razdelitev minut naloga
        /// </summary>
        private static void ZapisiNalogObracun(ObracunContext ctx, FaDnNalog nalog, MinuteRazdelitev razdelitev, int obracunam, bool imaPogodbo, MinutniSklad? sklad, HashSet<(string PredStevilka, int PredLeto)>? povezaniPredracuni, int trajanjeNaloga)
        {
            // Za vsako kategorijo minut zapiši zapis (samo delavniškie minute se odštevajo)
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Delavnik, CasovnaTarifa.Dnevna, razdelitev.Delavnik_Dnevna, obracunam, imaPogodbo, sklad, povezaniPredracuni, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Delavnik, CasovnaTarifa.Popoldanska, razdelitev.Delavnik_Popoldanska, obracunam, imaPogodbo, sklad, povezaniPredracuni, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Delavnik, CasovnaTarifa.Nocna, razdelitev.Delavnik_Nocna, obracunam, imaPogodbo, sklad, povezaniPredracuni, trajanjeNaloga);

            // Vikend in praznik
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Vikend, CasovnaTarifa.Dnevna, razdelitev.Vikend_Dnevna, obracunam, imaPogodbo, sklad, povezaniPredracuni, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Vikend, CasovnaTarifa.Popoldanska, razdelitev.Vikend_Popoldanska, obracunam, imaPogodbo, sklad, povezaniPredracuni, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Vikend, CasovnaTarifa.Nocna, razdelitev.Vikend_Nocna, obracunam, imaPogodbo, sklad, povezaniPredracuni, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Praznik, CasovnaTarifa.Dnevna, razdelitev.Praznik_Dnevna, obracunam, imaPogodbo, sklad, povezaniPredracuni, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Praznik, CasovnaTarifa.Popoldanska, razdelitev.Praznik_Popoldanska, obracunam, imaPogodbo, sklad, povezaniPredracuni, trajanjeNaloga);
            ZapisiKategorijoNalogObracun(ctx, nalog, TipDneva.Praznik, CasovnaTarifa.Nocna, razdelitev.Praznik_Nocna, obracunam, imaPogodbo, sklad, povezaniPredracuni, trajanjeNaloga);
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
            // Preveri ali se kilometri obračunajo (KajObracunam mora biti KmMin ali Km)
            if (obracunDn == null)
                return;

            if (obracunDn.KajObracunam != KajObracunam.KmMin && obracunDn.KajObracunam != KajObracunam.Km && obracunDn.KajObracunam != KajObracunam.ObveznoZaracunaj)
                return;

            // Preveri ali je šifra kilometrine nastavljena
            if (string.IsNullOrEmpty(ctx.SifraKilometrina))
            {
                manjkajoceSifre.Add("Šifra kilometrine ni nastavljena v Parametri > Servisna");
                return;
            }

            // Preveri ali se številka ne obračuna (helper)
            if (!NalogHelper.SeObracunaKilometrina(nalog.Stevilka))
                return;

            // Pridobi kilometre iz naloga (SIF30)
            var km = (decimal)nalog.Sif30;
            if (km <= 0)
                return;

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

        /// <summary>
        /// Opiše izvor postavke računa (pri katerem dodajanju je nastala): pogodba, delovni nalog, ročni vnos ...
        /// </summary>
        private static string OpisiIzvorPostavke(ObracunOsnutekPos p)
        {
            return p.TipPostavke switch
            {
                TipPostavke.POGODBA when p.PogodbaStevilka.HasValue =>
                    $"POGODBA {p.PogodbaStevilka}/{p.PogodbaLeto}",
                TipPostavke.POGODBA => "POGODBA",
                TipPostavke.NALOG when !string.IsNullOrWhiteSpace(p.NalogStevilka) =>
                    $"NALOG {p.NalogStevilka}/{p.NalogLeto} (obračun minut / servisne storitve / kilometrina)",
                TipPostavke.NALOG => "NALOG (obračun minut / servisne storitve / kilometrina)",
                TipPostavke.ROCNI => "ROČNI VNOS (dodatna postavka)",
                _ => p.TipPostavke.ToString()
            };
        }

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

        /// <summary>
        /// Uporabi vse zapise iz OBRACUN_OSNUTEK_SPREMEMBA za danega partnerja:
        /// - če artikel že obstaja v OBRACUN_OSNUTEK_POS, mu prištej količino (negativna količina = odštevanje)
        /// - če bi nova količina bila <= 0, postavko izbriši
        /// - če artikel še ne obstaja, ustvari novo postavko (TipPostavke = NALOG, da se ob ponovnem obračunu pobriše in ne podvaja)
        /// V <paramref name="logRows"/> doda zapise o starih in novih postavkah za izpis v skupnem logu.
        /// </summary>
        private static void UporabiSpremembeKolicin(ObracunContext ctx, int partner, List<string> logRows)
        {
            var spremembe = ctx.Db.ObracunOsnutekSprememba
                .Where(s => s.Mesec == ctx.Mesec && s.Leto == ctx.Leto && s.Partner == partner)
                .OrderBy(s => s.DatumVnosa)
                .ToList();

            if (spremembe.Count == 0)
                return;

            string? nazivPartnerja = ctx.Db.Partner
                .Where(p => p.Sifra == partner)
                .Select(p => p.Naziv)
                .FirstOrDefault();

            // 1) Posnetek postavk PRED spremembami
            var postavkePred = ctx.Db.ObracunOsnutekPos
                .Where(p => p.Mesec == ctx.Mesec && p.Leto == ctx.Leto && p.Partner == partner)
                .OrderBy(p => p.Zs)
                .ToList();

            logRows.Add("");
            logRows.Add($"--- Partner {partner} {nazivPartnerja} ---");

            // Seznam sprememb
            logRows.Add($"  Spremembe količin:");
            foreach (var s in spremembe)
            {
                ctx.Artikli.TryGetValue(s.Artikel, out var artInfoLog);
                var nazivLog = artInfoLog?.Naziv ?? "?";
                var opombaLog = string.IsNullOrWhiteSpace(s.Opomba) ? "" : $", opomba: \"{s.Opomba}\"";
                logRows.Add($"    {s.Artikel} ({nazivLog}), kolicina {s.Kolicina:N2}  [{s.Uporabnik}, {s.DatumVnosa:dd.MM.yyyy HH:mm}{opombaLog}]");
            }

            // Postavke PRED
            logRows.Add($"  Postavke računa PRED spremembami:");
            if (postavkePred.Count == 0)
            {
                logRows.Add($"    (ni postavk)");
            }
            else
            {
                foreach (var p in postavkePred)
                {
                    var kol = p.Kolicina ?? 0m;
                    var vred = kol * (p.Cena ?? 0m) * (1m - (p.Rabat ?? 0m) / 100m);
                    logRows.Add($"    {p.Artikel} {p.Naziv}, kolicina {kol:N2}, vrednost {vred:N2}");
                }
            }

            // 2) Uveljavi spremembe
            foreach (var s in spremembe)
            {
                ctx.Artikli.TryGetValue(s.Artikel, out var artInfo);
                var nazivArt = artInfo?.Naziv ?? "?";
                var cenaArt = artInfo?.ProdajnaCena ?? 0m;

                var obstojeca = ctx.Db.ObracunOsnutekPos
                    .Where(p => p.Mesec == ctx.Mesec && p.Leto == ctx.Leto && p.Partner == partner && p.Artikel == s.Artikel)
                    .OrderBy(p => p.Zs)
                    .FirstOrDefault();

                if (obstojeca != null)
                {
                    var staraKol = obstojeca.Kolicina ?? 0m;
                    var novaKol = staraKol + s.Kolicina;
                    if (novaKol <= 0m)
                    {
                        ctx.Db.ObracunOsnutekPos
                            .Where(p => p.Mesec == ctx.Mesec && p.Leto == ctx.Leto && p.Partner == partner && p.Zs == obstojeca.Zs)
                            .Delete();
                    }
                    else
                    {
                        ctx.Db.ObracunOsnutekPos
                            .Where(p => p.Mesec == ctx.Mesec && p.Leto == ctx.Leto && p.Partner == partner && p.Zs == obstojeca.Zs)
                            .Set(p => p.Kolicina, novaKol)
                            .Update();
                    }
                }
                else
                {
                    if (s.Kolicina <= 0m)
                        continue;

                    var maxZs = ctx.Db.ObracunOsnutekPos
                        .Where(p => p.Mesec == ctx.Mesec && p.Leto == ctx.Leto && p.Partner == partner)
                        .Select(p => (int?)p.Zs)
                        .Max() ?? 0;

                    var novaPostavka = new ObracunOsnutekPos
                    {
                        Mesec = ctx.Mesec,
                        Leto = ctx.Leto,
                        Partner = partner,
                        Zs = maxZs + 1,
                        Artikel = s.Artikel,
                        Naziv = nazivArt,
                        Kolicina = s.Kolicina,
                        Cena = cenaArt,
                        Rabat = 0m,
                        TipPostavke = TipPostavke.NALOG
                    };
                    ctx.Db.Insert(novaPostavka);
                }
            }

            // 3) Posnetek postavk PO spremembah
            var postavkePo = ctx.Db.ObracunOsnutekPos
                .Where(p => p.Mesec == ctx.Mesec && p.Leto == ctx.Leto && p.Partner == partner)
                .OrderBy(p => p.Zs)
                .ToList();

            logRows.Add($"  Postavke računa PO spremembah:");
            if (postavkePo.Count == 0)
            {
                logRows.Add($"    (ni postavk)");
            }
            else
            {
                foreach (var p in postavkePo)
                {
                    var kol = p.Kolicina ?? 0m;
                    var vred = kol * (p.Cena ?? 0m) * (1m - (p.Rabat ?? 0m) / 100m);
                    logRows.Add($"    {p.Artikel} {p.Naziv}, kolicina {kol:N2}, vrednost {vred:N2}");
                }
            }
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

