using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace ES.Trading.DesktopApp.Views
{
    public partial class MarketOverviewView : UserControl
    {
        public MarketOverviewView()
        {
            InitializeComponent();
        }

        // Open the event's Url in the default browser when its hyperlink is clicked.
        // Swallow failures (malformed URL, no default browser) — the panel shouldn't crash.
        private void OnHyperlinkNavigate(object sender, RequestNavigateEventArgs e)
        {
            if (e.Uri == null) { e.Handled = true; return; }
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch { /* ignore */ }
            e.Handled = true;
        }
    }
}
