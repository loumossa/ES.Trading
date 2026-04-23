#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

// This file is a NinjaScript source stub. It MUST live in the user's
// NinjaTrader 8 install:
//
//   Documents\NinjaTrader 8\bin\Custom\AddOns\ESTradingAddon.cs
//
// Reason: NT8 only discovers AddOnBase subclasses inside its own compiled
// NinjaTrader.Custom.dll (the assembly NinjaScript Editor produces).
// External DLLs dropped in bin\Custom\AddOns\ are invisible to NT8's
// add-on discovery. This stub gets compiled by NT8 into NinjaTrader.Custom.dll,
// and all the real logic lives in ES.Trading.NTAddon.dll (built from the
// ES.Trading.NTAddon project and dropped in bin\Custom\).
//
// One-time setup in NT8:
//   1. Build the ES.Trading solution (the NTAddon project outputs to bin\Custom\).
//   2. Copy this file to Documents\NinjaTrader 8\bin\Custom\AddOns\.
//   3. Open NinjaScript Editor, right-click the project → References →
//      add ES.Trading.NTAddon.dll (and its deps if NT8 complains).
//   4. Press F5 to compile. Restart NT8.
//   5. Tools menu → "ES Trading Panel" should now appear.

namespace NinjaTrader.NinjaScript.AddOns
{
    public class ESTradingAddon : AddOnBase
    {
        private ES.Trading.NTAddon.ESTradingAddonHost _host;
        private BarsRequest _barsRequest;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "ES Futures intraday trading panel — OR levels, alerts, and session tracking.";
                Name        = "ES Trading Panel";

                _barsRequest = new BarsRequest(
                    Instrument.GetInstrument("ES 06-25"),
                    DateTime.Now.AddDays(-1),
                    DateTime.Now);
            }
            else if (State == State.Configure)
            {
                if (_barsRequest != null)
                {
                    _barsRequest.BarsPeriod   = new BarsPeriod { BarsPeriodType = BarsPeriodType.Tick, Value = 1 };
                    _barsRequest.TradingHours = TradingHours.Get("CME US Index Futures RTH");
                    _barsRequest.Update      += OnBarsUpdate;
                }
            }
            else if (State == State.Active)
            {
                try
                {
                    _host = new ES.Trading.NTAddon.ESTradingAddonHost();
                    _host.Initialize(msg => Print(msg));
                }
                catch (Exception ex)
                {
                    Print("[ES.Trading] Add-On stub init failed: " + ex);
                }
            }
            else if (State == State.Terminated)
            {
                try
                {
                    if (_barsRequest != null)
                    {
                        _barsRequest.Update -= OnBarsUpdate;
                        _barsRequest.Dispose();
                        _barsRequest = null;
                    }
                    _host?.Shutdown();
                }
                catch { /* Swallow on shutdown */ }
            }
        }

        private void OnBarsUpdate(object sender, BarsUpdateEventArgs e)
        {
            _host?.OnBarsUpdate(sender, e);
        }
    }
}
