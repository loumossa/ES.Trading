using System;

namespace ES.Trading.Core.MarketOverview
{
    /// <summary>
    /// Runtime-configurable options for the market overview service.
    /// Keep these in one place so the UI can expose them in Settings later.
    /// </summary>
    public class MarketOverviewOptions
    {
        /// <summary>
        /// User-Agent sent to SEC EDGAR. SEC requires a real UA with contact info
        /// and will 403 or throttle anonymous traffic.
        /// </summary>
        public string SecUserAgent { get; set; } = "ES.Trading/1.0 (contact: user@example.com)";

        /// <summary>Hard cap on items kept per source before ranking. Keeps memory bounded.</summary>
        public int MaxItemsPerSource { get; set; } = 200;

        /// <summary>HTTP timeout per request.</summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(20);

        /// <summary>Whole-panel fetch timeout. If exceeded, orchestrator returns whatever completed.</summary>
        public TimeSpan OverallTimeout { get; set; } = TimeSpan.FromSeconds(45);
    }
}
