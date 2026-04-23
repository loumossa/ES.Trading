using System;
using NinjaTrader.Data;

namespace ES.Trading.NTAddon.Services
{
    /// <summary>
    /// Tracks the Opening Range for the current session.
    /// 
    /// The OR is defined as the high and low of the first 30 seconds of RTH:
    /// 9:30:00.000 to 9:30:29.999 ET. At 9:30:30 the OR is locked and never changes.
    ///
    /// Usage:
    ///   1. Call Reset() at the start of each session (or on Add-On load).
    ///   2. Feed every real-time tick via OnMarketData().
    ///   3. Subscribe to ORLocked to be notified when the window closes.
    ///   4. Read High, Low, FirstRotationLevel, SecondRotationLevel after lock.
    /// </summary>
    public class ORCalculator
    {
        // ─── Configuration ────────────────────────────────────────────────────────

        private readonly int _windowSeconds;        // default 30
        private readonly double _rotationHandles;   // default 15

        // ─── State ────────────────────────────────────────────────────────────────

        private bool   _capturing;
        private bool   _locked;
        private double _high = double.MinValue;
        private double _low  = double.MaxValue;

        // ─── Public state ─────────────────────────────────────────────────────────

        public bool   IsLocked  => _locked;
        public bool   IsActive  => _capturing && !_locked;
        public double? High     => _locked ? _high : null;
        public double? Low      => _locked ? _low  : null;

        /// <summary>
        /// The price at which a long bias is established (above OR High).
        /// Null until OR is locked.
        /// </summary>
        public double? BiasLongAbove => High;

        /// <summary>
        /// The price at which a short bias is established (below OR Low).
        /// Null until OR is locked.
        /// </summary>
        public double? BiasShortBelow => Low;

        /// <summary>First rotation target: OR High + rotationHandles (long) or OR Low - rotationHandles (short).</summary>
        public double? FirstRotationLong  => High.HasValue ? High + _rotationHandles : null;
        public double? FirstRotationShort => Low.HasValue  ? Low  - _rotationHandles : null;

        /// <summary>Second rotation target (add-on exits): 2× rotation from OR High/Low.</summary>
        public double? SecondRotationLong  => High.HasValue ? High + (_rotationHandles * 2) : null;
        public double? SecondRotationShort => Low.HasValue  ? Low  - (_rotationHandles * 2) : null;

        /// <summary>Fires once when the OR window closes at 9:30:30.</summary>
        public event Action<double, double>? ORLocked;

        // ─── Constructor ──────────────────────────────────────────────────────────

        public ORCalculator(int windowSeconds = 30, double rotationHandles = 15)
        {
            _windowSeconds   = windowSeconds;
            _rotationHandles = rotationHandles;
        }

        // ─── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Resets state for a new session. Call once per day before 9:30.
        /// </summary>
        public void Reset()
        {
            _capturing = false;
            _locked    = false;
            _high      = double.MinValue;
            _low       = double.MaxValue;
        }

        /// <summary>
        /// Feed each incoming tick. Handles capture window open/close automatically
        /// based on the tick timestamp (ET assumed — NT8 provides local exchange time).
        /// </summary>
        public void OnMarketData(MarketDataEventArgs e)
        {
            if (_locked) return;

            // Only process Last (trade) prices — not bid/ask
            if (e.MarketDataType != MarketDataType.Last) return;
            if (e.Price <= 0) return;

            var time = e.Time;  // NT8 provides exchange-local time

            // Open the capture window at 9:30:00
            if (!_capturing && IsOROpen(time))
                _capturing = true;

            // Close and lock the window at 9:30:30
            if (_capturing && IsORClosed(time))
            {
                LockOR();
                return;
            }

            if (!_capturing) return;

            // Track high and low within the window
            if (e.Price > _high) _high = e.Price;
            if (e.Price < _low)  _low  = e.Price;
        }

        /// <summary>
        /// Call this if you want to manually lock the OR (e.g. from a timer callback
        /// at exactly 9:30:30 rather than waiting for the next tick).
        /// No-op if already locked or no data was captured.
        /// </summary>
        public void Forcelock()
        {
            if (_locked || !_capturing) return;
            if (_high == double.MinValue || _low == double.MaxValue) return;
            LockOR();
        }

        // ─── Private ──────────────────────────────────────────────────────────────

        private void LockOR()
        {
            _locked    = true;
            _capturing = false;

            // Guard against pathological cases (e.g. only one tick)
            if (_high < _low) _high = _low;

            ORLocked?.Invoke(_high, _low);
        }

        private bool IsOROpen(DateTime time)
        {
            // 9:30:00 ET
            return time.Hour == 9 && time.Minute == 30 && time.Second == 0;
        }

        private bool IsORClosed(DateTime time)
        {
            // At or past 9:30:{windowSeconds}
            if (time.Hour != 9 || time.Minute != 30) return false;
            return time.Second >= _windowSeconds;
        }
    }
}
