using System.Windows;
using ES.Trading.DesktopApp.ViewModels;

namespace ES.Trading.DesktopApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Wire up ViewModels via constructor injection from App-level singletons
            DashboardView.DataContext       = new DashboardViewModel(App.TradeRepo, App.DisciplineRepo, App.ConfigRepo);
            TradeLogView.DataContext        = new TradeLogViewModel(App.TradeRepo, App.DisciplineRepo);
            MacroLevelsView.DataContext     = new MacroLevelsViewModel(App.TradeRepo);
            SettingsView.DataContext        = new SettingsViewModel(App.ConfigRepo);

            var marketVm = new MarketOverviewViewModel(App.MarketOverview);
            MarketOverviewView.DataContext  = marketVm;
            marketVm.StartInitialLoad();   // auto-fetch on app startup

            DbPathLabel.Text = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                "NinjaTrader 8", "ES.Trading", "es_trading.db");
        }
    }
}
