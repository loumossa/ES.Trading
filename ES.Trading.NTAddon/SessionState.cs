using System;
using ES.Trading.Core.Models;

namespace ES.Trading.NTAddon.Services
{
    /// <summary>
    /// Holds all mutable intraday state for the current session.
    /// 
    /// The Add-On's services read/write this; the WPF panel binds to it.
    /// Kept as a plain class (not a ViewModel) so it can be used in both
    /// the service layer and the UI layer without circular dependencies.
    ///
    /// Reset() is called each morning when the Add-On initializes for the day.
    /// </summary>
    public class SessionState
    {
        // ─── Health ───────────────────────────────────────────────────────────────

        /// <summary>
        /// False when AddOn initialization or runtime setup failed. The WPF panel
        /// surfaces this as a red banner so a silent broken state isn't possible.
        /// </summary>
        public bool IsHealthy { get; set; } = true;

        /// <summary>Short user-facing reason why <see cref="IsHealthy"/> is false.</summary>
        public string? InitErrorMessage { get; set; }

        // ─── Day ──────────────────────────────────────────────────────────────────

        public TradingDay? CurrentDay { get; set; }

        public bool IsNoTradeDay { get; set; }

        /// <summary>Cumulative realized P&L for the session so far.</summary>
        public double CumulativePL { get; set; }

        /// <summary>Number of trade attempts taken today (1-based, incremented at each entry).</summary>
        public int AttemptCount { get; set; }

        public bool DailyLossLimitHit { get; set; }
        public bool MaxAttemptsHit { get; set; }

        /// <summary>Pre-session mood rating entered by the trader (1–5). Null until set.</summary>
        public int? MoodRating { get; set; }

        // ─── OR ───────────────────────────────────────────────────────────────────

        public bool ORIsLocked { get; set; }
        public double? ORHigh { get; set; }
        public double? ORLow { get; set; }

        /// <summary>Convenience: OR range in handles. Null until OR is locked.</summary>
        public double? ORRange => (ORHigh.HasValue && ORLow.HasValue)
            ? ORHigh.Value - ORLow.Value
            : null;

        // ─── Levels ───────────────────────────────────────────────────────────────

        public double? FirstRotationLong  { get; set; }
        public double? FirstRotationShort { get; set; }
        public double? SecondRotationLong  { get; set; }
        public double? SecondRotationShort { get; set; }

        // ─── Current price ────────────────────────────────────────────────────────

        public double LastPrice { get; set; }

        /// <summary>
        /// Derived directional bias based on last price vs OR.
        /// "Long" if above OR High, "Short" if below OR Low, "Neutral" if inside OR.
        /// "Unknown" before OR is locked.
        /// </summary>
        public string Bias
        {
            get
            {
                if (!ORIsLocked || ORHigh == null || ORLow == null || LastPrice <= 0)
                    return "Unknown";

                if (LastPrice > ORHigh.Value) return "Long";
                if (LastPrice < ORLow.Value)  return "Short";
                return "Neutral";
            }
        }

        // ─── Thresholds (loaded from Configuration at startup) ────────────────────

        public double DailyLossLimitDollars { get; set; } = 800;
        public int    MaxAttempts           { get; set; } = 4;
        public double RotationHandles       { get; set; } = 15;
        public double PartialProfitHandles  { get; set; } = 4;
        public bool   AlertSoundEnabled     { get; set; } = true;

        // ─── Alert tracking (prevent repeated firing) ─────────────────────────────

        public bool WarnedApproachingLossLimit { get; set; }
        public bool WarnedApproachingMaxAttempts { get; set; }
        public bool FiredLossLimitAlert { get; set; }
        public bool FiredMaxAttemptsAlert { get; set; }

        // ─── Level touch tracking (prevent repeated firing per level) ─────────────

        public bool AlertedFirstRotationLong   { get; set; }
        public bool AlertedFirstRotationShort  { get; set; }
        public bool AlertedSecondRotationLong  { get; set; }
        public bool AlertedSecondRotationShort { get; set; }
        public bool AlertedORHighBreak { get; set; }
        public bool AlertedORLowBreak  { get; set; }

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        /// <summary>Resets all intraday state. Call once per session at Add-On init.</summary>
        public void Reset()
        {
            CurrentDay    = null;
            IsNoTradeDay  = false;
            CumulativePL  = 0;
            AttemptCount  = 0;
            DailyLossLimitHit   = false;
            MaxAttemptsHit      = false;
            MoodRating    = null;

            ORIsLocked    = false;
            ORHigh        = null;
            ORLow         = null;

            FirstRotationLong   = null;
            FirstRotationShort  = null;
            SecondRotationLong  = null;
            SecondRotationShort = null;

            LastPrice = 0;

            WarnedApproachingLossLimit    = false;
            WarnedApproachingMaxAttempts  = false;
            FiredLossLimitAlert           = false;
            FiredMaxAttemptsAlert         = false;

            AlertedFirstRotationLong   = false;
            AlertedFirstRotationShort  = false;
            AlertedSecondRotationLong  = false;
            AlertedSecondRotationShort = false;
            AlertedORHighBreak = false;
            AlertedORLowBreak  = false;
        }
    }
}
