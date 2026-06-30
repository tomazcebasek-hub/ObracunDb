using System;
using System.Collections.Generic;

namespace ObracunDb.Services
{
    /// <summary>
    /// Tip dneva za obra�un.
    /// </summary>
    public enum TipDneva
    {
        Delavnik,
        Vikend,
        Praznik
    }

    /// <summary>
    /// �asovna tarifa (ura v dnevu).
    /// </summary>
    public enum CasovnaTarifa
    {
        /// <summary>07:00 - 16:00</summary>
        Dnevna,
        /// <summary>16:00 - 22:00</summary>
        Popoldanska,
        /// <summary>22:00 - 07:00</summary>
        Nocna
    }

    /// <summary>
    /// Vrsta tarife glede na tip dela.
    /// </summary>
    public enum VrstaTarife
    {
        Servisna,
        Strokovna,
        Programerska
    }

    /// <summary>
    /// Razdelitev minut po tarifah.
    /// </summary>
    public class MinuteRazdelitev
    {
        // Delavnik
        public int Delavnik_Dnevna { get; set; }      // 07-16
        public int Delavnik_Popoldanska { get; set; } // 16-22
        public int Delavnik_Nocna { get; set; }       // 22-07

        // Vikend
        public int Vikend_Dnevna { get; set; }
        public int Vikend_Popoldanska { get; set; }
        public int Vikend_Nocna { get; set; }

        // Praznik
        public int Praznik_Dnevna { get; set; }
        public int Praznik_Popoldanska { get; set; }
        public int Praznik_Nocna { get; set; }

        /// <summary>
        /// Skupno �tevilo minut.
        /// </summary>
        public int SkupajMinut =>
            Delavnik_Dnevna + Delavnik_Popoldanska + Delavnik_Nocna +
            Vikend_Dnevna + Vikend_Popoldanska + Vikend_Nocna +
            Praznik_Dnevna + Praznik_Popoldanska + Praznik_Nocna;

        /// <summary>
        /// Doda minute v ustrezno kategorijo.
        /// </summary>
        public void DodajMinute(TipDneva tipDneva, CasovnaTarifa casovnaTarifa, int minute)
        {
            switch (tipDneva)
            {
                case TipDneva.Delavnik:
                    switch (casovnaTarifa)
                    {
                        case CasovnaTarifa.Dnevna: Delavnik_Dnevna += minute; break;
                        case CasovnaTarifa.Popoldanska: Delavnik_Popoldanska += minute; break;
                        case CasovnaTarifa.Nocna: Delavnik_Nocna += minute; break;
                    }
                    break;
                case TipDneva.Vikend:
                    switch (casovnaTarifa)
                    {
                        case CasovnaTarifa.Dnevna: Vikend_Dnevna += minute; break;
                        case CasovnaTarifa.Popoldanska: Vikend_Popoldanska += minute; break;
                        case CasovnaTarifa.Nocna: Vikend_Nocna += minute; break;
                    }
                    break;
                case TipDneva.Praznik:
                    switch (casovnaTarifa)
                    {
                        case CasovnaTarifa.Dnevna: Praznik_Dnevna += minute; break;
                        case CasovnaTarifa.Popoldanska: Praznik_Popoldanska += minute; break;
                        case CasovnaTarifa.Nocna: Praznik_Nocna += minute; break;
                    }
                    break;
            }
        }

        /// <summary>
        /// Se�teje drugo razdelitev v to.
        /// </summary>
        public void Pristej(MinuteRazdelitev druga)
        {
            Delavnik_Dnevna += druga.Delavnik_Dnevna;
            Delavnik_Popoldanska += druga.Delavnik_Popoldanska;
            Delavnik_Nocna += druga.Delavnik_Nocna;
            Vikend_Dnevna += druga.Vikend_Dnevna;
            Vikend_Popoldanska += druga.Vikend_Popoldanska;
            Vikend_Nocna += druga.Vikend_Nocna;
            Praznik_Dnevna += druga.Praznik_Dnevna;
            Praznik_Popoldanska += druga.Praznik_Popoldanska;
            Praznik_Nocna += druga.Praznik_Nocna;
        }

