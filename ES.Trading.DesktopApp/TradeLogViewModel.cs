using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using ES.Trading.Core.Data;
using ES.Trading.Core.Models;

namespace ES.Trading.DesktopApp.ViewModels
{
    public class TradeLogViewModel : ViewModelBase
    {
        private readonly TradeRepository     _tradeRepo;
        private readonly DisciplineRepository _disciplineRepo;

        // ─── Trade list ────────────────────────────────────────────────────────

        public ObservableCollection<TradeRow> Trades { get; } = new();

        private TradeRow? _selectedTrade;
        public TradeRow? SelectedTrade
        {
            get => _selectedTrade;
            set
            {
                if (SetField(ref _selectedTrade, value))
                    LoadTradeDetail(value);
            }
        }

        // ─── Detail / annotation panel ─────────────────────────────────────────

        private ObservableCollection<TradeLegRow> _legs = new();
        private ObservableCollection<DisciplineCheckRow> _checks = new();
        private string _notes = string.Empty;
        private int? _moodRating;
        private string? _screenshotPath;
        private ObservableCollection<TagItem> _tags = new();

        public ObservableCollection<TradeLegRow>        Legs        { get => _legs;          set => SetField(ref _legs, value); }
        public ObservableCollection<DisciplineCheckRow> Checks      { get => _checks;        set => SetField(ref _checks, value); }
        public string  Notes                                         { get => _notes;         set => SetField(ref _notes, value); }
        public int?    MoodRating                                    { get => _moodRating;    set => SetField(ref _moodRating, value); }
        public string? ScreenshotPath                                { get => _screenshotPath; set => SetField(ref _screenshotPath, value); }
        public ObservableCollection<TagItem> Tags                    { get => _tags;          set => SetField(ref _tags, value); }

        public bool HasScreenshot => !string.IsNullOrEmpty(ScreenshotPath);

        // ─── Filters ───────────────────────────────────────────────────────────

        private string _filterDate = DateTime.Today.ToString("yyyy-MM-dd");
        public string FilterDate
        {
            get => _filterDate;
            set
            {
                if (SetField(ref _filterDate, value))
                    LoadTrades();
            }
        }

        // ─── Commands ─────────────────────────────────────────────────────────

        public RelayCommand SaveAnnotationCommand   { get; }
        public RelayCommand AttachScreenshotCommand { get; }
        public RelayCommand ExportCsvCommand        { get; }
        public RelayCommand GoTodayCommand          { get; }

        // ─── Constructor ──────────────────────────────────────────────────────

        public TradeLogViewModel(TradeRepository tradeRepo, DisciplineRepository disciplineRepo)
        {
            _tradeRepo      = tradeRepo;
            _disciplineRepo = disciplineRepo;

            SaveAnnotationCommand   = new RelayCommand(SaveAnnotation,   () => SelectedTrade != null);
            AttachScreenshotCommand = new RelayCommand(AttachScreenshot, () => SelectedTrade != null);
            ExportCsvCommand        = new RelayCommand(ExportCsv);
            GoTodayCommand          = new RelayCommand(() => FilterDate = DateTime.Today.ToString("yyyy-MM-dd"));

            // Initialize all tag options
            foreach (var tag in TradeTags.All)
                Tags.Add(new TagItem { Key = tag, Label = tag, IsSelected = false });

            LoadTrades();
        }

        // ─── Data loading ──────────────────────────────────────────────────────

        public void LoadTrades()
        {
            Trades.Clear();
            SelectedTrade = null;
            Legs.Clear();
            Checks.Clear();

            var day = _tradeRepo.GetByDate(FilterDate);
            if (day == null) return;

            var trades = _tradeRepo.GetTradesByDay(day.Id);
            foreach (var t in trades)
            {
                Trades.Add(new TradeRow
                {
                    Id             = t.Id,
                    Time           = t.EntryTime.ToString("HH:mm:ss"),
                    Instrument     = t.Instrument,
                    ContractSymbol = t.ContractSymbol,
                    Direction      = t.Direction,
                    EntryPrice     = t.EntryPrice,
                    TotalPL        = t.TotalPL,
                    SetupType      = t.SetupType ?? "—",
                    AttemptNumber  = t.AttemptNumber,
                    HasNotes       = !string.IsNullOrWhiteSpace(t.Notes),
                    HasScreenshot  = !string.IsNullOrWhiteSpace(t.ScreenshotPath)
                });
            }
        }

        private void LoadTradeDetail(TradeRow? row)
        {
            Legs.Clear();
            Checks.Clear();

            if (row == null) return;

            var trade = _tradeRepo.GetTradeWithDetail(row.Id);
            if (trade == null) return;

            // Populate annotation fields
            Notes         = trade.Notes ?? string.Empty;
            MoodRating    = null;  // stored at day level — future: load from day
            ScreenshotPath = trade.ScreenshotPath;
            OnPropertyChanged(nameof(HasScreenshot));

            // Legs
            foreach (var leg in trade.Legs)
            {
                Legs.Add(new TradeLegRow
                {
                    Time      = leg.ExecutionTime.ToString("HH:mm:ss"),
                    Type      = leg.LegType,
                    Price     = leg.Price,
                    Contracts = leg.Contracts,
                    PL        = leg.PLRealized
                });
            }

            // Discipline checks
            foreach (var c in trade.DisciplineChecks)
            {
                Checks.Add(new DisciplineCheckRow
                {
                    Rule   = DisciplineRules.GetLabel(c.RuleKey),
                    Passed = c.Passed,
                    Notes  = c.Notes ?? string.Empty
                });
            }

            // Tags
            var activeTags = trade.GetTags();
            foreach (var tag in Tags)
                tag.IsSelected = activeTags.Contains(tag.Key);
        }

