using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ES.Trading.Core.MarketOverview.Sources
{
    /// <summary>
    /// v1 placeholder: returns nothing. Fed speaker appearances and FOMC statements
    /// are picked up by the RSS news source and boosted by the ranker's macro keyword
    /// list ("fed", "fomc", "powell", ...).
    ///
    /// Future work: scrape federalreserve.gov/newsevents/calendar.htm or (if a stable
    /// JSON endpoint emerges) parse that and produce strongly-typed entries so we can
    /// render "today at 2pm ET: Fed Chair speaking" deterministically.
    /// </summary>
    public class FedCalendarSource : IFedCalendarSource
    {
        public Task<IReadOnlyList<MarketEvent>> GetUpcomingAsync(DateTime todayLocal, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MarketEvent>>(Array.Empty<MarketEvent>());
    }
}
