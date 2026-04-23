using System;
using System.Collections.Generic;
using System.Linq;
using ES.Trading.Core.Data;
using ES.Trading.Core.Models;

namespace ES.Trading.Core.Services
{
    /// <summary>
    /// Calculates discipline scores from DisciplineCheck records.
    ///
    /// Scoring philosophy:
    ///   - Only applicable rules count. A trade with no add-on is not penalized for
    ///     AddOnRulesFollowed — that rule simply has no denominator contribution.
    ///   - Score = (rules passed / rules applicable) × 100, as a percentage.
    ///   - MES fractional exit trades are excluded entirely from scoring.
    /// </summary>
    public class DisciplineScoreCalculator
    {
        private readonly DisciplineRepository _disciplineRepo;

        public DisciplineScoreCalculator(DisciplineRepository disciplineRepo)
        {
            _disciplineRepo = disciplineRepo;
        }

        /// <summary>
        /// Returns the discipline score for a single trading day.
        /// Returns null if no applicable checks exist for that day.
        /// </summary>
        public DisciplineScore? CalculateForDay(int dayId)
        {
            var checks = _disciplineRepo.GetByDay(dayId).ToList();
            return BuildScore(checks, dayId.ToString());
        }

        /// <summary>
        /// Returns the rolling discipline score across the last N calendar days.
        /// Uses all checks within [today - rollingDays, today].
        /// </summary>
        public DisciplineScore? CalculateRolling(int rollingDays)
        {
            var toDate   = DateTime.Today.ToString("yyyy-MM-dd");
            var fromDate = DateTime.Today.AddDays(-rollingDays).ToString("yyyy-MM-dd");

            var checks = _disciplineRepo.GetByDateRange(fromDate, toDate).ToList();
            return BuildScore(checks, $"Last {rollingDays} days");
        }

        /// <summary>
        /// Returns per-day scores for the last N trading days, ordered newest first.
        /// Used to render the discipline trend chart in the Desktop App.
        /// </summary>
        public IEnumerable<DailyDisciplineScore> CalculateDailyTrend(
            IEnumerable<TradingDay> days,
            Func<int, IEnumerable<DisciplineCheck>> getChecksForDay)
        {
            return days.Select(day =>
            {
                var checks = getChecksForDay(day.Id).ToList();
                var score  = BuildScore(checks, day.Date);

                return new DailyDisciplineScore
                {
                    Date        = day.Date,
                    Score       = score?.Percentage,
                    Applicable  = score?.ApplicableCount ?? 0,
                    Passed      = score?.PassedCount ?? 0
                };
            }).OrderByDescending(d => d.Date);
        }

        /// <summary>
        /// Returns a breakdown of pass rate per rule across a set of checks.
        /// Used to identify which rules are being violated most frequently.
        /// </summary>
        public IEnumerable<RuleBreakdown> GetRuleBreakdown(IEnumerable<DisciplineCheck> checks)
        {
            return checks
                .GroupBy(c => c.RuleKey)
                .Select(g => new RuleBreakdown
                {
                    RuleKey      = g.Key,
                    Label        = DisciplineRules.GetLabel(g.Key),
                    TotalChecks  = g.Count(),
                    PassedChecks = g.Count(c => c.Passed),
                    PassRate     = g.Count() > 0
                        ? (double)g.Count(c => c.Passed) / g.Count() * 100.0
                        : 0.0
                })
                .OrderBy(r => r.PassRate);  // worst rules first
        }

        // ─── Private ──────────────────────────────────────────────────────────────

        private static DisciplineScore? BuildScore(List<DisciplineCheck> checks, string label)
        {
            if (checks.Count == 0) return null;

            int applicable = checks.Count;
            int passed     = checks.Count(c => c.Passed);
            double pct     = (double)passed / applicable * 100.0;

            var byRule = checks
                .GroupBy(c => c.RuleKey)
                .ToDictionary(
                    g => g.Key,
                    g => new RuleResult
                    {
                        RuleKey      = g.Key,
                        Label        = DisciplineRules.GetLabel(g.Key),
                        PassedCount  = g.Count(c => c.Passed),
                        TotalCount   = g.Count()
                    });

            return new DisciplineScore
            {
                Label           = label,
                Percentage      = Math.Round(pct, 1),
                PassedCount     = passed,
                ApplicableCount = applicable,
                ByRule          = byRule
            };
        }
    }

    // ─── Result types ─────────────────────────────────────────────────────────────

    public class DisciplineScore
    {
        /// <summary>Display label — e.g. the date string or "Last 20 days".</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Overall score as a percentage (0–100), rounded to 1 decimal place.</summary>
        public double Percentage { get; set; }

        public int PassedCount { get; set; }
        public int ApplicableCount { get; set; }

        /// <summary>Per-rule breakdown. Key is the DisciplineRules constant.</summary>
        public Dictionary<string, RuleResult> ByRule { get; set; } = new();

        public override string ToString() =>
            $"{Percentage:F1}% ({PassedCount}/{ApplicableCount} rules passed)";
    }

    public class RuleResult
    {
        public string RuleKey { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int PassedCount { get; set; }
        public int TotalCount { get; set; }
        public double PassRate => TotalCount > 0 ? (double)PassedCount / TotalCount * 100.0 : 0.0;
    }

    public class DailyDisciplineScore
    {
        public string Date { get; set; } = string.Empty;

        /// <summary>Null if no checks exist for this day.</summary>
        public double? Score { get; set; }

        public int Applicable { get; set; }
        public int Passed { get; set; }
    }

    public class RuleBreakdown
    {
        public string RuleKey { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int TotalChecks { get; set; }
        public int PassedChecks { get; set; }
        public double PassRate { get; set; }
    }
}
