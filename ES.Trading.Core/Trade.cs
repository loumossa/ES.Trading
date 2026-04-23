using System;
using System.Collections.Generic;
using System.Text.Json;

namespace ES.Trading.Core.Models
{
    public class Trade
    {
        public int Id { get; set; }
        public int DayId { get; set; }

        public DateTime EntryTime { get; set; }

        /// <summary>"ES" or "MES"</summary>
        public string Instrument { get; set; } = "ES";

        /// <summary>"Long" or "Short"</summary>
        public string Direction { get; set; } = string.Empty;

        public double EntryPrice { get; set; }
        public double? StopPrice { get; set; }

        /// <summary>Which attempt this is for the day (1-based).</summary>
        public int AttemptNumber { get; set; }

        /// <summary>"ORBreak", "RotationEntry", "AddOn"</summary>
        public string? SetupType { get; set; }

        /// <summary>
        /// True when this is an MES trade used as a fractional exit against an open ES position.
        /// Excluded from discipline scoring; netted against ES position for P&L (÷10).
        /// </summary>
        public bool IsMESFractionalExit { get; set; }

        /// <summary>Total realized P&L for this trade across all legs.</summary>
        public double? TotalPL { get; set; }

        public string? Notes { get; set; }

        /// <summary>Relative path to screenshot file under /screenshots folder.</summary>
        public string? ScreenshotPath { get; set; }

        /// <summary>
        /// Tags stored as a JSON array in SQLite. Use Tags property for typed access.
        /// Not mapped directly — use TagsJson for persistence.
        /// </summary>
        public string? TagsJson { get; set; }

        // Navigation — populated by repositories, not stored in this table
        public List<TradeLeg> Legs { get; set; } = new();
        public List<DisciplineCheck> DisciplineChecks { get; set; } = new();

        /// <summary>Deserializes TagsJson into a list of tag strings.</summary>
        public List<string> GetTags()
        {
            if (string.IsNullOrWhiteSpace(TagsJson))
                return new List<string>();

            try
            {
                return JsonSerializer.Deserialize<List<string>>(TagsJson) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>Serializes tags into TagsJson for persistence.</summary>
        public void SetTags(IEnumerable<string> tags)
        {
            TagsJson = JsonSerializer.Serialize(tags);
        }
    }

    /// <summary>
    /// Well-known tag values. Use these constants instead of magic strings
    /// to ensure consistency between NT8 Add-On and Desktop App.
    /// </summary>
    public static class TradeTags
    {
        public const string ValidSetup        = "ValidSetup";
        public const string ORBreakEntry      = "ORBreakEntry";
        public const string RotationAddOn     = "RotationAddOn";
        public const string FallbackRotation  = "FallbackRotation";
        public const string RevengeTrace      = "RevengeTrade";
        public const string OutsideRules      = "OutsideRules";
        public const string NewsDay           = "NewsDay";
        public const string NarrowOR          = "NarrowOR";

        public static IReadOnlyList<string> All => new[]
        {
            ValidSetup, ORBreakEntry, RotationAddOn, FallbackRotation,
            RevengeTrace, OutsideRules, NewsDay, NarrowOR
        };
    }

    /// <summary>Well-known setup type values.</summary>
    public static class SetupTypes
    {
        public const string ORBreak        = "ORBreak";
        public const string RotationEntry  = "RotationEntry";
        public const string AddOn          = "AddOn";
    }
}
