using System;
using System.IO;
using System.Media;
using NinjaTrader.Gui;

namespace ES.Trading.NTAddon.Services
{
    /// <summary>
    /// Monitors SessionState on each price tick and fires alerts when thresholds are crossed.
    ///
    /// Alert types:
    ///   - Warning: approaching a limit (80% of loss limit, 1 attempt remaining)
    ///   - Critical: limit reached (loss limit hit, max attempts hit)
    ///   - Informational: OR locked, rotation levels touched, OR High/Low broken
    ///
    /// All alerts are warn-only — no order blocking, no enforcement.
    /// Each alert fires at most once per session (tracked via SessionState flags).
    /// </summary>
    public class AlertService
    {
        private readonly SessionState _state;

        // ─── Events ───────────────────────────────────────────────────────────────

        /// <summary>Fires when an alert is raised. Payload: (message, severity).</summary>
        public event Action<string, AlertSeverity>? AlertRaised;

        // ─── Constructor ──────────────────────────────────────────────────────────

        public AlertService(SessionState state)
        {
            _state = state;
        }

        // ─── Called on each price tick ────────────────────────────────────────────

        /// <summary>
        /// Evaluates all thresholds against current state and fires any new alerts.
        /// Call this after SessionState.LastPrice and CumulativePL are updated.
        /// </summary>
        public void Evaluate()
        {
            CheckLossLimit();
            CheckAttemptCount();
            CheckLevelTouches();
        }

        /// <summary>
        /// Called by ExecutionListener when a new trade attempt is registered.
        /// Separates attempt-count logic from price-tick logic for clarity.
        /// </summary>
        public void OnAttemptIncremented()
        {
            CheckAttemptCount();
        }

        // ─── OR event triggers (called directly, not on every tick) ──────────────

        public void OnORLocked(double high, double low)
        {
            Raise($"OR locked — High: {high:F2}  Low: {low:F2}  Range: {high - low:F2} handles",
                AlertSeverity.Info);
        }

        // ─── Private evaluation logic ─────────────────────────────────────────────

        private void CheckLossLimit()
        {
            if (_state.DailyLossLimitHit) return;

            var limit = _state.DailyLossLimitDollars;
            var loss  = -_state.CumulativePL;   // positive = loss

            // Critical: limit reached
            if (loss >= limit && !_state.FiredLossLimitAlert)
            {
                _state.DailyLossLimitHit  = true;
                _state.FiredLossLimitAlert = true;
                Raise($"DAILY LOSS LIMIT HIT — ${loss:F0} loss (limit: ${limit:F0}). Shut it down.",
                    AlertSeverity.Critical);
                return;
            }

            // Warning: 80% of limit reached
            if (loss >= limit * 0.8 && !_state.WarnedApproachingLossLimit)
            {
                _state.WarnedApproachingLossLimit = true;
                Raise($"Approaching daily loss limit — ${loss:F0} of ${limit:F0} used.",
                    AlertSeverity.Warning);
            }
        }

        private void CheckAttemptCount()
        {
            if (_state.MaxAttemptsHit) return;

            var count = _state.AttemptCount;
            var max   = _state.MaxAttempts;

            // Critical: max attempts reached
            if (count >= max && !_state.FiredMaxAttemptsAlert)
            {
                _state.MaxAttemptsHit        = true;
                _state.FiredMaxAttemptsAlert = true;
                Raise($"MAX ATTEMPTS REACHED ({count}/{max}). Done for the day.",
                    AlertSeverity.Critical);
                return;
            }

            // Warning: 1 attempt remaining
            if (count == max - 1 && !_state.WarnedApproachingMaxAttempts)
            {
                _state.WarnedApproachingMaxAttempts = true;
                Raise($"1 attempt remaining ({count}/{max}).",
                    AlertSeverity.Warning);
            }
        }

        private void CheckLevelTouches()
        {
            if (!_state.ORIsLocked || _state.LastPrice <= 0) return;

            var price = _state.LastPrice;

            // OR High break (long bias confirmed)
            if (!_state.AlertedORHighBreak && _state.ORHigh.HasValue
                && price > _state.ORHigh.Value)
            {
                _state.AlertedORHighBreak = true;
                Raise($"OR High broken ({_state.ORHigh:F2}) — Long bias active.", AlertSeverity.Info);
            }

            // OR Low break (short bias confirmed)
            if (!_state.AlertedORLowBreak && _state.ORLow.HasValue
                && price < _state.ORLow.Value)
            {
                _state.AlertedORLowBreak = true;
                Raise($"OR Low broken ({_state.ORLow:F2}) — Short bias active.", AlertSeverity.Info);
            }

            // First rotation long
            if (!_state.AlertedFirstRotationLong && _state.FirstRotationLong.HasValue
                && price >= _state.FirstRotationLong.Value)
            {
                _state.AlertedFirstRotationLong = true;
                Raise($"First rotation long reached ({_state.FirstRotationLong:F2}) — approach target.",
                    AlertSeverity.Info);
            }

            // First rotation short
            if (!_state.AlertedFirstRotationShort && _state.FirstRotationShort.HasValue
                && price <= _state.FirstRotationShort.Value)
            {
                _state.AlertedFirstRotationShort = true;
                Raise($"First rotation short reached ({_state.FirstRotationShort:F2}) — approach target.",
                    AlertSeverity.Info);
            }

            // Second rotation long
            if (!_state.AlertedSecondRotationLong && _state.SecondRotationLong.HasValue
                && price >= _state.SecondRotationLong.Value)
            {
                _state.AlertedSecondRotationLong = true;
                Raise($"Second rotation long reached ({_state.SecondRotationLong:F2}) — final target.",
                    AlertSeverity.Info);
            }

            // Second rotation short
            if (!_state.AlertedSecondRotationShort && _state.SecondRotationShort.HasValue
                && price <= _state.SecondRotationShort.Value)
            {
                _state.AlertedSecondRotationShort = true;
                Raise($"Second rotation short reached ({_state.SecondRotationShort:F2}) — final target.",
                    AlertSeverity.Info);
            }
        }

        private void Raise(string message, AlertSeverity severity)
        {
            AlertRaised?.Invoke(message, severity);

            if (_state.AlertSoundEnabled)
                PlaySound(severity);
        }

        private static void PlaySound(AlertSeverity severity)
        {
            try
            {
                // NT8 ships with alert sounds under its installation directory.
                // Fall back to system sounds if NT8 sounds are unavailable.
                switch (severity)
                {
                    case AlertSeverity.Critical:
                        SystemSounds.Hand.Play();
                        break;
                    case AlertSeverity.Warning:
                        SystemSounds.Exclamation.Play();
                        break;
                    default:
                        SystemSounds.Asterisk.Play();
                        break;
                }
            }
            catch
            {
                // Never let a sound failure crash the Add-On
            }
        }
    }

    public enum AlertSeverity
    {
        Info,
        Warning,
        Critical
    }
}
