using System;
using System.Collections.ObjectModel;
using System.Linq;
using ES.Trading.Core.Data;
using ES.Trading.Core.Models;
using ES.Trading.Core.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace ES.Trading.DesktopApp.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private readonly TradeRepository          _tradeRepo;
        private readonly DisciplineRepository     _disciplineRepo;
        private readonly ConfigurationRepository  _configRepo;
        private readonly DisciplineScoreCalculator _scorer;

        // ─── Discipline score display ──────────────────────────────────────────

        private double _todayScore;
        private double _rollingScore;
        private int    _rollingDays;
        private string _todayScoreLabel  = "—";
        private string _rollingScoreLabel = "—";

        public double TodayScore       { get => _todayScore;       set => SetField(ref _todayScore, value); }
        public double RollingScore     { get => _rollingScore;     set => SetField(ref _rollingScore, value); }
        public string TodayScoreLabel  { get => _todayScoreLabel;  set => SetField(ref _todayScoreLabel, value); }
        public string RollingScoreLabel { get => _rollingScoreLabel; set => SetField(ref _rollingScoreLabel, value); }

        // ─── Session summary ───────────────────────────────────────────────────

        private double _todayPL;
        private int    _todayAttempts;
        private string _todayBias = "—";

        public double TodayPL       { get => _todayPL;       set => SetField(ref _todayPL, value); }
        public int    TodayAttempts { get => _todayAttempts; set => SetField(ref _todayAttempts, value); }
        public string TodayBias     { get => _todayBias;     set => SetField(ref _todayBias, value); }

        // ─── Rule breakdown ────────────────────────────────────────────────────

        public ObservableCollection<RuleBreakdownRow> RuleBreakdown { get; } = new();

        // ─── Charts ───────────────────────────────────────────────────────────

        // Discipline trend line chart
        public ISeries[]  DisciplineSeries { get; private set; } = Array.Empty<ISeries>();
        public Axis[]     DisciplineXAxes  { get; private set; } = Array.Empty<Axis>();
        public Axis[]     DisciplineYAxes  { get; private set; } = Array.Empty<Axis>();

        // P&L by time-of-day heatmap (bar chart bucketed by 30-min intervals)
        public ISeries[]  TimeOfDaySeries  { get; private set; } = Array.Empty<ISeries>();
        public Axis[]     TimeOfDayXAxes   { get; private set; } = Array.Empty<Axis>();

        // ─── Commands ─────────────────────────────────────────────────────────

        public RelayCommand RefreshCommand { get; }

        // ─── Constructor ──────────────────────────────────────────────────────

        public DashboardViewModel(
            TradeRepository tradeRepo,
            DisciplineRepository disciplineRepo,
            ConfigurationRepository configRepo)
        {
            _tradeRepo      = tradeRepo;
            _disciplineRepo = disciplineRepo;
            _configRepo     = configRepo;
            _scorer         = new DisciplineScoreCalculator(disciplineRepo);

            RefreshCommand = new RelayCommand(Refresh);
            Refresh();
        }

        // ─── Data loading ──────────────────────────────────────────────────────

        public void Refresh()
        {
            _rollingDays = _configRepo.GetOrDefault(ConfigKeys.DisciplineRollingDays, 20);

            LoadTodaySummary();
            LoadDisciplineScores();
            LoadRuleBreakdown();
            BuildDisciplineTrendChart();
            BuildTimeOfDayChart();
        }

        private void LoadTodaySummary()
        {
            var today = _tradeRepo.GetByDate(DateTime.Today.ToString("yyyy-MM-dd"));
            if (today == null) return;

            TodayPL       = today.DailyPL;
            TodayAttempts = today.AttemptCount;
            TodayBias     = today.ORHigh.HasValue && today.ORLow.HasValue
                ? $"OR {today.ORLow:F2} – {today.ORHigh:F2}"
                : "—";
        }

        private void LoadDisciplineScores()
        {
            var today = _tradeRepo.GetByDate(DateTime.Today.ToString("yyyy-MM-dd"));
            if (today != null)
            {
                var dayScore = _scorer.CalculateForDay(today.Id);
                TodayScore      = dayScore?.Percentage ?? 0;
                TodayScoreLabel = dayScore != null
                    ? $"{dayScore.Percentage:F0}%  ({dayScore.PassedCount}/{dayScore.ApplicableCount})"
                    : "No trades today";
            }

            var rolling = _scorer.CalculateRolling(_rollingDays);
            RollingScore      = rolling?.Percentage ?? 0;
            RollingScoreLabel = rolling != null
                ? $"{rolling.Percentage:F0}%  ({rolling.PassedCount}/{rolling.ApplicableCount})"
                : "No data";
        }

        private void LoadRuleBreakdown()
        {
            RuleBreakdown.Clear();

            var toDate   = DateTime.Today.ToString("yyyy-MM-dd");
            var fromDate = DateTime.Today.AddDays(-_rollingDays).ToString("yyyy-MM-dd");
            var checks   = _disciplineRepo.GetByDateRange(fromDate, toDate).ToList();

            var breakdown = _scorer.GetRuleBreakdown(checks);
            foreach (var r in breakdown)
            {
                RuleBreakdown.Add(new RuleBreakdownRow
                {
                    Label      = r.Label,
                    PassRate   = $"{r.PassRate:F0}%",
                    Passed     = r.PassedChecks,
                    Total      = r.TotalChecks,
                    IsWorst    = r.PassRate < 70
                });
            }
        }

        // ─── Chart builders ────────────────────────────────────────────────────

        private void BuildDisciplineTrendChart()
        {
            var days   = _tradeRepo.GetRecentDays(_rollingDays).ToList();
            var labels = new List<string>();
            var values = new List<double?>();

            foreach (var day in days.OrderBy(d => d.Date))
            {
                labels.Add(day.Date[5..]);  // MM-dd
                var checks = _disciplineRepo.GetByDay(day.Id).ToList();
                if (checks.Count == 0)
                    values.Add(null);
                else
                {
                    int passed = checks.Count(c => c.Passed);
                    values.Add(Math.Round((double)passed / checks.Count * 100, 1));
                }
            }

            DisciplineSeries = new ISeries[]
            {
                new LineSeries<double?>
                {
                    Values        = values,
                    Name          = "Discipline %",
                    Stroke        = new SolidColorPaint(SKColor.Parse("#5BC0DE"), 2),
                    Fill          = new SolidColorPaint(SKColor.Parse("#1A5BC0DE")),
                    GeometrySize  = 6,
                    GeometryStroke = new SolidColorPaint(SKColor.Parse("#5BC0DE"), 2),
                    GeometryFill  = new SolidColorPaint(SKColor.Parse("#1A1A1A")),
                    LineSmoothness = 0.3
                }
            };

            DisciplineXAxes = new Axis[]
            {
                new Axis
                {
                    Labels          = labels,
                    LabelsPaint     = new SolidColorPaint(SKColor.Parse("#999999")),
                    TicksPaint      = new SolidColorPaint(SKColor.Parse("#3A3A3A")),
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2A2A2A")),
                    TextSize        = 10
                }
            };

            DisciplineYAxes = new Axis[]
            {
                new Axis
                {
                    MinLimit        = 0,
                    MaxLimit        = 100,
                    LabelsPaint     = new SolidColorPaint(SKColor.Parse("#999999")),
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2A2A2A")),
                    Labeler         = v => $"{v:F0}%",
                    TextSize        = 10
                }
            };

            OnPropertyChanged(nameof(DisciplineSeries));
            OnPropertyChanged(nameof(DisciplineXAxes));
            OnPropertyChanged(nameof(DisciplineYAxes));
        }

        private void BuildTimeOfDayChart()
        {
            // Bucket all trades by 30-minute RTH windows: 9:30, 10:00, ..., 15:30
            var allTrades = _tradeRepo.GetAllTrades()
                .Where(t => !t.IsMESFractionalExit && t.TotalPL.HasValue)
                .ToList();

            var buckets = new[]
            {
                "9:30","10:00","10:30","11:00","11:30",
                "12:00","12:30","13:00","13:30","14:00",
                "14:30","15:00","15:30"
            };

            var plByBucket = buckets.Select(_ => 0.0).ToArray();

            foreach (var trade in allTrades)
            {
                int bucket = (trade.EntryTime.Hour * 60 + trade.EntryTime.Minute - 570) / 30;
                if (bucket >= 0 && bucket < plByBucket.Length)
                    plByBucket[bucket] += trade.TotalPL!.Value;
            }

            TimeOfDaySeries = new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Values = plByBucket,
                    Name   = "P&L by time of day",
                    Fill   = new SolidColorPaint(SKColor.Parse("#5CB85C")),
                    // Color overridden per-point below based on positive/negative
                }
            };

            TimeOfDayXAxes = new Axis[]
            {
                new Axis
                {
                    Labels          = buckets,
                    LabelsPaint     = new SolidColorPaint(SKColor.Parse("#999999")),
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#2A2A2A")),
                    TextSize        = 10
                }
            };

            OnPropertyChanged(nameof(TimeOfDaySeries));
            OnPropertyChanged(nameof(TimeOfDayXAxes));
        }
    }

    // ─── Supporting display types ──────────────────────────────────────────────

    public class RuleBreakdownRow
    {
        public string Label    { get; set; } = string.Empty;
        public string PassRate { get; set; } = string.Empty;
        public int    Passed   { get; set; }
        public int    Total    { get; set; }
        public string PassedAndTotal => $"{Passed}/{Total}";

        /// <summary>True when pass rate is below 70% — highlighted in the UI.</summary>
        public bool   IsWorst  { get; set; }
    }
}
