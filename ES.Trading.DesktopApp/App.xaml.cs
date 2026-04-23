using System.Windows;
using ES.Trading.Core.Data;
using ES.Trading.Core.MarketOverview;

namespace ES.Trading.DesktopApp
{
    public partial class App : Application
    {
        // Shared instances — passed via constructor injection to each ViewModel.
        // Kept here so there's one DatabaseContext (one connection string) for the app.
        public static DatabaseContext         Db             { get; private set; } = null!;
        public static TradeRepository         TradeRepo      { get; private set; } = null!;
        public static DisciplineRepository    DisciplineRepo { get; private set; } = null!;
        public static ConfigurationRepository ConfigRepo     { get; private set; } = null!;

        public static MarketOverviewService   MarketOverview { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Resolve database path — same location the NT8 Add-On uses.
            // Both components point at the same file.
            string dbPath = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "NinjaTrader 8", "ES.Trading", "es_trading.db");

            System.IO.Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(dbPath)!);

            Db = new DatabaseContext(dbPath);
            Db.EnsureCreated();

            TradeRepo      = new TradeRepository(Db);
            DisciplineRepo = new DisciplineRepository(Db);
            ConfigRepo     = new ConfigurationRepository(Db);

            // Market overview: one service instance per app. Uses free sources today;
            // swap the IMarketDataSource for a Schwab adapter when that's wired up.
            var mkOptions = new MarketOverviewOptions
            {
                SecUserAgent = "ES.Trading/1.0 (contact: lou.mossa@gmail.com)"
            };
            MarketOverview = MarketOverviewService.CreateDefault(mkOptions);
        }
    }
}
