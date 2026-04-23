using System.Windows;
using ES.Trading.Core.Data;
using ES.Trading.Core.Models;

namespace ES.Trading.DesktopApp.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ConfigurationRepository _configRepo;

        public RelayCommand SaveCommand { get; }

        public SettingsViewModel(ConfigurationRepository configRepo)
        {
            _configRepo = configRepo;
            SaveCommand = new RelayCommand(Save);
            Load();
        }

        // ─── Risk limits ─────────────────────────────────────────────────────────

        private double _dailyLossLimitDollars;
        public double DailyLossLimitDollars
        {
            get => _dailyLossLimitDollars;
            set => SetField(ref _dailyLossLimitDollars, value);
        }

        private int _maxAttempts;
        public int MaxAttempts
        {
            get => _maxAttempts;
            set => SetField(ref _maxAttempts, value);
        }

        private int _fixedStopTicks;
        public int FixedStopTicks
        {
            get => _fixedStopTicks;
            set => SetField(ref _fixedStopTicks, value);
        }

        // ─── OR & levels ─────────────────────────────────────────────────────────

        private int _orWindowSeconds;
        public int ORWindowSeconds
        {
            get => _orWindowSeconds;
            set => SetField(ref _orWindowSeconds, value);
        }

        private double _rotationHandles;
        public double RotationHandles
        {
            get => _rotationHandles;
            set => SetField(ref _rotationHandles, value);
        }

        private double _partialProfitHandles;
        public double PartialProfitHandles
        {
            get => _partialProfitHandles;
            set => SetField(ref _partialProfitHandles, value);
        }

        // ─── Alerts ──────────────────────────────────────────────────────────────

        private bool _alertSoundEnabled;
        public bool AlertSoundEnabled
        {
            get => _alertSoundEnabled;
            set => SetField(ref _alertSoundEnabled, value);
        }

        private bool _panelAlwaysOnTop;
        public bool PanelAlwaysOnTop
        {
            get => _panelAlwaysOnTop;
            set => SetField(ref _panelAlwaysOnTop, value);
        }

        // ─── Discipline ──────────────────────────────────────────────────────────

        private int _disciplineRollingDays;
        public int DisciplineRollingDays
        {
            get => _disciplineRollingDays;
            set => SetField(ref _disciplineRollingDays, value);
        }

        // ─── Load / save ─────────────────────────────────────────────────────────

        private void Load()
        {
            DailyLossLimitDollars = _configRepo.GetOrDefault(ConfigKeys.DailyLossLimitDollars, 800.0);
            MaxAttempts           = _configRepo.GetOrDefault(ConfigKeys.MaxAttempts, 4);
            FixedStopTicks        = _configRepo.GetOrDefault(ConfigKeys.FixedStopTicks, 5);
            ORWindowSeconds       = _configRepo.GetOrDefault(ConfigKeys.ORWindowSeconds, 30);
            RotationHandles       = _configRepo.GetOrDefault(ConfigKeys.RotationHandles, 15.0);
            PartialProfitHandles  = _configRepo.GetOrDefault(ConfigKeys.PartialProfitHandles, 4.0);
            AlertSoundEnabled     = _configRepo.GetOrDefault(ConfigKeys.AlertSoundEnabled, true);
            DisciplineRollingDays = _configRepo.GetOrDefault(ConfigKeys.DisciplineRollingDays, 20);
            PanelAlwaysOnTop      = _configRepo.GetOrDefault(ConfigKeys.PanelAlwaysOnTop, false);
        }

        private void Save()
        {
            _configRepo.Set(ConfigKeys.DailyLossLimitDollars, DailyLossLimitDollars);
            _configRepo.Set(ConfigKeys.MaxAttempts,           MaxAttempts);
            _configRepo.Set(ConfigKeys.FixedStopTicks,        FixedStopTicks);
            _configRepo.Set(ConfigKeys.ORWindowSeconds,       ORWindowSeconds);
            _configRepo.Set(ConfigKeys.RotationHandles,       RotationHandles);
            _configRepo.Set(ConfigKeys.PartialProfitHandles,  PartialProfitHandles);
            _configRepo.Set(ConfigKeys.AlertSoundEnabled,     AlertSoundEnabled);
            _configRepo.Set(ConfigKeys.DisciplineRollingDays, DisciplineRollingDays);
            _configRepo.Set(ConfigKeys.PanelAlwaysOnTop,      PanelAlwaysOnTop);

            MessageBox.Show(
                "Settings saved. Restart the NT8 Add-On for changes to take effect.",
                "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
