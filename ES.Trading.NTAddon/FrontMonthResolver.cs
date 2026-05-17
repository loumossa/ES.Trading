using System;
using NinjaTrader.Cbi;

namespace ES.Trading.NTAddon.Services
{
    /// <summary>
    /// Resolves the front-month ES contract at runtime so the AddOn does not
    /// have to be edited every quarter when the contract rolls.
    ///
    /// ES futures expire on the third Friday of March (H), June (M),
    /// September (U), and December (Z). Most traders roll forward about a
    /// week before expiry, so we treat the contract as "front month" until
    /// it's within <see cref="RollDaysBeforeExpiry"/> days of expiry and
    /// then advance to the next quarter.
    /// </summary>
    public static class FrontMonthResolver
    {
        private const int RollDaysBeforeExpiry = 8;
        private static readonly int[] QuarterlyMonths = { 3, 6, 9, 12 };

        /// <summary>
        /// Returns the currently-tradeable front-month ES <see cref="Instrument"/>,
        /// or <c>null</c> if no NT8 instrument record was found for it (e.g. the
        /// user has not yet downloaded that contract definition).
        /// </summary>
        public static Instrument? ResolveFrontMonthEs(DateTime? asOf = null)
        {
            return ResolveFrontMonth("ES", asOf);
        }

        /// <summary>
        /// Returns the resolved NT8 symbol string (e.g. "ES 06-26"), independent
        /// of whether NT8 has the instrument record. Useful for logging.
        /// </summary>
        public static string ResolveFrontMonthSymbol(string root = "ES", DateTime? asOf = null)
        {
            var today = (asOf ?? DateTime.Today).Date;

            foreach (var year in new[] { today.Year, today.Year + 1 })
            {
                foreach (var month in QuarterlyMonths)
                {
                    var expiry = ThirdFriday(year, month);
                    if (expiry.AddDays(-RollDaysBeforeExpiry) <= today) continue;

                    return $"{root} {month:D2}-{year % 100:D2}";
                }
            }

            throw new InvalidOperationException(
                "Could not compute a front-month symbol — quarterly month table exhausted.");
        }

        private static Instrument? ResolveFrontMonth(string root, DateTime? asOf)
        {
            var today = (asOf ?? DateTime.Today).Date;

            foreach (var year in new[] { today.Year, today.Year + 1 })
            {
                foreach (var month in QuarterlyMonths)
                {
                    var expiry = ThirdFriday(year, month);
                    if (expiry.AddDays(-RollDaysBeforeExpiry) <= today) continue;

                    string symbol = $"{root} {month:D2}-{year % 100:D2}";
                    var instr = Instrument.GetInstrument(symbol);
                    if (instr != null) return instr;
                }
            }

            return null;
        }

        private static DateTime ThirdFriday(int year, int month)
        {
            var first = new DateTime(year, month, 1);
            int offset = ((int)DayOfWeek.Friday - (int)first.DayOfWeek + 7) % 7;
            return first.AddDays(offset + 14);
        }
    }
}
