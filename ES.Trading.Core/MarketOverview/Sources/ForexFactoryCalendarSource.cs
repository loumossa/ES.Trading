using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ES.Trading.Core.MarketOverview.Sources
{
    /// <summary>
    /// Parses the weekly economic calendar XML published by the FairEconomy mirror
    /// of ForexFactory. This feed is free and doesn't require a key. All times are
    /// reported in Eastern Time, which we convert to UTC.
    ///
    /// Impact tiers: High=red, Medium=orange, Low=yellow, Holiday=skipped.
    /// </summary>
    public class ForexFactoryCalendarSource : IEconomicCalendarSource
    {
        private const string FeedUrl = "https://nfs.faireconomy.media/ff_calendar_thisweek.xml";

        public async Task<IReadOnlyList<EconomicEvent>> GetForDateAsync(DateTime dateLocal, CancellationToken ct)
        {
            using var resp = await MarketOverviewHttp.Generic.GetAsync(FeedUrl, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string xml = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            var doc = XDocument.Parse(xml);
            var results = new List<EconomicEvent>();
            var etZone  = TryGetEasternTimeZone();

            // We want events that fall on the caller's local date, but expressed in ET
            // (because the feed is ET). Convert the incoming dateLocal to ET wall clock
            // and match by ET calendar date.
            var targetEtDate = dateLocal.Date;

            foreach (var node in doc.Descendants("event"))
            {
                var ev = ParseEvent(node, etZone);
                if (ev == null) continue;
                var eventEt = TimeZoneInfo.ConvertTimeFromUtc(ev.TimeUtc, etZone).Date;
                if (eventEt != targetEtDate) continue;
                results.Add(ev);
            }
            return results.OrderBy(e => e.TimeUtc).ToList();
        }

        private EconomicEvent? ParseEvent(XElement node, TimeZoneInfo etZone)
        {
            string title    = (string?)node.Element("title")    ?? string.Empty;
            string country  = (string?)node.Element("country")  ?? string.Empty;
            string dateStr  = (string?)node.Element("date")     ?? string.Empty;
            string timeStr  = (string?)node.Element("time")     ?? string.Empty;
            string impact   = (string?)node.Element("impact")   ?? string.Empty;
            string forecast = (string?)node.Element("forecast") ?? string.Empty;
            string previous = (string?)node.Element("previous") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(dateStr)) return null;
            if (impact.Equals("Holiday", StringComparison.OrdinalIgnoreCase))           return null;

            // Date: "MM-dd-yyyy". Time: "8:30am", "All Day", "Tentative", or blank.
            if (!DateTime.TryParseExact(dateStr, "MM-dd-yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                return null;

            DateTime etWall;
            if (TryParseFfTime(timeStr, out var timeOfDay))
                etWall = date.Date.Add(timeOfDay);
            else
                etWall = date.Date.AddHours(8);  // "All Day" / "Tentative" — park at 8am ET

            // ET is ambiguous around DST transitions — assume unambiguous for calendar releases.
            var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(etWall, DateTimeKind.Unspecified), etZone);

            return new EconomicEvent
            {
                TimeUtc  = utc,
                Country  = country,
                Title    = title,
                Impact   = ParseImpact(impact),
                Forecast = NullIfEmpty(forecast),
                Previous = NullIfEmpty(previous)
            };
        }

        private static bool TryParseFfTime(string s, out TimeSpan result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (DateTime.TryParseExact(s.Trim(), new[] { "h:mmtt", "htt" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            {
                result = t.TimeOfDay;
                return true;
            }
            return false;
        }

        private static ImpactTier ParseImpact(string s) => s.ToLowerInvariant() switch
        {
            "high"   => ImpactTier.High,
            "medium" => ImpactTier.Medium,
            "low"    => ImpactTier.Low,
            _        => ImpactTier.Low
        };

        private static string? NullIfEmpty(string s)
            => string.IsNullOrWhiteSpace(s) ? null : s;

        private static TimeZoneInfo TryGetEasternTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); } catch { }
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }      catch { }
            return TimeZoneInfo.Utc;
        }
    }
}
