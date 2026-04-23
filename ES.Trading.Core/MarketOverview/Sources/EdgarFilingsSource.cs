using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ES.Trading.Core.MarketOverview.Sources
{
    /// <summary>
    /// Pulls 8-K filings from EDGAR's full-text search API for the overnight window,
    /// then intersects with the embedded SPX constituents table. Non-SPX filings are
    /// dropped by default — you'll see them via news feeds if they matter at a sector
    /// level.
    ///
    /// Note: EDGAR's search granularity is by date, not timestamp, so we pull the
    /// full day of "since" and rely on the ranker to push boilerplate (Item 7.01 /
    /// 9.01-only) filings to the bottom of the panel.
    /// </summary>
    public class EdgarFilingsSource : IFilingsSource
    {
        private const string SearchBase = "https://efts.sec.gov/LATEST/search-index";
        private readonly SpxConstituentTable _spx;
        private readonly int _maxHits;

        public EdgarFilingsSource(SpxConstituentTable spx, int maxHits = 100)
        {
            _spx     = spx;
            _maxHits = Math.Min(100, Math.Max(10, maxHits));  // EDGAR caps at 100 per page
        }

        public async Task<IReadOnlyList<MarketEvent>> GetFilingsSinceAsync(DateTime sinceUtc, CancellationToken ct)
        {
            // EDGAR's search uses ET calendar dates. We bracket the window from the ET
            // date of sinceUtc through today ET.
            var etZone = TryGetEasternTimeZone();
            var sinceEt = TimeZoneInfo.ConvertTimeFromUtc(sinceUtc, etZone);
            var nowEt   = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, etZone);

            string start = sinceEt.ToString("yyyy-MM-dd");
            string end   = nowEt.ToString("yyyy-MM-dd");

            string url = SearchBase
                + "?q=&forms=8-K"
                + "&dateRange=custom"
                + "&startdt=" + start
                + "&enddt="   + end
                + "&hits="    + _maxHits;

            using var resp = await MarketOverviewHttp.SecEdgar.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            var results = new List<MarketEvent>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("hits", out var hitsWrap)) return results;
            if (!hitsWrap.TryGetProperty("hits", out var hitsArr))         return results;

            foreach (var hit in hitsArr.EnumerateArray())
            {
                if (!hit.TryGetProperty("_source", out var src)) continue;

                var ev = ParseHit(hit, src);
                if (ev == null) continue;
                results.Add(ev);
            }
            return results;
        }

        private MarketEvent? ParseHit(JsonElement hit, JsonElement src)
        {
            // Accession number drives the archive URL.
            string accno = hit.TryGetProperty("_id", out var idEl) ? idEl.GetString() ?? string.Empty : string.Empty;

            // CIK — first of the ciks array. Padded to 10 digits.
            int? cik = null;
            if (src.TryGetProperty("ciks", out var ciksEl) && ciksEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in ciksEl.EnumerateArray())
                {
                    var s = c.GetString();
                    if (int.TryParse(s, out var parsed)) { cik = parsed; break; }
                }
            }
            if (cik == null) return null;

            // Drop anything not in our SPX seed. (See class doc for rationale.)
            if (!_spx.TryGetByCik(cik.Value, out var constituent)) return null;

            // Items — normalize "Item 2.02" → "2.02"
            var items = new List<string>();
            if (src.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in itemsEl.EnumerateArray())
                {
                    var raw = it.GetString();
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var norm = raw!.Trim();
                    if (norm.StartsWith("Item ", StringComparison.OrdinalIgnoreCase))
                        norm = norm.Substring(5).Trim();
                    items.Add(norm);
                }
            }

            // Filed date → UTC noon as a reasonable approximation when we only have date granularity.
            DateTime filedUtc = DateTime.UtcNow;
            if (src.TryGetProperty("file_date", out var dtEl) && dtEl.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(dtEl.GetString(), out var dt))
                    filedUtc = DateTime.SpecifyKind(dt.Date.AddHours(16), DateTimeKind.Utc); // ~noon ET = 16:00 UTC
            }

            string accnoNoDashes = accno.Replace("-", "");
            string url = "https://www.sec.gov/Archives/edgar/data/"
                + cik.Value + "/" + accnoNoDashes + "/" + accno + "-index.htm";

            string itemsStr = items.Count > 0 ? " (Item " + string.Join(", ", items) + ")" : string.Empty;
            string headline = constituent.Name + " filed 8-K" + itemsStr;

            return new MarketEvent
            {
                Kind         = EventKind.EdgarFiling,
                Headline     = headline,
                Source       = "EDGAR",
                Url          = url,
                TimestampUtc = filedUtc,
                Ticker       = constituent.Ticker,
                CompanyName  = constituent.Name,
                Sector       = constituent.Sector,
                FilingItems  = items
            };
        }

        private static TimeZoneInfo TryGetEasternTimeZone()
        {
            // Windows uses "Eastern Standard Time"; Linux/macOS use "America/New_York".
            try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
            catch { }
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }
            catch { }
            return TimeZoneInfo.Utc;   // last resort — will be off by 4-5 hours
        }
    }
}