        // ─── Commands ─────────────────────────────────────────────────────────

        private void SaveAnnotation()
        {
            if (SelectedTrade == null) return;

            var trade = _tradeRepo.GetTradeWithDetail(SelectedTrade.Id);
            if (trade == null) return;

            trade.Notes          = Notes;
            trade.ScreenshotPath = ScreenshotPath;
            trade.SetTags(Tags.Where(t => t.IsSelected).Select(t => t.Key));

            _tradeRepo.UpdateTrade(trade);

            // Update the row's indicator flags
            SelectedTrade.HasNotes      = !string.IsNullOrWhiteSpace(Notes);
            SelectedTrade.HasScreenshot = !string.IsNullOrWhiteSpace(ScreenshotPath);
        }

        private void AttachScreenshot()
        {
            if (SelectedTrade == null) return;

            var dialog = new OpenFileDialog
            {
                Title  = "Select screenshot",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            // Copy to screenshots folder next to the DB
            string screenshotsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NinjaTrader 8", "ES.Trading", "screenshots");

            Directory.CreateDirectory(screenshotsDir);

            string ext      = Path.GetExtension(dialog.FileName);
            string fileName = $"trade_{SelectedTrade.Id}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
            string destPath = Path.Combine(screenshotsDir, fileName);

            File.Copy(dialog.FileName, destPath, overwrite: true);

            // Store relative path
            ScreenshotPath = Path.Combine("screenshots", fileName);
            OnPropertyChanged(nameof(HasScreenshot));
        }

        private void ExportCsv()
        {
            var dialog = new SaveFileDialog
            {
                Title            = "Export trades to CSV",
                Filter           = "CSV files|*.csv",
                FileName         = $"es_trades_{DateTime.Today:yyyyMMdd}.csv"
            };

            if (dialog.ShowDialog() != true) return;

            var allTrades = _tradeRepo.GetAllTrades().ToList();

            using var writer = new StreamWriter(dialog.FileName);
            writer.WriteLine("Id,Date,EntryTime,Instrument,ContractSymbol,Direction,EntryPrice,StopPrice," +
                             "AttemptNumber,SetupType,TotalPL,Tags,Notes");

            foreach (var t in allTrades)
            {
                writer.WriteLine(
                    $"{t.Id}," +
                    $"{t.EntryTime:yyyy-MM-dd}," +
                    $"{t.EntryTime:HH:mm:ss}," +
                    $"{t.Instrument}," +
                    $"{t.ContractSymbol ?? ""}," +
                    $"{t.Direction}," +
                    $"{t.EntryPrice:F2}," +
                    $"{t.StopPrice?.ToString("F2") ?? ""}," +
                    $"{t.AttemptNumber}," +
                    $"{t.SetupType ?? ""}," +
                    $"{t.TotalPL?.ToString("F2") ?? ""}," +
                    $"\"{string.Join("|", t.GetTags())}\"," +
                    $"\"{(t.Notes ?? "").Replace("\"", "\"\"")}\"");
            }

            MessageBox.Show($"Exported {allTrades.Count} trades to:\n{dialog.FileName}",
                "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ─── Display row types ────────────────────────────────────────────────────

    public class TradeRow : ViewModelBase
    {
        public int     Id             { get; set; }
        public string  Time           { get; set; } = string.Empty;
        public string  Instrument     { get; set; } = string.Empty;
        public string? ContractSymbol { get; set; }
        public string  Direction      { get; set; } = string.Empty;
        public double  EntryPrice     { get; set; }
        public double? TotalPL        { get; set; }
        public string  SetupType      { get; set; } = string.Empty;
        public int     AttemptNumber  { get; set; }

        private bool _hasNotes;
        private bool _hasScreenshot;
        public bool HasNotes      { get => _hasNotes;      set => SetField(ref _hasNotes, value); }
        public bool HasScreenshot { get => _hasScreenshot; set => SetField(ref _hasScreenshot, value); }

        public string PLDisplay => TotalPL.HasValue ? $"${TotalPL:F2}" : "Open";

        /// <summary>
        /// Full contract symbol when present, otherwise the normalized root.
        /// Trades recorded before the ContractSymbol column was added show as "ES" / "MES".
        /// </summary>
        public string ContractDisplay =>
            !string.IsNullOrWhiteSpace(ContractSymbol) ? ContractSymbol! : Instrument;
    }

    public class TradeLegRow
    {
        public string  Time      { get; set; } = string.Empty;
        public string  Type      { get; set; } = string.Empty;
        public double  Price     { get; set; }
        public int     Contracts { get; set; }
        public double? PL        { get; set; }
        public string  PLDisplay => PL.HasValue ? $"${PL:F2}" : "—";
    }

    public class DisciplineCheckRow
    {
        public string Rule   { get; set; } = string.Empty;
        public bool   Passed { get; set; }
        public string Notes  { get; set; } = string.Empty;
        public string Icon   => Passed ? "✓" : "✗";
    }

    public class TagItem : ViewModelBase
    {
        public string Key   { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
    }
}
