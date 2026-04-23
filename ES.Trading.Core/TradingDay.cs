using System;

namespace ES.Trading.Core.Models
{
    public class TradingDay
    {
        public int Id { get; set; }

        /// <summary>ISO 8601 date string (yyyy-MM-dd). Unique per day.</summary>
        public string Date { get; set; } = string.Empty;

        public double? ORHigh { get; set; }
        public double? ORLow { get; set; }

        /// <summary>Calculated: ORHigh - ORLow. Null until OR is locked at 9:30:30.</summary>
        public double? ORRange => (ORHigh.HasValue && ORLow.HasValue)
            ? ORHigh.Value - ORLow.Value
            : null;

        /// <summary>True if this is a no-trade day (FOMC, NFP, CPI).</summary>
        public bool IsNoTradeDay { get; set; }

        /// <summary>Pre-session mood/readiness rating. 1 (poor) to 5 (excellent).</summary>
        public int? MoodRating { get; set; }

        /// <summary>Cumulative realized P&L for the session. Updated as trades close.</summary>
        public double DailyPL { get; set; }

        /// <summary>Number of trade attempts taken today (incremented at each entry).</summary>
        public int AttemptCount { get; set; }

        /// <summary>True if the daily loss limit was hit and trading was shut down.</summary>
        public bool DailyLossLimitHit { get; set; }

        public string? Notes { get; set; }
    }
}
