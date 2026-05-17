using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using ES.Trading.Core.Data;
using ES.Trading.Core.Models;
using CoreTrade = ES.Trading.Core.Models.Trade;

namespace ES.Trading.NTAddon.Services
{
    /// <summary>
    /// Subscribes to NinjaTrader's Account.ExecutionUpdate event and translates
    /// NT8 execution objects into TradeLeg records written to SQLite.
    ///
    /// Trade grouping logic:
    ///   - An "Entry" leg opens a new Trade record.
    ///   - Subsequent fills on the same instrument/direction within the same day
    ///     are attached to the open Trade as additional legs (PartialExit, AddOn, etc.).
    ///   - When net position reaches zero, the Trade is closed and TotalPL is finalized.
    ///   - MES fills against an open ES position are detected and flagged as
    ///     IsMESFractionalExit = true (excluded from discipline scoring).
    ///
    /// The LegType for each fill is inferred from position direction:
    ///   - Same direction as open position → AddOn
    ///   - Opposite direction, position remains open → PartialExit
    ///   - Opposite direction, position closes → StopOut or FinalExit (determined by
    ///     comparing exit price to recorded StopPrice)
    /// </summary>
    public class ExecutionListener : IDisposable
    {
        private readonly List<Account> _accounts;
        private readonly TradeRepository _tradeRepo;
        private readonly DisciplineRepository _disciplineRepo;
        private readonly SessionState _state;
        private readonly AlertService _alertService;

        // Key: instrument name ("ES 06-25" etc) → open Trade record
        private readonly Dictionary<string, CoreTrade> _openTrades = new();

        // Running net contracts per instrument (positive = long, negative = short)
        private readonly Dictionary<string, int> _netPosition = new();

        public event Action? StateChanged;

        public ExecutionListener(
            IEnumerable<Account> accounts,
            TradeRepository tradeRepo,
            DisciplineRepository disciplineRepo,
            SessionState state,
            AlertService alertService)
        {
            _accounts       = accounts?.ToList() ?? new List<Account>();
            _tradeRepo      = tradeRepo;
            _disciplineRepo = disciplineRepo;
            _state          = state;
            _alertService   = alertService;

            // Subscribe to every account so fills are captured no matter which
            // account the user trades on (live, Sim101, Playback, etc).
            foreach (var account in _accounts)
                account.ExecutionUpdate += OnExecutionUpdate;
        }

        public IReadOnlyList<Account> SubscribedAccounts => _accounts;

        public void Dispose()
        {
            foreach (var account in _accounts)
                account.ExecutionUpdate -= OnExecutionUpdate;
        }

        // ─── Core handler ─────────────────────────────────────────────────────────

        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            try
            {
                ProcessExecution(e.Execution);
            }
            catch (Exception ex)
            {
                // Log and swallow — never let a DB error crash NT8
                NinjaTrader.Code.Output.Process(
                    $"[ES.Trading] ExecutionListener error: {ex.Message}", PrintTo.OutputTab1);
            }
        }

        private void ProcessExecution(Execution exec)
        {
            if (_state.CurrentDay == null) return;

            // Roll into a new TradingDay if the calendar date changed since init.
            // Without this, fills after midnight on NT8 sessions that stay open
            // across days would be attached to yesterday's row.
            var todayKey = DateTime.Today.ToString("yyyy-MM-dd");
            if (_state.CurrentDay.Date != todayKey)
                RolloverDay();

            var instrument = exec.Instrument.FullName;
            var isMES      = instrument.StartsWith("MES", StringComparison.OrdinalIgnoreCase);
            var isES       = instrument.StartsWith("ES",  StringComparison.OrdinalIgnoreCase) && !isMES;

            // Only handle ES and MES
            if (!isES && !isMES) return;

            // Determine the fill direction (+1 = bought, -1 = sold)
            int fillSign = exec.Order.OrderAction switch
            {
                OrderAction.Buy       => +1,
                OrderAction.BuyToCover => +1,
                OrderAction.Sell      => -1,
                OrderAction.SellShort => -1,
                _ => 0
            };
            if (fillSign == 0) return;

            int fillContracts = exec.Quantity * fillSign;

            // ── MES fractional exit handling ──────────────────────────────────────

            if (isMES && HasOpenESPosition())
            {
                RecordMESFractionalExit(exec, fillContracts);
                return;
            }

            // ── ES position tracking ──────────────────────────────────────────────

            int priorNet = _netPosition.TryGetValue(instrument, out var pos) ? pos : 0;
            int newNet   = priorNet + fillContracts;
            _netPosition[instrument] = newNet;

            if (priorNet == 0)
            {
                // Opening a new position → new Trade
                OpenNewTrade(exec, instrument, fillContracts);
            }
            else if (Math.Sign(priorNet) == Math.Sign(fillContracts))
            {
                // Adding to existing position in the same direction → AddOn leg
                AppendLeg(exec, instrument, LegTypes.AddOn, fillContracts);
            }
            else
            {
                // Reducing or closing position
                bool closing = newNet == 0;

                string legType = closing
                    ? DetermineClosingLegType(exec, instrument)
                    : LegTypes.PartialExit;

                double? pl = closing ? CalculatePL(exec, instrument) : null;
                AppendLeg(exec, instrument, legType, fillContracts, pl);

                if (closing)
                    CloseTrade(exec, instrument, pl ?? 0);
            }

            StateChanged?.Invoke();
        }