        /// <summary>
        /// Vrne novo razdelitev z največ toliko minutami kot je zahtevano, proporcionalno po kategorijah.
        /// </summary>
        public MinuteRazdelitev VzemiMinut(int minute)
        {
            var skupaj = SkupajMinut;
            if (minute <= 0 || skupaj <= 0)
                return new MinuteRazdelitev();
            if (minute >= skupaj)
                return Kopija();

            var result = new MinuteRazdelitev();
            var ostane = minute;
            var kategorije = new (Action<int> Set, int Vrednost)[]
            {
                (v => result.Delavnik_Dnevna = v, Delavnik_Dnevna),
                (v => result.Delavnik_Popoldanska = v, Delavnik_Popoldanska),
                (v => result.Delavnik_Nocna = v, Delavnik_Nocna),
                (v => result.Vikend_Dnevna = v, Vikend_Dnevna),
                (v => result.Vikend_Popoldanska = v, Vikend_Popoldanska),
                (v => result.Vikend_Nocna = v, Vikend_Nocna),
                (v => result.Praznik_Dnevna = v, Praznik_Dnevna),
                (v => result.Praznik_Popoldanska = v, Praznik_Popoldanska),
                (v => result.Praznik_Nocna = v, Praznik_Nocna)
            };

            var zadnjiPozitivni = Array.FindLastIndex(kategorije, k => k.Vrednost > 0);
            for (var i = 0; i < kategorije.Length; i++)
            {
                var vrednost = kategorije[i].Vrednost;
                if (vrednost <= 0)
                    continue;

                var vzeto = i == zadnjiPozitivni
                    ? ostane
                    : Math.Min(vrednost, (int)Math.Floor((decimal)vrednost * minute / skupaj));
                kategorije[i].Set(vzeto);
                ostane -= vzeto;
            }

            return result;
        }

        /// <summary>
        /// Vrne razliko med to in drugo razdelitvijo.
        /// </summary>
        public MinuteRazdelitev Odstej(MinuteRazdelitev druga)
        {
            return new MinuteRazdelitev
            {
                Delavnik_Dnevna = Delavnik_Dnevna - druga.Delavnik_Dnevna,
                Delavnik_Popoldanska = Delavnik_Popoldanska - druga.Delavnik_Popoldanska,
                Delavnik_Nocna = Delavnik_Nocna - druga.Delavnik_Nocna,
                Vikend_Dnevna = Vikend_Dnevna - druga.Vikend_Dnevna,
                Vikend_Popoldanska = Vikend_Popoldanska - druga.Vikend_Popoldanska,
                Vikend_Nocna = Vikend_Nocna - druga.Vikend_Nocna,
                Praznik_Dnevna = Praznik_Dnevna - druga.Praznik_Dnevna,
                Praznik_Popoldanska = Praznik_Popoldanska - druga.Praznik_Popoldanska,
                Praznik_Nocna = Praznik_Nocna - druga.Praznik_Nocna
            };
        }

        private MinuteRazdelitev Kopija()
        {
            return new MinuteRazdelitev
            {
                Delavnik_Dnevna = Delavnik_Dnevna,
                Delavnik_Popoldanska = Delavnik_Popoldanska,
                Delavnik_Nocna = Delavnik_Nocna,
                Vikend_Dnevna = Vikend_Dnevna,
                Vikend_Popoldanska = Vikend_Popoldanska,
                Vikend_Nocna = Vikend_Nocna,
                Praznik_Dnevna = Praznik_Dnevna,
                Praznik_Popoldanska = Praznik_Popoldanska,
                Praznik_Nocna = Praznik_Nocna
            };
        }

        /// <summary>
        /// Vrne berljiv izpis razdelitve.
        /// </summary>
        public override string ToString()
        {
            var parts = new List<string>();

            if (Delavnik_Dnevna > 0) parts.Add($"Del 7-16: {Delavnik_Dnevna}");
            if (Delavnik_Popoldanska > 0) parts.Add($"Del 16-22: {Delavnik_Popoldanska}");
            if (Delavnik_Nocna > 0) parts.Add($"Del 22-7: {Delavnik_Nocna}");

            if (Vikend_Dnevna > 0) parts.Add($"Vik 7-16: {Vikend_Dnevna}");
            if (Vikend_Popoldanska > 0) parts.Add($"Vik 16-22: {Vikend_Popoldanska}");
            if (Vikend_Nocna > 0) parts.Add($"Vik 22-7: {Vikend_Nocna}");

            if (Praznik_Dnevna > 0) parts.Add($"Pra 7-16: {Praznik_Dnevna}");
            if (Praznik_Popoldanska > 0) parts.Add($"Pra 16-22: {Praznik_Popoldanska}");
            if (Praznik_Nocna > 0) parts.Add($"Pra 22-7: {Praznik_Nocna}");

            return parts.Count > 0 ? string.Join(", ", parts) : "0 min";
        }
    }

