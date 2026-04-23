using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ES.Trading.Core.MarketOverview.Sources;

namespace ES.Trading.Core.MarketOverview
{
    /// <summary>
    /// Fans out to all configured sources in parallel, ranks the aggregate, and
    /// returns a single <see cref="MarketOverviewSnapshot"/>. Individual source
    /// failures are caught, recorded in <see cref="MarketOverviewSnapshot.SourceErrors"/>,
    /// and the remaining sources are kept — so one flaky feed never blanks the panel.
    /// </summary>
    public class MarketOverviewService
    {
        private readonly IEconomicCalendarSource _calendar;
        private readonly IFilingsSource          _filings;
        private readonly INewsSource             _news;
        private readonly IMarketDataSource       _market;
        private readonly IFedCalendarSource      _fed;
        private readonly ITreasuryAuctionSource  _treasury;

        private readonly EventRanker          _ranker;
        private readonly MarketOverviewOptions _options;

        public MarketOverviewService(
            MarketOverviewOptions   options,
            IEconomicCalendarSource calendar,
            IFilingsSource          filings,
            INewsSource             news,
            IMarketDataSource       market,
            IFedCalendarSource      fed,
            ITreasuryAuctionSource  treasury,
            EventRanker?            ranker = null)
        {
            _options  = options;
            _calendar = calendar;
            _filings  = filings;
            _news     = news;
            _market   = market;
            _fed      = fed;
            _treasury = treasury;
            _ranker   = ranker ?? new EventRanker();
        }

        /// <summary>
        /// Default wiring: embedded SPX seed, all free sources enabled. Call once at
        /// app startup; swap the <see cref="IMarketDataSource"/> for a Schwab adapter
        /// when that's wired up.
        /// </summary>
        public static MarketOverviewService CreateDefault(MarketOverviewOptions options)
        {
            MarketOverviewHttp.Configure(options);
            var spx = SpxConstituentTable.Load();

            return new MarketOverviewService(
                options,
                calendar: new ForexFactoryCalendarSource(),
                filings:  new EdgarFilingsSource(spx, options.MaxItemsPerSource),
                news:     new RssNewsSource(options.MaxItemsPerSource),
                market:   new YahooMarketDataSource(),
                fed:      new FedCalendarSource(),
                treasury: new TreasuryAuctionSource());
        }

        /// <summary>
        /// Fetch everything and build the snapshot. Respects the overall-timeout in
        /// options — if that hits, whatever has completed is returned.
        /// </summary>
        public async Task<MarketOverviewSnapshot> GetSnapshotAsync(CancellationToken ct = default)
        {
            var window   = ComputeOvernightWindow(DateTime.UtcNow);
            var snapshot = new MarketOverviewSnapshot
            {
                WindowStartUtc = window.startUtc,
                WindowEndUtc   = window.endUtc
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_options.OverallTimeout);

            // Wrap each source so exceptions map to snapshot.SourceErrors instead of propagating.
            var quotesT   = SafeAsync("MarketData", () => _market.GetOvernightQuotesAsync(cts.Token), snapshot);
            var calendarT = SafeAsync("Calendar",   () => _calendar.GetForDateAsync(DateTime.UtcNow, cts.Token), snapshot);
            var filingsT  = SafeAsync("EDGAR",      () => _filings.GetFilingsSinceAsync(window.startUtc, cts.Token), snapshot);
            var newsT     = SafeAsync("News",       () => _news.GetHeadlinesSinceAsync(window.startUtc, cts.Token), snapshot);
            var fedT      = SafeAsync("Fed",        () => _fed.GetUpcomingAsync(DateTime.UtcNow, cts.Token), snapshot);
            var treasuryT = SafeAsync("Treasury",   () => _treasury.GetUpcomingAsync(DateTime.UtcNow, cts.Token), snapshot);

            await Task.WhenAll(quotesT, calendarT, filingsT, newsT, fedT, treasuryT).ConfigureAwait(false);

            snapshot.OvernightQuotes  = (await quotesT.ConfigureAwait(false)).ToList();
            snapshot.EconomicCalendar = (await calendarT.ConfigureAwait(false)).ToList();

            // Merge all event-shaped sources, rank, keep top N.
            var allEvents = new List<MarketEvent>();
            allEvents.AddRange(await filingsT.ConfigureAwait(false));
            allEvents.AddRange(await newsT.ConfigureAwait(false));
            allEvents.AddRange(await fedT.ConfigureAwait(false));
            allEvents.AddRange(await treasuryT.ConfigureAwait(false));

            var ranked = _ranker.RankAll(allEvents);
            snapshot.TopEvents  = ranked.Take(25).ToList();
            snapshot.SectorHeat = _ranker.BuildSectorHeat(ranked);

            return snapshot;
        }

        /// <summary>
        /// Overnight window: most recent NY cash close (4pm ET, weekdays only) to now.
        ///
        /// Examples (all ET):
        ///   Wed 10:41 PM  → Wed 4:00 PM  (today's close already happened)
        ///   Thu  7:00 AM  → Wed 4:00 PM  (today's close hasn't happened yet)
        ///   Mon  7:00 AM  → Fri 4:00 PM  (walk back through the weekend)
        ///   Sat/Sun any   → Fri 4:00 PM
        ///   Mon  5:00 PM  → Mon 4:00 PM  (today's close just happened)
        ///
        /// Does not account for NYSE holidays — they'll effectively look like a long
        /// weekend with no cash activity, which is fine for morning prep.
        /// </summary>
        public static (DateTime startUtc, DateTime endUtc) ComputeOvernightWindow(DateTime nowUtc)
        {
            var etZone = TryGetEasternTimeZone();
            var nowEt  = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, etZone);

            DateTime closeEt;
            if (nowEt.Hour >= 16 && IsWeekday(nowEt.DayOfWeek))
            {
                // Today's 4pm close already happened and it's a weekday.
                closeEt = nowEt.Date.AddHours(16);
            }
            else
            {
                // Walk back to the most recent weekday and take its 4pm close.
                var day = nowEt.Date.AddDays(-1);
                while (!IsWeekday(day.DayOfWeek))
                    day = day.AddDays(-1);
                closeEt = day.AddHours(16);
            }

            var startUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(closeEt, DateTimeKind.Unspecified), etZone);
            return (startUtc, nowUtc);
        }

        private static bool IsWeekday(DayOfWeek d)
            => d != DayOfWeek.Saturday && d != DayOfWeek.Sunday;

        private static async Task<IReadOnlyList<T>> SafeAsync<T>(
            string sourceName,
            Func<Task<IReadOnlyList<T>>> op,
            MarketOverviewSnapshot snapshot)
        {
            try
            {
                return await op().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (snapshot.SourceErrors)
                    snapshot.SourceErrors[sourceName] = ex.GetType().Name + ": " + ex.Message;
                return Array.Empty<T>();
            }
        }

        private static TimeZoneInfo TryGetEasternTimeZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); } catch { }
            try { return TimeZoneInfo.FindSystemTimeZoneById("America/New_York"); }      catch { }
            return TimeZoneInfo.Utc;
        }
    }
}
