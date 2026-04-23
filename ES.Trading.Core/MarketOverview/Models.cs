using System;
using System.Collections.Generic;

namespace ES.Trading.Core.MarketOverview
{
    /// <summary>
    /// A ranked news, filing, or Fed/Treasury event surfaced on the morning-prep panel.
    /// All fields are nullable-friendly so partial data from a source isn't fatal.
    /// </summary>
    public class MarketEvent
    {
        public EventKind    Kind         { get; set; }
        public string       Headline     { get; set; } = string.Empty;
        public string?      Summary      { get; set; }
        public string       Source       { get; set; } = string.Empty;   // "EDGAR", "Reuters", "Federal Reserve", ...
        public string?      Url          { get; set; }
        public DateTime     TimestampUtc { get; set; }

        public string?      Ticker       { get; set; }                    // primary ticker when identifiable
        public string?      CompanyName  { get; set; }
        public GicsSector   Sector       { get; set; } = GicsSector.Unknown;
        public EventBreadth Breadth      { get; set; } = EventBreadth.SingleTicker;

        /// <summary>Numeric impact score produced by EventRanker. Higher = surface earlier.</summary>
        public double       Score        { get; set; }

        /// <summary>Human-readable rationale for the score, e.g. "8-K Item 4.02 × SEC × sector".</summary>
        public string?      ScoreReason  { get; set; }

        /// <summary>EDGAR-specific: 8-K item codes like "2.02", "5.02" when present.</summary>
        public IReadOnlyList<string>? FilingItems { get; set; }
    }

    /// <summary>
    /// A scheduled economic release on today's calendar (CPI, NFP, FOMC, etc.).
    /// Separate type from MarketEvent because impact is built-in (ForexFactory colors).
    /// </summary>
    public class EconomicEvent
    {
        public DateTime   TimeUtc    { get; set; }
        public string     Country    { get; set; } = string.Empty;   // "USD", "EUR", ...
        public string     Title      { get; set; } = string.Empty;   // "Core CPI m/m"
        public ImpactTier Impact     { get; set; }
        public string?    Forecast   { get; set; }
        public string?    Previous   { get; set; }
        public string?    Actual     { get; set; }                   // if already released
    }

    /// <summary>Single quote row in the overnight strip (futures, indices, rates, FX).</summary>
    public class OvernightQuote
    {
        public string   Symbol       { get; set; } = string.Empty;   // raw source symbol, e.g. "ES=F"
        public string   DisplayName  { get; set; } = string.Empty;   // "S&P 500 Futures"
        public string   Category     { get; set; } = string.Empty;   // "US Futures", "Global", "Rates", "FX"
        public double?  Last         { get; set; }
        public double?  ChangeAbs    { get; set; }
        public double?  ChangePct    { get; set; }
        public DateTime? QuoteTimeUtc { get; set; }
    }

    /// <summary>
    /// Aggregated events rolled up by GICS sector for the heat grid.
    /// Only sectors with at least one flagged event are typically rendered.
    /// </summary>
    public class SectorHeat
    {
        public GicsSector               Sector      { get; set; }
        public double                   TotalScore  { get; set; }
        public int                      EventCount  { get; set; }
        public List<MarketEvent>        TopEvents   { get; set; } = new List<MarketEvent>();
    }

    /// <summary>
    /// The full morning-coffee payload. One snapshot per fetch.
    /// Per-source failures are captured in <see cref="SourceErrors"/> rather than thrown,
    /// so a single flaky feed doesn't nuke the whole panel.
    /// </summary>
    public class MarketOverviewSnapshot
    {
        public DateTime                            GeneratedUtc    { get; set; } = DateTime.UtcNow;
        public DateTime                            WindowStartUtc  { get; set; }
        public DateTime                            WindowEndUtc    { get; set; }

        public List<OvernightQuote>                OvernightQuotes { get; set; } = new List<OvernightQuote>();
        public List<EconomicEvent>                 EconomicCalendar { get; set; } = new List<EconomicEvent>();
        public List<MarketEvent>                   TopEvents       { get; set; } = new List<MarketEvent>();
        public List<SectorHeat>                    SectorHeat      { get; set; } = new List<SectorHeat>();

        /// <summary>source name → error message, populated only for sources that failed.</summary>
        public Dictionary<string, string>          SourceErrors    { get; set; } = new Dictionary<string, string>();
    }
}
