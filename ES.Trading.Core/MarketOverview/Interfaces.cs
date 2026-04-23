using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ES.Trading.Core.MarketOverview
{
    // Each source returns its slice of the snapshot asynchronously. The orchestrator
    // fans them out in parallel and tolerates individual-source failures.
    //
    // "Since" is the overnight-window start (Friday 4pm ET on Mondays, prior day 4pm ET otherwise)
    // converted to UTC. Sources should filter to events >= since.

    public interface IEconomicCalendarSource
    {
        Task<IReadOnlyList<EconomicEvent>> GetForDateAsync(DateTime dateLocal, CancellationToken ct);
    }

    public interface IFilingsSource
    {
        Task<IReadOnlyList<MarketEvent>> GetFilingsSinceAsync(DateTime sinceUtc, CancellationToken ct);
    }

    public interface INewsSource
    {
        Task<IReadOnlyList<MarketEvent>> GetHeadlinesSinceAsync(DateTime sinceUtc, CancellationToken ct);
    }

    public interface IMarketDataSource
    {
        Task<IReadOnlyList<OvernightQuote>> GetOvernightQuotesAsync(CancellationToken ct);
    }

    public interface IFedCalendarSource
    {
        Task<IReadOnlyList<MarketEvent>> GetUpcomingAsync(DateTime todayLocal, CancellationToken ct);
    }

    public interface ITreasuryAuctionSource
    {
        Task<IReadOnlyList<MarketEvent>> GetUpcomingAsync(DateTime todayLocal, CancellationToken ct);
    }
}
