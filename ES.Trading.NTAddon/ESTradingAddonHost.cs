using System;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using ES.Trading.NTAddon.Services;

namespace ES.Trading.NTAddon
{
    // Plain host class — instantiated by the NinjaScript stub that lives in
    // Documents\NinjaTrader 8\bin\Custom\AddOns\ESTradingAddon.cs.
    // Does not inherit from AddOnBase so it can be compiled outside NT8's
    // NinjaScript build (AddOnBase types must live in NinjaTrader.Custom.dll
    // for NT8 to discover them).
    public class ESTradingAddonHost
    {
        // ─── Core dependencies ────────────────────────────────────────────────────

        private ES.Trading.Core.Data.DatabaseContext?         _db;
        private ES.Trading.Core.Data.TradeRepository?         _tradeRepo;
        private ES.Trading.Core.Data.DisciplineRepository?    _disciplineRepo;
        private ES.Trading.Core.Data.ConfigurationRepository? _configRepo;

        // ─── Services ─────────────────────────────────────────────────────────────

        private SessionState?      _state;
        private ORCalculator?      _orCalc;
        private AlertService?      _alertService;
        private ExecutionListener? _executionListener;

        // ─── NT8 resources ────────────────────────────────────────────────────────

        private Account? _account;

        // ─── Panel ────────────────────────────────────────────────────────────────

        private ESTradingWindow? _window;

        // ─── Logging ──────────────────────────────────────────────────────────────

        private Action<string> _log = _ => { };

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        public void Initialize(Action<string> log)
        {
            _log = log ?? (_ => { });

            try
            {
                InitializeCore();
                InitializeServices();
                SubscribeAccount();
                OpenPanel();
            }
            catch (Exception ex)
            {
                _log($"[ES.Trading] Add-On initialization failed: {ex}");
            }
        }

        public void Shutdown()
        {
            try
            {
                _executionListener?.Dispose();
                _window?.Dispatcher.InvokeAsync(() => _window.Close());
            }
            catch { /* Swallow on shutdown */ }
        }

        // ─── Initialization ───────────────────────────────────────────────────────

        private void InitializeCore()
        {
            string dbPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NinjaTrader 8", "ES.Trading", "es_trading.db");

            System.IO.Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(dbPath)!);

            _db = new ES.Trading.Core.Data.DatabaseContext(dbPath);
            _db.EnsureCreated();

            _tradeRepo      = new ES.Trading.Core.Data.TradeRepository(_db);
            _disciplineRepo = new ES.Trading.Core.Data.DisciplineRepository(_db);
            _configRepo     = new ES.Trading.Core.Data.ConfigurationRepository(_db);
        }

        private void InitializeServices()
        {
            int    windowSecs      = _configRepo!.GetOrDefault(ES.Trading.Core.Models.ConfigKeys.ORWindowSeconds,       30);
            double rotationHandles = _configRepo!.GetOrDefault(ES.Trading.Core.Models.ConfigKeys.RotationHandles,       15.0);
            double lossLimit       = _configRepo!.GetOrDefault(ES.Trading.Core.Models.ConfigKeys.DailyLossLimitDollars, 800.0);
            int    maxAttempts     = _configRepo!.GetOrDefault(ES.Trading.Core.Models.ConfigKeys.MaxAttempts,           4);
            double partialProfit   = _configRepo!.GetOrDefault(ES.Trading.Core.Models.ConfigKeys.PartialProfitHandles,  4.0);
            bool   soundEnabled    = _configRepo!.GetOrDefault(ES.Trading.Core.Models.ConfigKeys.AlertSoundEnabled,     true);

            _state = new SessionState
            {
                DailyLossLimitDollars = lossLimit,
                MaxAttempts           = maxAttempts,
                RotationHandles       = rotationHandles,
                PartialProfitHandles  = partialProfit,
                AlertSoundEnabled     = soundEnabled
            };
            _state.Reset();

            _state.CurrentDay        = _tradeRepo!.GetOrCreateToday();
            _state.CumulativePL      = _state.CurrentDay.DailyPL;
            _state.AttemptCount      = _state.CurrentDay.AttemptCount;
            _state.IsNoTradeDay      = _state.CurrentDay.IsNoTradeDay;
            _state.DailyLossLimitHit = _state.CurrentDay.DailyLossLimitHit;

            _orCalc = new ORCalculator(windowSecs, rotationHandles);
            _orCalc.ORLocked += OnORLocked;

            _alertService = new AlertService(_state);
            _alertService.AlertRaised += OnAlertRaised;
        }

