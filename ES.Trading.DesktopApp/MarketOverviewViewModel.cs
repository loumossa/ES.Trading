using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ES.Trading.Core.MarketOverview;

namespace ES.Trading.DesktopApp.ViewModels
{
    public class MarketOverviewViewModel : ViewModelBase
    {
        private readonly MarketOverviewService _service;
        private CancellationTokenSource?       _inflight;

        // ─── Header / status ──────────────────────────────────────────────

        private bool   _isLoading;
        private string _statusLabel = "Not loaded";
        private string _windowLabel = string.Empty;

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetField(ref _isLoading, value))
                {
                    OnPropertyChanged(nameof(IsNotLoading));
                    RefreshCommand.RaiseCanExecuteChanged();
                }
            }
        }
        public bool   IsNotLoading => !_isLoading;
        public string StatusLabel { get => _statusLabel; set => SetField(ref _statusLabel, value); }
        public string WindowLabel { get => _windowLabel; set => SetField(ref _windowLabel, value); }

        private bool _hasErrors;
        public bool HasErrors { get => _hasErrors; private set => SetField(ref _hasErrors, value); }

        // ─── Collections ──────────────────────────────────────────────────

        public ObservableCollection<QuoteRow>      OvernightQuotes { get; } = new();
        public ObservableCollection<EconomicRow>   EconomicEvents  { get; } = new();
        public ObservableCollection<EventRow>      TopEvents       { get; } = new();
        public ObservableCollection<SectorHeatRow> SectorHeat      { get; } = new();
        public ObservableCollection<string>        SourceErrors    { get; } = new();

        // ─── Commands ─────────────────────────────────────────────────────

        public RelayCommand RefreshCommand { get; }

        public MarketOverviewViewModel(MarketOverviewService service)
        {
            _service = service;
            RefreshCommand = new RelayCommand(
                async () => await RefreshAsync(),
                () => !IsLoading);
        }

        /// <summary>Fire-and-forget initial load; safe to call from App startup.</summary>
        public void StartInitialLoad()
        {
            _ = RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            if (IsLoading) return;   // already in flight

            _inflight?.Cancel();
            _inflight = new CancellationTokenSource();
            var ct = _inflight.Token;

            IsLoading   = true;
            StatusLabel = "Loading…";
            SourceErrors.Clear();

            try
            {
                var snapshot = await _service.GetSnapshotAsync(ct).ConfigureAwait(true);
                PopulateFrom(snapshot);

                StatusLabel = "Loaded " + snapshot.GeneratedUtc.ToLocalTime().ToString("h:mm tt");
                WindowLabel = FormatWindow(snapshot.WindowStartUtc, snapshot.WindowEndUtc);
            }
            catch (OperationCanceledException)
            {
                StatusLabel = "Cancelled";
            }
            catch (Exception ex)
            {
                StatusLabel = "Error: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void PopulateFrom(MarketOverviewSnapshot snapshot)
        {
            OvernightQuotes.Clear();
            foreach (var q in snapshot.OvernightQuotes)
                OvernightQuotes.Add(new QuoteRow(q));

            EconomicEvents.Clear();
            foreach (var e in snapshot.EconomicCalendar)
                EconomicEvents.Add(new EconomicRow(e));

            TopEvents.Clear();
            int rank = 1;
            foreach (var ev in snapshot.TopEvents)
                TopEvents.Add(new EventRow(ev, rank++));

            SectorHeat.Clear();
            foreach (var s in snapshot.SectorHeat)
                SectorHeat.Add(new SectorHeatRow(s));

            SourceErrors.Clear();
            foreach (var kvp in snapshot.SourceErrors)
                SourceErrors.Add(kvp.Key + ": " + kvp.Value);
            HasErrors = SourceErrors.Count > 0;
        }

        private static string FormatWindow(DateTime startUtc, DateTime endUtc)
        {
            var startLocal = startUtc.ToLocalTime();
            var endLocal   = endUtc.ToLocalTime();
            return "Window: " + startLocal.ToString("ddd h:mm tt")
                 + " → " + endLocal.ToString("ddd h:mm tt");
        }
    }

    // ─── Row view-models (display-formatted) ───────────────────────────────

    public class QuoteRow
    {
        public string Symbol      { get; }
        public string DisplayName { get; }
        public string Category    { get; }
        public string LastLabel   { get; }
        public string ChangeLabel { get; }
        public string PctLabel    { get; }
        /// <summary>True = green tint, False = red tint, null = neutral.</summary>
        public bool?  IsUp        { get; }

        public QuoteRow(OvernightQuote q)
        {
            Symbol      = q.Symbol;
            DisplayName = q.DisplayName;
            Category    = q.Category;
            LastLabel   = q.Last.HasValue      ? q.Last.Value.ToString("0.##")    : "—";
            ChangeLabel = q.ChangeAbs.HasValue ? SignedFmt(q.ChangeAbs.Value, "0.##") : "—";
            PctLabel    = q.ChangePct.HasValue ? SignedFmt(q.ChangePct.Value, "0.##") + "%" : "—";
            IsUp        = q.ChangeAbs.HasValue ? q.ChangeAbs.Value > 0 : (bool?)null;
        }
        private static string SignedFmt(double v, string fmt)
            => (v >= 0 ? "+" : "") + v.ToString(fmt);
    }

    public class EconomicRow
    {
        public string TimeLabel    { get; }
        public string Country      { get; }
        public string Title        { get; }
        public string ImpactLabel  { get; }
        public string Forecast     { get; }
        public string Previous     { get; }
        public ImpactTier Impact   { get; }

        public EconomicRow(EconomicEvent e)
        {
            TimeLabel    = e.TimeUtc.ToLocalTime().ToString("h:mm tt");
            Country      = e.Country;
            Title        = e.Title;
            Impact       = e.Impact;
            ImpactLabel  = e.Impact.ToDisplay();
            Forecast     = e.Forecast ?? "—";
            Previous     = e.Previous ?? "—";
        }
    }

    public class EventRow
    {
        public int    Rank         { get; }
        public string TimeLabel    { get; }
        public string Headline     { get; }
        public string Source       { get; }
        public string Ticker       { get; }
        public string SectorLabel  { get; }
        public string ScoreLabel   { get; }
        public string ScoreReason  { get; }
        public string BreadthLabel { get; }
        public string? Url         { get; }

        public EventRow(MarketEvent ev, int rank)
        {
            Rank         = rank;
            TimeLabel    = ev.TimestampUtc.ToLocalTime().ToString("h:mm tt");
            Headline     = ev.Headline;
            Source       = ev.Source;
            Ticker       = ev.Ticker ?? string.Empty;
            SectorLabel  = ev.Sector.ToDisplay();
            ScoreLabel   = ev.Score.ToString("0.#");
            ScoreReason  = ev.ScoreReason ?? string.Empty;
            BreadthLabel = ev.Breadth.ToString();
            Url          = ev.Url;
        }
    }

    public class SectorHeatRow
    {
        public string SectorLabel     { get; }
        public int    EventCount      { get; }
        public string ScoreLabel      { get; }
        public string TopHeadlines    { get; }

        public SectorHeatRow(SectorHeat h)
        {
            SectorLabel  = h.Sector.ToDisplay();
            EventCount   = h.EventCount;
            ScoreLabel   = h.TotalScore.ToString("0.#");
            TopHeadlines = string.Join(" • ", h.TopEvents.Take(3).Select(e => e.Headline));
        }
    }
}