    /// <summary>
    /// Kalkulator za razdelitev minut po tarifah.
    /// </summary>
    public static class MinuteCalculator
    {
        /// <summary>
        /// Izra�una razdelitev minut za en nalog.
        /// </summary>
        /// <param name="datum">Datum naloga</param>
        /// <param name="zacetek">Za�etni �as (ura:minuta)</param>
        /// <param name="konec">Kon�ni �as (ura:minuta)</param>
        /// <param name="prazniki">Seznam datumov praznikov</param>
        /// <returns>Razdelitev minut po tarifah</returns>
        public static MinuteRazdelitev IzracunajRazdelitev(DateTime datum, DateTime zacetek, DateTime konec, HashSet<DateTime> prazniki)
        {
            var razdelitev = new MinuteRazdelitev();

            // Dolo�i tip dneva
            var tipDneva = DolocitTipDneva(datum, prazniki);

            // �as za�etka in konca (samo ura:minuta)
            var zacetekUra = zacetek.TimeOfDay;
            var konecUra = konec.TimeOfDay;

            // �e je trajanje 0 (za�etek == konec), vrni prazno razdelitev
            if (zacetekUra == konecUra)
            {
                return razdelitev;
            }

            // �e je konec pred za�etkom, pomeni da gre �ez polno�
            if (konecUra < zacetekUra)
            {
                // Nalog gre �ez polno� - razdeli na dva dela
                // Del 1: od za�etka do polno�i
                RazdeliMinuteVCasovnePasove(razdelitev, tipDneva, zacetekUra, TimeSpan.FromHours(24));

                // Del 2: od polno�i do konca (naslednji dan)
                var naslednjaDatum = datum.AddDays(1);
                var tipNaslednjegaDne = DolocitTipDneva(naslednjaDatum, prazniki);
                RazdeliMinuteVCasovnePasove(razdelitev, tipNaslednjegaDne, TimeSpan.Zero, konecUra);
            }
            else
            {
                // Normalen nalog znotraj enega dne
                RazdeliMinuteVCasovnePasove(razdelitev, tipDneva, zacetekUra, konecUra);
            }

            return razdelitev;
        }

        /// <summary>
        /// Dolo�i tip dneva glede na datum.
        /// </summary>
        public static TipDneva DolocitTipDneva(DateTime datum, HashSet<DateTime> prazniki)
        {
            // Praznik ima prednost
            if (prazniki.Contains(datum.Date))
                return TipDneva.Praznik;

            // Vikend
            if (datum.DayOfWeek == DayOfWeek.Saturday || datum.DayOfWeek == DayOfWeek.Sunday)
                return TipDneva.Vikend;

            return TipDneva.Delavnik;
        }

        /// <summary>
        /// Dolo�i �asovno tarifo za dano uro.
        /// </summary>
        public static CasovnaTarifa DolocitCasovnoTarifo(TimeSpan ura)
        {
            var ure = ura.TotalHours;

            // 07:00 - 16:00 = Dnevna
            if (ure >= 7 && ure < 16)
                return CasovnaTarifa.Dnevna;

            // 16:00 - 22:00 = Popoldanska
            if (ure >= 16 && ure < 22)
                return CasovnaTarifa.Popoldanska;

            // 22:00 - 07:00 = No�na
            return CasovnaTarifa.Nocna;
        }

        /// <summary>
        /// Razdeli minute med za�etkom in koncem v ustrezne �asovne pasove.
        /// </summary>
        private static void RazdeliMinuteVCasovnePasove(MinuteRazdelitev razdelitev, TipDneva tipDneva, TimeSpan zacetek, TimeSpan konec)
        {
            if (zacetek >= konec)
                return;

            // Meje �asovnih pasov
            var meja7 = TimeSpan.FromHours(7);
            var meja16 = TimeSpan.FromHours(16);
            var meja22 = TimeSpan.FromHours(22);

            // No�na 00:00 - 07:00
            if (zacetek < meja7)
            {
                var konecPasu = konec < meja7 ? konec : meja7;
                var minute = (int)(konecPasu - zacetek).TotalMinutes;
                razdelitev.DodajMinute(tipDneva, CasovnaTarifa.Nocna, minute);
            }

            // Dnevna 07:00 - 16:00
            if (zacetek < meja16 && konec > meja7)
            {
                var zacetekPasu = zacetek > meja7 ? zacetek : meja7;
                var konecPasu = konec < meja16 ? konec : meja16;
                if (konecPasu > zacetekPasu)
                {
                    var minute = (int)(konecPasu - zacetekPasu).TotalMinutes;
                    razdelitev.DodajMinute(tipDneva, CasovnaTarifa.Dnevna, minute);
                }
            }

            // Popoldanska 16:00 - 22:00
            if (zacetek < meja22 && konec > meja16)
            {
                var zacetekPasu = zacetek > meja16 ? zacetek : meja16;
                var konecPasu = konec < meja22 ? konec : meja22;
                if (konecPasu > zacetekPasu)
                {
                    var minute = (int)(konecPasu - zacetekPasu).TotalMinutes;
                    razdelitev.DodajMinute(tipDneva, CasovnaTarifa.Popoldanska, minute);
                }
            }

            // No�na 22:00 - 24:00
            if (konec > meja22)
            {
                var zacetekPasu = zacetek > meja22 ? zacetek : meja22;
                var minute = (int)(konec - zacetekPasu).TotalMinutes;
                razdelitev.DodajMinute(tipDneva, CasovnaTarifa.Nocna, minute);
            }
        }
    }
}
