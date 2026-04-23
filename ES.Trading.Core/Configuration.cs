namespace ES.Trading.Core.Models
{
    public class ConfigurationEntry
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    /// <summary>
    /// Well-known configuration keys. All values are stored as strings in SQLite
    /// and parsed at read time. Use ConfigurationRepository.Get&lt;T&gt; for typed access.
    /// </summary>
    public static class ConfigKeys
    {
        /// <summary>Dollar amount that triggers the daily loss limit alert. Default: 800.</summary>
        public const string DailyLossLimitDollars = "DailyLossLimitDollars";

        /// <summary>Max trade attempts before shutdown alert fires. Default: 4.</summary>
        public const string MaxAttempts = "MaxAttempts";

        /// <summary>Default stop in ticks — informational reference only. Default: 5.</summary>
        public const string FixedStopTicks = "FixedStopTicks";

        /// <summary>Handles per rotation level measured from the OR High/Low. Default: 15.</summary>
        public const string RotationHandles = "RotationHandles";

        /// <summary>Handles at which the "pay for the trade" partial exit is taken. Default: 4.</summary>
        public const string PartialProfitHandles = "PartialProfitHandles";

        /// <summary>Duration of OR capture window in seconds from 9:30:00. Default: 30.</summary>
        public const string ORWindowSeconds = "ORWindowSeconds";

        /// <summary>Whether audio alerts are enabled in the NT8 panel. Default: true.</summary>
        public const string AlertSoundEnabled = "AlertSoundEnabled";

        /// <summary>Rolling window in days for the discipline score average. Default: 20.</summary>
        public const string DisciplineRollingDays = "DisciplineRollingDays";

        /// <summary>Whether the NT8 panel window stays above other windows. Default: false.</summary>
        public const string PanelAlwaysOnTop = "PanelAlwaysOnTop";
    }
}
