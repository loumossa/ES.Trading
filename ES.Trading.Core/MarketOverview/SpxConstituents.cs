using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ES.Trading.Core.MarketOverview
{
    /// <summary>One row of the embedded SPX constituents table.</summary>
    public class SpxConstituent
    {
        public string     Ticker { get; set; } = string.Empty;
        public int        Cik    { get; set; }
        public string     Name   { get; set; } = string.Empty;
        public GicsSector Sector { get; set; }
    }

    /// <summary>
    /// Loads the embedded SPX constituents CSV once and offers fast lookups by CIK or ticker.
    ///
    /// The CSV is a curated top-~90 list by index weight — not the full 500. That's
    /// intentional: these names carry the bulk of SPX weight and are the ones whose
    /// filings actually move the index. Expand by editing spx_constituents.csv and
    /// re-embedding (no code changes required).
    /// </summary>
    public class SpxConstituentTable
    {
        private readonly Dictionary<int, SpxConstituent>    _byCik    = new Dictionary<int, SpxConstituent>();
        private readonly Dictionary<string, SpxConstituent> _byTicker = new Dictionary<string, SpxConstituent>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<SpxConstituent> All => _byCik.Values;

        public static SpxConstituentTable Load()
        {
            var table = new SpxConstituentTable();
            var asm   = typeof(SpxConstituentTable).Assembly;
            string resourceName = FindResource(asm, "spx_constituents.csv");
            if (resourceName == null)
                throw new InvalidOperationException("Embedded resource spx_constituents.csv not found");

            using (var stream = asm.GetManifestResourceStream(resourceName)!)
            using (var reader = new StreamReader(stream))
            {
                string? line;
                bool    headerSkipped = false;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!headerSkipped) { headerSkipped = true; continue; }

                    var parts = line.Split(',');
                    if (parts.Length < 4) continue;
                    if (!int.TryParse(parts[1].Trim(), out int cik)) continue;

                    var row = new SpxConstituent
                    {
                        Ticker = parts[0].Trim(),
                        Cik    = cik,
                        Name   = parts[2].Trim(),
                        Sector = ParseSector(parts[3].Trim())
                    };

                    table._byCik[cik]          = row;
                    table._byTicker[row.Ticker] = row;
                }
            }
            return table;
        }

        public bool TryGetByCik(int cik, out SpxConstituent row) => _byCik.TryGetValue(cik, out row!);

        public bool TryGetByTicker(string ticker, out SpxConstituent row)
            => _byTicker.TryGetValue(ticker, out row!);

        private static string FindResource(Assembly asm, string suffix)
        {
            foreach (var name in asm.GetManifestResourceNames())
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return name;
            return null!;
        }

        private static GicsSector ParseSector(string s)
            => Enum.TryParse<GicsSector>(s, true, out var v) ? v : GicsSector.Unknown;
    }
}
