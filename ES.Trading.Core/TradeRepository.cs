using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using ES.Trading.Core.Models;

namespace ES.Trading.Core.Data
{
    public class TradeRepository
    {
        private readonly DatabaseContext _db;

        public TradeRepository(DatabaseContext db)
        {
            _db = db;
        }

        // ─── TradingDays ──────────────────────────────────────────────────────────

        /// <summary>Returns the TradingDay for today's date, or null if not yet created.</summary>
        public TradingDay? GetTodayOrNull()
        {
            return GetByDate(DateTime.Today.ToString("yyyy-MM-dd"));
        }

        /// <summary>
        /// Returns the TradingDay for a specific date, or null.
        /// Date format: yyyy-MM-dd.
        /// </summary>
        public TradingDay? GetByDate(string date)
        {
            using var conn = _db.OpenConnection();
            return conn.QuerySingleOrDefault<TradingDay>(
                "SELECT * FROM TradingDays WHERE Date = @Date;",
                new { Date = date });
        }

        /// <summary>
        /// Gets today's TradingDay, creating it if it doesn't exist.
        /// The NT8 Add-On calls this at startup to ensure the day record exists.
        /// </summary>
        public TradingDay GetOrCreateToday()
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var existing = GetByDate(today);
            if (existing != null) return existing;

            var day = new TradingDay { Date = today };
            return InsertTradingDay(day);
        }

