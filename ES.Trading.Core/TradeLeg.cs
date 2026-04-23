using System;

namespace ES.Trading.Core.Models
{
    public class TradeLeg
    {
        public int Id { get; set; }
        public int TradeId { get; set; }

        /// <summary>
        /// "Entry", "PartialExit", "StopOut", "AddOn", "FinalExit"
        /// Use LegTypes constants for consistency.
        /// </summary>
        public string LegType { get; set; } = string.Empty;

        public DateTime ExecutionTime { get; set; }
        public double Price { get; set; }
        public int Contracts { get; set; }

        /// <summary>
        /// Realized P&L for exit legs. Null for entry and add-on legs.
        /// For MES fractional exits, this is already normalized to ES-equivalent (÷10).
        /// </summary>
        public double? PLRealized { get; set; }
    }

    /// <summary>Well-known leg type values.</summary>
    public static class LegTypes
    {
        public const string Entry        = "Entry";
        public const string PartialExit  = "PartialExit";
        public const string StopOut      = "StopOut";
        public const string AddOn        = "AddOn";
        public const string FinalExit    = "FinalExit";
    }
}
