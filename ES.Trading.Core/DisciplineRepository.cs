using System.Collections.Generic;
using Dapper;
using ES.Trading.Core.Models;

namespace ES.Trading.Core.Data
{
    public class DisciplineRepository
    {
        private readonly DatabaseContext _db;

        public DisciplineRepository(DatabaseContext db)
        {
            _db = db;
        }

        public DisciplineCheck Insert(DisciplineCheck check)
        {
            using var conn = _db.OpenConnection();
            check.Id = conn.QuerySingle<int>(
                @"INSERT INTO DisciplineChecks (TradeId, DayId, RuleKey, Passed, Notes)
                  VALUES (@TradeId, @DayId, @RuleKey, @Passed, @Notes);
                  SELECT last_insert_rowid();",
                check);
            return check;
        }

        public void Update(DisciplineCheck check)
        {
            using var conn = _db.OpenConnection();
            conn.Execute(
                @"UPDATE DisciplineChecks SET
                    Passed = @Passed,
                    Notes  = @Notes
                  WHERE Id = @Id;",
                check);
        }

        /// <summary>Returns all discipline checks for a specific trade.</summary>
        public IEnumerable<DisciplineCheck> GetByTrade(int tradeId)
        {
            using var conn = _db.OpenConnection();
            return conn.Query<DisciplineCheck>(
                "SELECT * FROM DisciplineChecks WHERE TradeId = @TradeId;",
                new { TradeId = tradeId });
        }

        /// <summary>Returns all discipline checks for a trading day (trade-level and day-level).</summary>
        public IEnumerable<DisciplineCheck> GetByDay(int dayId)
        {
            using var conn = _db.OpenConnection();
            return conn.Query<DisciplineCheck>(
                "SELECT * FROM DisciplineChecks WHERE DayId = @DayId;",
                new { DayId = dayId });
        }

        /// <summary>
        /// Returns all discipline checks within a date range (inclusive).
        /// Used by DisciplineScoreCalculator for rolling averages.
        /// Dates are ISO 8601 strings (yyyy-MM-dd).
        /// </summary>
        public IEnumerable<DisciplineCheck> GetByDateRange(string fromDate, string toDate)
        {
            using var conn = _db.OpenConnection();
            return conn.Query<DisciplineCheck>(
                @"SELECT dc.*
                  FROM DisciplineChecks dc
                  JOIN TradingDays td ON dc.DayId = td.Id
                  WHERE td.Date >= @From AND td.Date <= @To;",
                new { From = fromDate, To = toDate });
        }

        /// <summary>
        /// Upserts a discipline check for a given day + rule combination.
        /// Used for day-level rules (e.g. MaxAttemptsRespected) where only one
        /// row should exist per rule per day.
        /// </summary>
        public void UpsertDayLevelCheck(int dayId, string ruleKey, bool passed, string? notes = null)
        {
            using var conn = _db.OpenConnection();

            var existing = conn.QuerySingleOrDefault<DisciplineCheck>(
                "SELECT * FROM DisciplineChecks WHERE DayId = @DayId AND RuleKey = @RuleKey AND TradeId IS NULL;",
                new { DayId = dayId, RuleKey = ruleKey });

            if (existing == null)
            {
                conn.Execute(
                    @"INSERT INTO DisciplineChecks (TradeId, DayId, RuleKey, Passed, Notes)
                      VALUES (NULL, @DayId, @RuleKey, @Passed, @Notes);",
                    new { DayId = dayId, RuleKey = ruleKey, Passed = passed, Notes = notes });
            }
            else
            {
                conn.Execute(
                    "UPDATE DisciplineChecks SET Passed = @Passed, Notes = @Notes WHERE Id = @Id;",
                    new { Passed = passed, Notes = notes, Id = existing.Id });
            }
        }
    }
}