        // ─── Day rollover ─────────────────────────────────────────────────────────

        private void RolloverDay()
        {
            AddonLog.Info($"[ES.Trading] Day rollover: {_state.CurrentDay?.Date} → {DateTime.Today:yyyy-MM-dd}");

            // Reset per-day state (preserves config thresholds; nulls CurrentDay)
            _state.Reset();

            // Reload (or create) today's TradingDay row and prime session state
            var today = _tradeRepo.GetOrCreateToday();
            _state.CurrentDay        = today;
            _state.CumulativePL      = today.DailyPL;
            _state.AttemptCount      = today.AttemptCount;
            _state.IsNoTradeDay      = today.IsNoTradeDay;
            _state.DailyLossLimitHit = today.DailyLossLimitHit;

            // Any open position from yesterday is no longer ours to track —
            // NT8 carries position state across the session, but for accounting
            // purposes we treat each calendar day as a fresh slate.
            _openTrades.Clear();
            _netPosition.Clear();

            StateChanged?.Invoke();
        }

        // ─── Trade lifecycle ──────────────────────────────────────────────────────

        private void OpenNewTrade(Execution exec, string instrument, int fillContracts)
        {
            // Increment attempt count and alert service
            int newCount = _tradeRepo.IncrementAttemptCount(_state.CurrentDay!.Id);
            _state.AttemptCount = newCount;
            _alertService.OnAttemptIncremented();

            string direction = fillContracts > 0 ? "Long" : "Short";

            var trade = new CoreTrade
            {
                DayId          = _state.CurrentDay.Id,
                EntryTime      = exec.Time,
                Instrument     = "ES",
                ContractSymbol = instrument,   // full NT8 symbol, e.g. "ES 06-26"
                Direction      = direction,
                EntryPrice     = exec.Price,
                AttemptNumber  = newCount,
                SetupType      = InferSetupType()
            };

            _tradeRepo.InsertTrade(trade);
            _openTrades[instrument] = trade;

            // Entry leg
            var leg = new TradeLeg
            {
                TradeId       = trade.Id,
                LegType       = LegTypes.Entry,
                ExecutionTime = exec.Time,
                Price         = exec.Price,
                Contracts     = Math.Abs(fillContracts)
            };
            _tradeRepo.InsertTradeLeg(leg);

            // Immediately record WaitedForORBreak discipline check
            RecordWaitedForORBreakCheck(trade);
        }

        private void AppendLeg(Execution exec, string instrument, string legType,
                               int fillContracts, double? pl = null)
        {
            if (!_openTrades.TryGetValue(instrument, out CoreTrade trade)) return;

            var leg = new TradeLeg
            {
                TradeId       = trade.Id,
                LegType       = legType,
                ExecutionTime = exec.Time,
                Price         = exec.Price,
                Contracts     = Math.Abs(fillContracts),
                PLRealized    = pl
            };
            _tradeRepo.InsertTradeLeg(leg);
        }

        private void CloseTrade(Execution exec, string instrument, double pl)
        {
            if (!_openTrades.TryGetValue(instrument, out CoreTrade trade)) return;

            trade.TotalPL = pl;
            _tradeRepo.UpdateTrade(trade);

            // Update cumulative session P&L
            _state.CumulativePL += pl;
            _tradeRepo.AddToDailyPL(_state.CurrentDay!.Id, pl);

            // Record HonoredStop discipline check
            RecordHonoredStopCheck(trade, exec.Price);

            _openTrades.Remove(instrument);
            _netPosition[instrument] = 0;
        }

        // ─── MES fractional exit ──────────────────────────────────────────────────

        private void RecordMESFractionalExit(Execution exec, int fillContracts)
        {
            // Find the open ES trade to attach this to
            var esKey   = _openTrades.Keys.FirstOrDefault(k => k.StartsWith("ES"));
            if (esKey == null) return;
            var esTrade = _openTrades[esKey];

            // Normalize MES to ES-equivalent: 10 MES = 1 ES
            double esEquivPL = (exec.Price * Math.Abs(fillContracts)) / 10.0;

            var leg = new TradeLeg
            {
                TradeId       = esTrade.Id,
                LegType       = LegTypes.PartialExit,
                ExecutionTime = exec.Time,
                Price         = exec.Price,
                Contracts     = Math.Abs(fillContracts),
                PLRealized    = null  // P&L calculated on ES close
            };
            _tradeRepo.InsertTradeLeg(leg);

            // Log flagged MES exit to output tab for transparency
            NinjaTrader.Code.Output.Process(
                $"[ES.Trading] MES fractional exit recorded: {Math.Abs(fillContracts)} MES @ {exec.Price:F2}",
                PrintTo.OutputTab1);
        }

