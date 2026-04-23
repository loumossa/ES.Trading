using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ES.Trading.Core.MarketOverview.Sources
{
    /// <summary>
    /// Pulls announced Treasury auctions from TreasuryDirect's public JSON feed.
    /// We keep upcoming Notes/Bonds within the next 5 days — T-bills are deliberately
    /// included but get down-weighted by the ranker.
    /// </summary>
    public class TreasuryAuctionSource : ITreasuryAuctionSource
    {
        private const string FeedUrl = "https://www.treasurydirect.gov/TA_WS/securities/announced?format=json";

        public async Task<IReadOnlyList<MarketEvent>> GetUpcomingAsync(DateTime todayLocal, CancellationToken ct)
        {
            using var resp = await MarketOverviewHttp.Generic.GetAsync(FeedUrl, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            var results  = new List<MarketEvent>();
            var cutoff   = todayLocal.Date.AddDays(5);
            var lowerEt  = todayLocal.Date;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return results;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var ev = ParseAuction(el, lowerEt, cutoff);
                if (ev != null) results.Add(ev);
            }
            return results.OrderBy(e => e.TimestampUtc).ToList();
        }

        private MarketEvent? ParseAuction(JsonElement el, DateTime lowerDate, DateTime upperDate)
        {
            string type = GetString(el, "securityType") ?? string.Empty;
            string term = GetString(el, "securityTerm") ?? string.Empty;
            string auctionDateStr = GetString(el, "auctionDate") ?? string.Empty;

            if (!DateTime.TryParse(auctionDateStr, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var auctionDate))
                return null;

            if (auctionDate.Date < lowerDate || auctionDate.Date > upperDate) return null;

            // 11am ET is the typical auction window — use that as a reasonable timestamp.
            var etZone = TryGetEasternTimeZone();
            var etWall = auctionDate.Date.AddHours(11);
            var utc    = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(etWall, DateTimeKind.Unspecified), etZone);

            string headline = "Treasury auction: " + term + " " + type;

            return new MarketEvent
            {
                Kind         = EventKind.TreasuryAuction,
                Headline     = headline,
                Source       = "TreasuryDirect",
                Url          = "https://www.treasurydirect.gov/auctions/announcements-data-results/",
                TimestampUtc = utc,
                Breadth      = EventBreadth.Macro
            };
        }

        private static string? GetString(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static TimeZoneInfo TryGetEasternTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); } catch { }
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }      catch { }
            return TimeZoneInfo.Utc;
        }
    }
}
