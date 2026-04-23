using System.Collections.Generic;

namespace ES.Trading.Core.Models
{
    public class DisciplineCheck
    {
        public int Id { get; set; }

        /// <summary>Null for day-level rules (e.g. MaxAttemptsRespected).</summary>
        public int? TradeId { get; set; }

        /// <summary>Always set — links to the trading day this check belongs to.</summary>
        public int DayId { get; set; }

        /// <summary>Use DisciplineRules constants. Identifies which rule this row evaluates.</summary>
        public string RuleKey { get; set; } = string.Empty;

        /// <summary>True = rule was followed, False = rule was violated.</summary>
        public bool Passed { get; set; }

        public string? Notes { get; set; }
    }

    /// <summary>
    /// The four rules that contribute to the discipline score.
    /// Scoped indicates whether the check applies per-trade or per-day.
    /// </summary>
    public static class DisciplineRules
    {
        /// <summary>
        /// Per-trade. Did the trader wait for a valid OR break before entering?
        /// Not applicable to RotationEntry setup types (those are fallback entries).
        /// </summary>
        public const string WaitedForORBreak = "WaitedForORBreak";

        /// <summary>
        /// Per-trade. Was the original stop honored — i.e., not manually moved wider?
        /// Detected by comparing actual stop-out price against the recorded StopPrice.
        /// </summary>
        public const string HonoredStop = "HonoredStop";

        /// <summary>
        /// Per-day. Did the trader stop after hitting the max attempt count?
        /// Fails if AttemptCount exceeds MaxAttempts in Configuration.
        /// </summary>
        public const string MaxAttemptsRespected = "MaxAttemptsRespected";

        /// <summary>
        /// Per-trade (add-on trades only). Was the add-on taken at 1-2 handles beyond
        /// the rotation, with the stop placed correctly at the rotation entry point?
        /// Only applicable when SetupType == "AddOn".
        /// </summary>
        public const string AddOnRulesFollowed = "AddOnRulesFollowed";

        /// <summary>All four scored rules, in display order.</summary>
        public static IReadOnlyList<string> All => new[]
        {
            WaitedForORBreak,
            HonoredStop,
            MaxAttemptsRespected,
            AddOnRulesFollowed
        };

        /// <summary>Human-readable label for each rule key, for UI display.</summary>
        public static string GetLabel(string ruleKey) => ruleKey switch
        {
            WaitedForORBreak    => "Waited for valid OR break",
            HonoredStop         => "Honored stop without moving it",
            MaxAttemptsRespected => "Stopped after max attempts",
            AddOnRulesFollowed  => "Followed add-on rules",
            _                   => ruleKey
        };
    }
}