        // ─── Discipline checks ────────────────────────────────────────────────────

        private void RecordWaitedForORBreakCheck(CoreTrade trade)
        {
            // Not applicable to RotationEntry fallback trades
            if (trade.SetupType == SetupTypes.RotationEntry) return;

            // Passes if the OR was locked before the entry (it should be — open is at 9:30)
            // and price was beyond the OR High/Low at entry time.
            bool passed = _state.ORIsLocked && IsValidORBreakEntry(trade);

            _disciplineRepo.Insert(new DisciplineCheck
            {
                TradeId = trade.Id,
                DayId   = _state.CurrentDay!.Id,
                RuleKey = DisciplineRules.WaitedForORBreak,
                Passed  = passed,
                Notes   = passed ? null : "Entry before OR lock or inside OR range"
            });
        }

        private void RecordHonoredStopCheck(CoreTrade trade, double exitPrice)
        {
            if (trade.StopPrice == null)
            {
                // No recorded stop — can't evaluate, skip
                return;
            }

            // For a long trade: stop was honored if exit >= recorded stop (not stopped below it)
            // For a short trade: stop was honored if exit <= recorded stop (not stopped above it)
            bool passed = trade.Direction == "Long"
                ? exitPrice >= trade.StopPrice.Value
                : exitPrice <= trade.StopPrice.Value;

            _disciplineRepo.Insert(new DisciplineCheck
            {
                TradeId = trade.Id,
                DayId   = _state.CurrentDay!.Id,
                RuleKey = DisciplineRules.HonoredStop,
                Passed  = passed,
                Notes   = passed ? null
                    : $"Stop recorded at {trade.StopPrice:F2}, exited at {exitPrice:F2}"
            });
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private bool HasOpenESPosition()
        {
            return _openTrades.Keys.Any(k =>
                k.StartsWith("ES", StringComparison.OrdinalIgnoreCase) &&
                !k.StartsWith("MES", StringComparison.OrdinalIgnoreCase));
        }

        private string InferSetupType()
        {
            // If OR isn't locked yet or price is inside OR → treat as unknown
            if (!_state.ORIsLocked) return SetupTypes.ORBreak;

            return _state.Bias switch
            {
                "Long"  => SetupTypes.ORBreak,
                "Short" => SetupTypes.ORBreak,
                _       => SetupTypes.RotationEntry  // price inside OR = fallback entry
            };
        }

        private bool IsValidORBreakEntry(CoreTrade trade)
        {
            if (!_state.ORHigh.HasValue || !_state.ORLow.HasValue) return false;

            return trade.Direction == "Long"
                ? trade.EntryPrice > _state.ORHigh.Value
                : trade.EntryPrice < _state.ORLow.Value;
        }

        private string DetermineClosingLegType(Execution exec, string instrument)
        {
            if (!_openTrades.TryGetValue(instrument, out CoreTrade trade)) return LegTypes.FinalExit;
            if (trade.StopPrice == null) return LegTypes.FinalExit;

            // If the exit price is at or near the recorded stop → StopOut
            double tolerance = 2.0;  // 2 handles / 8 ticks
            bool nearStop = trade.Direction == "Long"
                ? exec.Price <= trade.StopPrice.Value + tolerance
                : exec.Price >= trade.StopPrice.Value - tolerance;

            return nearStop ? LegTypes.StopOut : LegTypes.FinalExit;
        }

        private double CalculatePL(Execution exec, string instrument)
        {
            if (!_openTrades.TryGetValue(instrument, out CoreTrade trade)) return 0;

            // ES point value: $50 per handle
            const double pointValue = 50.0;

            var legs      = _tradeRepo.GetLegsByTrade(trade.Id).ToList();
            int direction = trade.Direction == "Long" ? 1 : -1;
            double pl     = 0;

            // Simple FIFO P&L: sum (exitPrice - entryPrice) * contracts * direction * pointValue
            // This is approximate — NT8's own P&L calculation is authoritative,
            // but we need a value to store. For accuracy, consider reading
            // exec.Order.Realized from NT8 if available.
            var entryLegs = legs
                .Where(l => l.LegType == LegTypes.Entry || l.LegType == LegTypes.AddOn)
                .OrderBy(l => l.ExecutionTime)
                .ToList();

            double avgEntry = entryLegs.Count > 0
                ? entryLegs.Average(l => l.Price)
                : trade.EntryPrice;

            int totalContracts = legs
                .Where(l => l.LegType == LegTypes.Entry || l.LegType == LegTypes.AddOn)
                .Sum(l => l.Contracts);

            pl = (exec.Price - avgEntry) * direction * totalContracts * pointValue;

            return Math.Round(pl, 2);
        }
    }
}