        private void SubscribeAccount()
        {
            _account = Account.All.FirstOrDefault(a => a.Name != "Sim101")
                    ?? Account.All.FirstOrDefault();

            if (_account == null)
            {
                _log("[ES.Trading] No account found.");
                return;
            }

            _executionListener = new ExecutionListener(
                _account, _tradeRepo!, _disciplineRepo!, _state!, _alertService!);

            _executionListener.StateChanged += () =>
                _window?.Dispatcher.InvokeAsync(() => _window.RefreshState(_state!));
        }

        private void OpenPanel()
        {
            NinjaTrader.Core.Globals.RandomDispatcher.InvokeAsync(() =>
            {
                _window = new ESTradingWindow(
                    _state!, _alertService!, _tradeRepo!, _configRepo!);

                _window.ForceLockRequested += () => _orCalc?.Forcelock();
                _window.Show();
            });
        }

        // ─── BarsRequest tick handler (forwarded from NinjaScript stub) ───────────

        public void OnBarsUpdate(object sender, BarsUpdateEventArgs e)
        {
            try
            {
                if (_state == null || _orCalc == null || _alertService == null) return;

                var bars = e.BarsSeries;
                if (bars == null || bars.Count == 0) return;

                double   lastPrice = bars.GetClose(bars.Count - 1);
                DateTime lastTime  = bars.GetTime(bars.Count - 1);

                var mdArgs = new MarketDataEventArgs(
                    lastPrice,           // last
                    0,                   // ask
                    0,                   // bid
                    bars.Instrument,     // instrument
                    false,               // isReset
                    MarketDataType.Last, // marketDataType
                    lastPrice,           // price
                    lastTime,            // time
                    0,                   // volume
                    0);                  // tickId

                _orCalc.OnMarketData(mdArgs);

                if (lastPrice > 0)
                {
                    _state.LastPrice = lastPrice;
                    _alertService.Evaluate();
                    _window?.Dispatcher.InvokeAsync(() => _window.RefreshPrice(_state.LastPrice));
                }
            }
            catch (Exception ex)
            {
                _log($"[ES.Trading] BarsUpdate handler error: {ex.Message}");
            }
        }

        // ─── OR locked callback ───────────────────────────────────────────────────

        private void OnORLocked(double high, double low)
        {
            if (_state == null || _orCalc == null) return;

            _state.ORIsLocked          = true;
            _state.ORHigh              = high;
            _state.ORLow               = low;
            _state.FirstRotationLong   = _orCalc.FirstRotationLong;
            _state.FirstRotationShort  = _orCalc.FirstRotationShort;
            _state.SecondRotationLong  = _orCalc.SecondRotationLong;
            _state.SecondRotationShort = _orCalc.SecondRotationShort;

            if (_state.CurrentDay != null)
            {
                _state.CurrentDay.ORHigh = high;
                _state.CurrentDay.ORLow  = low;
                _tradeRepo!.UpdateTradingDay(_state.CurrentDay);
            }

            _alertService!.OnORLocked(high, low);
            _window?.Dispatcher.InvokeAsync(() => _window.RefreshState(_state));
        }

        private void OnAlertRaised(string message, AlertSeverity severity)
        {
            _window?.Dispatcher.InvokeAsync(() => _window.AddAlert(message, severity));
        }

        // ─── Force-lock access for stub (optional — if stub wants to proxy UI events) ─

        public void ForceLockOR() => _orCalc?.Forcelock();
    }
}
