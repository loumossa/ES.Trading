using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using ES.Trading.Core.Data;
using ES.Trading.Core.Models;

namespace ES.Trading.DesktopApp.ViewModels
{
    public class MacroLevelsViewModel : ViewModelBase
    {
        private readonly TradeRepository _tradeRepo;

        // ─── State ────────────────────────────────────────────────────────────

        public ObservableCollection<MacroLevelRow> Levels { get; } = new();

        private string _targetDate;
        public string TargetDate
        {
            get => _targetDate;
            set
            {
                if (SetField(ref _targetDate, value))
                    LoadLevels();
            }
        }

        private string _newLabel = string.Empty;
        private string _newPrice = string.Empty;

        public string NewLabel { get => _newLabel; set => SetField(ref _newLabel, value); }
        public string NewPrice { get => _newPrice; set => SetField(ref _newPrice, value); }

        // ─── Commands ─────────────────────────────────────────────────────────

        public RelayCommand AddLevelCommand    { get; }
        public RelayCommand SaveAllCommand     { get; }
        public RelayCommand DeleteLevelCommand { get; }
        public RelayCommand NextDayCommand     { get; }
        public RelayCommand PrevDayCommand     { get; }

        // ─── Constructor ──────────────────────────────────────────────────────

        public MacroLevelsViewModel(TradeRepository tradeRepo)
        {
            _tradeRepo  = tradeRepo;
            _targetDate = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");

            AddLevelCommand    = new RelayCommand(AddLevel,    CanAddLevel);
            SaveAllCommand     = new RelayCommand(SaveAll);
            DeleteLevelCommand = new RelayCommand(DeleteLevel);
            NextDayCommand     = new RelayCommand(() => TargetDate = ParseDate().AddDays(1).ToString("yyyy-MM-dd"));
            PrevDayCommand     = new RelayCommand(() => TargetDate = ParseDate().AddDays(-1).ToString("yyyy-MM-dd"));

            LoadLevels();
        }

        // ─── Loading ──────────────────────────────────────────────────────────

        private void LoadLevels()
        {
            Levels.Clear();
            var existing = _tradeRepo.GetMacroLevelsByDate(TargetDate);
            foreach (var l in existing)
            {
                Levels.Add(new MacroLevelRow
                {
                    Id    = l.Id,
                    Label = l.Label,
                    Price = l.Price
                });
            }
        }

        // ─── Commands ─────────────────────────────────────────────────────────

        private bool CanAddLevel() =>
            !string.IsNullOrWhiteSpace(NewLabel) &&
            double.TryParse(NewPrice, out _);

        private void AddLevel()
        {
            if (!double.TryParse(NewPrice, out double price)) return;

            Levels.Add(new MacroLevelRow
            {
                Id    = 0,   // not yet persisted
                Label = NewLabel.Trim(),
                Price = price
            });

            NewLabel = string.Empty;
            NewPrice = string.Empty;
        }

        private void SaveAll()
        {
            var levels = Levels.Select(r => new MacroLevel
            {
                Date  = TargetDate,
                Label = r.Label,
                Price = r.Price
            }).ToList();

            _tradeRepo.ReplaceMacroLevels(TargetDate, levels);
            LoadLevels();  // reload to get generated IDs

            MessageBox.Show(
                $"Saved {levels.Count} macro levels for {TargetDate}.",
                "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void DeleteLevel(object? param)
        {
            if (param is not MacroLevelRow row) return;

            if (row.Id > 0)
                _tradeRepo.DeleteMacroLevel(row.Id);

            Levels.Remove(row);
        }

        private DateTime ParseDate()
        {
            return DateTime.TryParse(TargetDate, out var d) ? d : DateTime.Today;
        }
    }

    public class MacroLevelRow : ViewModelBase
    {
        public int    Id    { get; set; }

        private string _label = string.Empty;
        private double _price;

        public string Label { get => _label; set => SetField(ref _label, value); }
        public double Price { get => _price; set => SetField(ref _price, value); }
    }
}