        public TradingDay InsertTradingDay(TradingDay day)
        {
            using var conn = _db.OpenConnection();
            day.Id = conn.QuerySingle<int>(
                @"INSERT INTO TradingDays (Date, ORHigh, ORLow, IsNoTradeDay, MoodRating,
                                          DailyPL, AttemptCount, DailyLossLimitHit, Notes)
                  VALUES (@Date, @ORHigh, @ORLow, @IsNoTradeDay, @MoodRating,
                          @DailyPL, @AttemptCount, @DailyLossLimitHit, @Notes);
                  SELECT last_insert_rowid();",
                day);
            return day;
        }

        public void UpdateTradingDay(TradingDay day)
        {
            using var conn = _db.OpenConnection();
            conn.Execute(
                @"UPDATE TradingDays SET
                    ORHigh            = @ORHigh,
                    ORLow             = @ORLow,
                    IsNoTradeDay      = @IsNoTradeDay,
                    MoodRating        = @MoodRating,
                    DailyPL           = @DailyPL,
                    AttemptCount      = @AttemptCount,
                    DailyLossLimitHit = @DailyLossLimitHit,
                    Notes             = @Notes
                  WHERE Id = @Id;",
                day);
        }

        /// <summary>
        /// Atomically increments AttemptCount for a day and returns the new count.
        /// Safer than read-modify-write from the application layer.
        /// </summary>
        public int IncrementAttemptCount(int dayId)
        {
            using var conn = _db.OpenConnection();
            return conn.QuerySingle<int>(
                @"UPDATE TradingDays SET AttemptCount = AttemptCount + 1 WHERE Id = @Id;
                  SELECT AttemptCount FROM TradingDays WHERE Id = @Id;",
                new { Id = dayId });
        }

        /// <summary>Updates DailyPL by adding the provided delta (can be negative).</summary>
        public void AddToDailyPL(int dayId, double plDelta)
        {
            using var conn = _db.OpenConnection();
            conn.Execute(
                "UPDATE TradingDays SET DailyPL = DailyPL + @Delta WHERE Id = @Id;",
                new { Id = dayId, Delta = plDelta });
        }

        /// <summary>Returns the most recent N trading days, ordered newest first.</summary>
        public IEnumerable<TradingDay> GetRecentDays(int count = 30)
        {
            using var conn = _db.OpenConnection();
            return conn.Query<TradingDay>(
                "SELECT * FROM TradingDays ORDER BY Date DESC LIMIT @Count;",
                new { Count = count });
        }

        // ─── Trades ───────────────────────────────────────────────────────────────

        /// <summary>Inserts a new trade and returns it with its generated Id.</summary>
        public Trade InsertTrade(Trade trade)
        {
            using var conn = _db.OpenConnection();
            trade.Id = conn.QuerySingle<int>(
                @"INSERT INTO Trades (DayId, EntryTime, Instrument, Direction, EntryPrice,
                                     StopPrice, AttemptNumber, SetupType, IsMESFractionalExit,
                                     TotalPL, Notes, ScreenshotPath, TagsJson)
                  VALUES (@DayId, @EntryTime, @Instrument, @Direction, @EntryPrice,
                          @StopPrice, @AttemptNumber, @SetupType, @IsMESFractionalExit,
                          @TotalPL, @Notes, @ScreenshotPath, @TagsJson);
                  SELECT last_insert_rowid();",
                trade);
            return trade;
        }

        public void UpdateTrade(Trade trade)
        {
            using var conn = _db.OpenConnection();
            conn.Execute(
                @"UPDATE Trades SET
                    Instrument          = @Instrument,
                    Direction           = @Direction,
                    EntryPrice          = @EntryPrice,
                    StopPrice           = @StopPrice,
                    AttemptNumber       = @AttemptNumber,
                    SetupType           = @SetupType,
                    IsMESFractionalExit = @IsMESFractionalExit,
                    TotalPL             = @TotalPL,
                    Notes               = @Notes,
                    ScreenshotPath      = @ScreenshotPath,
                    TagsJson            = @TagsJson
                  WHERE Id = @Id;",
                trade);
        }

        /// <summary>Returns all trades for a given DayId, without legs or discipline checks.</summary>
        public IEnumerable<Trade> GetTradesByDay(int dayId)
        {
            using var conn = _db.OpenConnection();
            return conn.Query<Trade>(
                "SELECT * FROM Trades WHERE DayId = @DayId ORDER BY EntryTime;",
                new { DayId = dayId });
        }

        /// <summary>
        /// Returns a trade with its legs and discipline checks populated.
        /// Use this when showing trade detail in the Desktop App.
        /// </summary>
        public Trade? GetTradeWithDetail(int tradeId)
        {
            using var conn = _db.OpenConnection();

            var trade = conn.QuerySingleOrDefault<Trade>(
                "SELECT * FROM Trades WHERE Id = @Id;",
                new { Id = tradeId });

            if (trade == null) return null;

            trade.Legs = conn.Query<TradeLeg>(
                "SELECT * FROM TradeLegs WHERE TradeId = @TradeId ORDER BY ExecutionTime;",
                new { TradeId = tradeId }).ToList();

            trade.DisciplineChecks = conn.Query<DisciplineCheck>(
                "SELECT * FROM DisciplineChecks WHERE TradeId = @TradeId;",
                new { TradeId = tradeId }).ToList();

            return trade;
        }

        /// <summary>Returns all trades across all days, for bulk analytics queries.</summary>
        public IEnumerable<Trade> GetAllTrades()
        {
            using var conn = _db.OpenConnection();
            return conn.Query<Trade>(
                "SELECT * FROM Trades ORDER BY EntryTime;");
        }

        // ─── TradeLegs ────────────────────────────────────────────────────────────

        public TradeLeg InsertTradeLeg(TradeLeg leg)
        {
            using var conn = _db.OpenConnection();
            leg.Id = conn.QuerySingle<int>(
                @"INSERT INTO TradeLegs (TradeId, LegType, ExecutionTime, Price, Contracts, PLRealized)
                  VALUES (@TradeId, @LegType, @ExecutionTime, @Price, @Contracts, @PLRealized);
                  SELECT last_insert_rowid();",
                leg);
            return leg;
        }

        public IEnumerable<TradeLeg> GetLegsByTrade(int tradeId)
        {
            using var conn = _db.OpenConnection();
            return conn.Query<TradeLeg>(
                "SELECT * FROM TradeLegs WHERE TradeId = @TradeId ORDER BY ExecutionTime;",
                new { TradeId = tradeId });
        }

        // ─── MacroLevels ──────────────────────────────────────────────────────────

        /// <summary>Returns all macro levels for a specific session date.</summary>
        public IEnumerable<MacroLevel> GetMacroLevelsByDate(string date)
        {
            using var conn = _db.OpenConnection();
            return conn.Query<MacroLevel>(
                "SELECT * FROM MacroLevels WHERE Date = @Date ORDER BY Price DESC;",
                new { Date = date });
        }

        /// <summary>Returns macro levels for tomorrow — used by NT8 Add-On at startup.</summary>
        public IEnumerable<MacroLevel> GetTomorrowsMacroLevels()
        {
            return GetMacroLevelsByDate(DateTime.Today.ToString("yyyy-MM-dd"));
        }

        public MacroLevel InsertMacroLevel(MacroLevel level)
        {
            using var conn = _db.OpenConnection();
            level.Id = conn.QuerySingle<int>(
                @"INSERT INTO MacroLevels (Date, Label, Price)
                  VALUES (@Date, @Label, @Price);
                  SELECT last_insert_rowid();",
                level);
            return level;
        }

        public void UpdateMacroLevel(MacroLevel level)
        {
            using var conn = _db.OpenConnection();
            conn.Execute(
                "UPDATE MacroLevels SET Label = @Label, Price = @Price WHERE Id = @Id;",
                level);
        }

        public void DeleteMacroLevel(int id)
        {
            using var conn = _db.OpenConnection();
            conn.Execute("DELETE FROM MacroLevels WHERE Id = @Id;", new { Id = id });
        }

        /// <summary>
        /// Replaces all macro levels for a date in a single transaction.
        /// Used by the Desktop App when saving the night-before level set.
        /// </summary>
        public void ReplaceMacroLevels(string date, IEnumerable<MacroLevel> levels)
        {
            using var conn = _db.OpenConnection();
            using var tx = conn.BeginTransaction();

            try
            {
                conn.Execute(
                    "DELETE FROM MacroLevels WHERE Date = @Date;",
                    new { Date = date },
                    tx);

                foreach (var level in levels)
                {
                    level.Date = date;
                    conn.Execute(
                        "INSERT INTO MacroLevels (Date, Label, Price) VALUES (@Date, @Label, @Price);",
                        level,
                        tx);
                }

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
    }
}
