using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ES.Trading.Core.Data;
using ES.Trading.Core.Models;
using ES.Trading.NTAddon.Services;

namespace ES.Trading.NTAddon
{
    public partial class ESTradingWindow : Window
    {
        private readonly SessionState           _state;
        private readonly AlertService           _alertService;
        private readonly TradeRepository        _tradeRepo;
        private readonly ConfigurationRepository _configRepo;

        private readonly DispatcherTimer _clockTimer;

        // Alert color map
        private static readonly Dictionary<AlertSeverity, Brush> AlertColors = new()
        {
            { AlertSeverity.Info,     new SolidColorBrush(Color.FromRgb(0x99, 0xCC, 0xFF)) },
            { AlertSeverity.Warning,  new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x55)) },
            { AlertSeverity.Critical, new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55)) },
        };

        public ESTradingWindow(
            SessionState state,
            AlertService alertService,
            TradeRepository tradeRepo,
            ConfigurationRepository configRepo)
        {
            InitializeComponent();

            _state        = state;
            _alertService = alertService;
            _tradeRepo    = tradeRepo;
            _configRepo   = configRepo;

            Topmost = _configRepo.GetOrDefault(ConfigKeys.PanelAlwaysOnTop, false);

            // Clock tick — updates time display and runs OR countdown
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += ClockTimer_Tick;
            _clockTimer.Start();

            // Load macro levels for today
            LoadMacroLevels();

            // Initial UI refresh
            RefreshState(_state);
        }

        // ─── Public refresh API (called from Add-On on background threads) ────────

        /// <summary>Full state refresh — call after any SessionState mutation.</summary>
        public void RefreshState(SessionState state)
        {
            // Health banner — shown first so the trader sees it before anything else
            if (!state.IsHealthy)
            {
                UnhealthyBanner.Visibility = Visibility.Visible;
                UnhealthyDetail.Text = state.InitErrorMessage ?? "Initialization failed.";
            }
            else
            {
                UnhealthyBanner.Visibility = Visibility.Collapsed;
            }

            // OR section
            if (state.ORIsLocked)
            {
                ORStatusLabel.Text      = "Locked ✓";
                ORStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0xCC, 0x88));
                ORHighLabel.Text        = state.ORHigh?.ToString("F2") ?? "—";
                ORLowLabel.Text         = state.ORLow?.ToString("F2")  ?? "—";
                ORRangeLabel.Text       = state.ORRange?.ToString("F2") ?? "—";

                Rot1LongLabel.Text  = state.FirstRotationLong?.ToString("F2")   ?? "—";
                Rot1ShortLabel.Text = state.FirstRotationShort?.ToString("F2")  ?? "—";
                Rot2LongLabel.Text  = state.SecondRotationLong?.ToString("F2")  ?? "—";
                Rot2ShortLabel.Text = state.SecondRotationShort?.ToString("F2") ?? "—";
            }
            else
            {
                ORStatusLabel.Text       = "Capturing...";
                ORStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x55));
            }

            // Session stats
            RefreshPL(state.CumulativePL);
            AttemptsLabel.Text = $"{state.AttemptCount} / {state.MaxAttempts}";
            AttemptsLabel.Foreground = state.MaxAttemptsHit
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55))
                : new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));

            // Loss limit progress bar
            double limitPct = state.DailyLossLimitDollars > 0
                ? Math.Min(100, Math.Max(0, -state.CumulativePL / state.DailyLossLimitDollars * 100))
                : 0;
            LossLimitBar.Value = limitPct;
            LossLimitBar.Foreground = limitPct >= 80
                ? new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55))
                : new SolidColorBrush(Color.FromRgb(0xCC, 0x88, 0x88));

            // No-trade day banner
            NoTradeDayBanner.Visibility = state.IsNoTradeDay ? Visibility.Visible : Visibility.Collapsed;
            NoTradeDayCheck.IsChecked   = state.IsNoTradeDay;

            // Bias label
            RefreshBias(state.Bias);
        }

        public void RefreshPrice(double price)
        {
            LastPriceLabel.Text = price.ToString("F2");
            RefreshBias(_state.Bias);
        }

        private void RefreshPL(double pl)
        {
            PLLabel.Text       = $"${pl:F2}";
            PLLabel.Foreground = pl >= 0
                ? new SolidColorBrush(Color.FromRgb(0x88, 0xCC, 0x88))
                : new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55));
        }

        private void RefreshBias(string bias)
        {
            (BiasLabel.Text, BiasLabel.Foreground) = bias switch
            {
                "Long"    => ("▲ LONG",    new SolidColorBrush(Color.FromRgb(0x88, 0xCC, 0x88))),
                "Short"   => ("▼ SHORT",   new SolidColorBrush(Color.FromRgb(0xFF, 0x55, 0x55))),
                "Neutral" => ("◆ NEUTRAL", new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x55))),
                _         => ("",          Brushes.Transparent)
            };
        }

        // ─── Alerts ───────────────────────────────────────────────────────────────

        public void AddAlert(string message, AlertSeverity severity)
        {
            var tb = new TextBlock
            {
                Text       = $"[{DateTime.Now:HH:mm:ss}] {message}",
                Style      = (Style)FindResource("AlertItem"),
                Foreground = AlertColors.TryGetValue(severity, out var brush) ? brush : Brushes.White
            };

            AlertsPanel.Children.Insert(0, tb);  // newest at top

            // Cap at 50 alerts in the panel
            while (AlertsPanel.Children.Count > 50)
                AlertsPanel.Children.RemoveAt(AlertsPanel.Children.Count - 1);

            AlertScroller.ScrollToTop();
        }

        private void ClearAlerts_Click(object sender, RoutedEventArgs e)
        {
            AlertsPanel.Children.Clear();
        }

        // ─── Clock timer ──────────────────────────────────────────────────────────

        private void ClockTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            ClockLabel.Text = now.ToString("HH:mm:ss");

            // Show OR countdown before 9:30:30
            if (!_state.ORIsLocked)
            {
                var orOpen  = now.Date.AddHours(9).AddMinutes(30);
                var orClose = orOpen.AddSeconds(30);

                if (now < orOpen)
                {
                    var countdown = orOpen - now;
                    ORStatusLabel.Text = $"Opens in {countdown:mm\\:ss}";
                }
                else if (now < orClose)
                {
                    var remaining = orClose - now;
                    ORStatusLabel.Text = $"Capturing ({remaining.Seconds}s)";
                    ORStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x55));

                    // Force lock if we're past the window and still not locked
                    // (handles the case where no tick arrived in the last second)
                    if (now >= orClose)
                        RequestForcelock();
                }
            }
        }

        private void RequestForcelock()
        {
            // Raise an event or call back to the Add-On to force-lock the OR
            // This is set from ESTradingAddon after the window is created
            ForceLockRequested?.Invoke();
        }

        public event Action? ForceLockRequested;

        // ─── Macro levels ─────────────────────────────────────────────────────────

        private void LoadMacroLevels()
        {
            var levels = _tradeRepo.GetTomorrowsMacroLevels().ToList();

            if (levels.Count == 0)
            {
                MacroLevelsList.ItemsSource = new[]
                {
                    new MacroLevel { Label = "No levels entered", Price = 0 }
                };
            }
            else
            {
                MacroLevelsList.ItemsSource = levels;
            }
        }

        // ─── Mood rating ──────────────────────────────────────────────────────────

        private void MoodButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            if (!int.TryParse(btn.Tag?.ToString(), out int rating)) return;

            _state.MoodRating = rating;

            if (_state.CurrentDay != null)
            {
                _state.CurrentDay.MoodRating = rating;
                _tradeRepo.UpdateTradingDay(_state.CurrentDay);
            }

            // Highlight selected mood button
            var panel = btn.Parent as StackPanel;
            if (panel == null) return;

            foreach (Button b in panel.Children.OfType<Button>())
            {
                b.Background = (string)b.Tag == rating.ToString()
                    ? new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0x99))
                    : new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            }
        }

        // ─── No-trade day checkbox ────────────────────────────────────────────────

        private void NoTradeDayCheck_Changed(object sender, RoutedEventArgs e)
        {
            bool isNoTradeDay = NoTradeDayCheck.IsChecked == true;
            _state.IsNoTradeDay = isNoTradeDay;

            NoTradeDayBanner.Visibility = isNoTradeDay ? Visibility.Visible : Visibility.Collapsed;

            if (_state.CurrentDay != null)
            {
                _state.CurrentDay.IsNoTradeDay = isNoTradeDay;
                _tradeRepo.UpdateTradingDay(_state.CurrentDay);
            }
        }

        // ─── Cleanup ──────────────────────────────────────────────────────────────

        protected override void OnClosed(EventArgs e)
        {
            _clockTimer.Stop();
            base.OnClosed(e);
        }
    }
}
