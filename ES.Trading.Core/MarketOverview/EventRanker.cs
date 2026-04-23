using System;
using System.Collections.Generic;
using System.Linq;

namespace ES.Trading.Core.MarketOverview
{
    /// <summary>
    /// Rules-based, explainable scorer for market events.
    ///
    ///     score = base_event × source_tier × breadth_multiplier
    ///
    /// Boon/bust events that hit an entire sector bubble up via the breadth multiplier.
    /// Everything is tunable via the tables in this file — no ML, no opaque model.
    /// </summary>
    public class EventRanker
    {
        // ── Tunables ──────────────────────────────────────────────────────────

        private const double MacroBreadth  = 3.0;
        private const double SectorBreadth = 2.0;
        private const double SingleBreadth = 1.0;

        // 8-K Item code → base score. Higher = more material.
        // Ref: https://www.sec.gov/files/form8-k.pdf
        private static readonly Dictionary<string, double> FilingItemScores = new Dictionary<string, double>
        {
            { "1.01", 6.0 },  // Material definitive agreement
            { "1.02", 7.0 },  // Termination of material agreement
            { "1.03", 10.0 }, // Bankruptcy / receivership
            { "2.01", 8.0 },  // Completion of acquisition or disposition
            { "2.02", 7.0 },  // Results of operations (earnings release)
            { "2.03", 6.0 },  // Material direct financial obligation
            { "2.04", 8.0 },  // Triggering events accelerating obligations
            { "2.05", 7.0 },  // Costs associated with exit/disposal activities
            { "2.06", 8.0 },  // Material impairment
            { "3.01", 9.0 },  // Delisting / transfer of listing
            { "3.02", 5.0 },  // Unregistered sale of equity securities
            { "3.03", 5.0 },  // Material modification to rights of security holders
            { "4.01", 7.0 },  // Changes in registrant's certifying accountant
            { "4.02", 10.0 }, // Non-reliance on previously issued financial statements
            { "5.01", 8.0 },  // Changes in control of registrant
            { "5.02", 7.0 },  // Departure/appointment of directors or officers
            { "5.03", 3.0 },  // Amendments to articles/bylaws
            { "5.07", 4.0 },  // Submission of matters to vote of security holders
            { "7.01", 3.0 },  // Reg FD disclosure (often boilerplate)
            { "8.01", 5.0 },  // Other events (catch-all — often where big news lives)
            { "9.01", 2.0 }   // Financial statements and exhibits
        };

        // Source name (lower-cased) → tier multiplier.
        private static readonly Dictionary<string, double> SourceTiers = new Dictionary<string, double>
        {
            { "edgar",            1.0 },
            { "sec",              1.0 },
            { "federal reserve",  1.0 },
            { "treasurydirect",   1.0 },
            { "reuters",          0.9 },
            { "ap",               0.9 },
            { "associated press", 0.9 },
            { "wall street journal", 0.9 },
            { "wsj",              0.9 },
            { "bloomberg",        0.9 },
            { "cnbc",             0.75 },
            { "marketwatch",      0.75 },
            { "barron's",         0.75 },
            { "yahoo",            0.6 },
            { "finviz",           0.5 }
        };

        // Macro triggers — any of these in the headline flips breadth to Macro.
        private static readonly string[] MacroKeywords = new[]
        {
            "fed", "fomc", "powell", "interest rate", "rate cut", "rate hike",
            "cpi", "inflation", "pce", "nonfarm payroll", "jobless claims",
            "gdp", "recession",
            "tariff", "sanctions", "export ban", "trade war",
            "debt ceiling", "shutdown",
            "oil price", "opec", "crude",
            "treasury yield", "bond yield"
        };

        // Boon/bust language — presence boosts the base by +3 before multipliers.
        private static readonly string[] BoonBustKeywords = new[]
        {
            "bankruptcy", "going concern", "default",
            "fraud", "investigation", "probe", "subpoena", "indictment",
            "recall", "halt", "suspended",
            "merger", "acquisition", "takeover", "buyout",
            "downgrade", "upgrade",
            "approval", "rejected", "denied",
            "strike", "walkout",
            "cuts guidance", "lowers guidance", "slashes", "profit warning",
            "raises guidance", "beats estimates", "misses estimates",
            "earnings beat", "earnings miss"
        };

        // Sector keyword hints — first match wins. Used for news with no attached ticker.
        private static readonly (GicsSector sector, string[] keywords)[] SectorKeywords = new[]
        {
            (GicsSector.InformationTechnology, new[] { "chip", "semiconductor", "semi ",
                "nvidia", "tsmc", "software", "cloud computing", "cybersecurity" }),
            (GicsSector.Financials, new[] { "bank ", "banks ", "fdic", "regional bank",
                "stress test", "credit suisse", "loan loss", "deposit flight" }),
            (GicsSector.Energy, new[] { "oil", "opec", "crude", "gasoline", "refinery",
                "pipeline", "lng", "natural gas" }),
            (GicsSector.HealthCare, new[] { "fda", "drug ", "pharma", "biotech", "clinical trial",
                "medicare", "medicaid", "insurer" }),
            (GicsSector.Industrials, new[] { "boeing", "aerospace", "defense contractor",
                "manufacturing pmi", "shipping", "freight", "rail" }),
            (GicsSector.CommunicationServices, new[] { "telecom", "5g", "streaming",
                "antitrust", "ad revenue" }),
            (GicsSector.ConsumerDiscretionary, new[] { "retail sales", "auto sales",
                "ev sales", "homebuilder" }),
            (GicsSector.ConsumerStaples, new[] { "grocery", "consumer staples", "packaged food" }),
            (GicsSector.Materials, new[] { "copper", "steel", "lithium", "mining", "chemical" }),
            (GicsSector.Utilities, new[] { "utility", "power grid", "blackout" }),
            (GicsSector.RealEstate, new[] { "commercial real estate", "reit", "office vacancy",
                "housing market", "mortgage rate" })
        };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Compute and attach a score + rationale to the event. Mutates in place and returns.
        /// Safe to call multiple times; the last call wins.
        /// </summary>
        public MarketEvent Score(MarketEvent ev)
        {
            double baseScore  = BaseScore(ev, out string baseReason);
            double tier       = SourceTier(ev.Source);
            var    breadth    = InferBreadth(ev);
            double breadthMul = BreadthMultiplier(breadth);

            // Side effect: let caller see inferred breadth / sector even if they fed defaults.
            ev.Breadth = breadth;
            if (ev.Sector == GicsSector.Unknown)
                ev.Sector = InferSectorFromText(ev.Headline + " " + (ev.Summary ?? string.Empty));

            ev.Score = baseScore * tier * breadthMul;
            ev.ScoreReason = baseReason
                + " × src=" + tier.ToString("0.##")
                + " × breadth=" + breadth;
            return ev;
        }

        /// <summary>Score a batch and return sorted descending by score.</summary>
        public List<MarketEvent> RankAll(IEnumerable<MarketEvent> events)
        {
            var scored = events.Select(Score).ToList();
            scored.Sort((a, b) => b.Score.CompareTo(a.Score));
            return scored;
        }

        /// <summary>
        /// Aggregate ranked events into per-sector heat. Only sectors with at least one
        /// event are included. Within each sector, events are kept sorted descending.
        /// </summary>
        public List<SectorHeat> BuildSectorHeat(IEnumerable<MarketEvent> rankedEvents, int topPerSector = 5)
        {
            var result = new Dictionary<GicsSector, SectorHeat>();
            foreach (var ev in rankedEvents)
            {
                if (ev.Sector == GicsSector.Unknown) continue;
                if (!result.TryGetValue(ev.Sector, out var heat))
                {
                    heat = new SectorHeat { Sector = ev.Sector };
                    result[ev.Sector] = heat;
                }
                heat.TotalScore += ev.Score;
                heat.EventCount += 1;
                if (heat.TopEvents.Count < topPerSector)
                    heat.TopEvents.Add(ev);
            }
            return result.Values.OrderByDescending(h => h.TotalScore).ToList();
        }

        // ── Base score per event kind ─────────────────────────────────────────

        private double BaseScore(MarketEvent ev, out string reason)
        {
            switch (ev.Kind)
            {
                case EventKind.EdgarFiling:  return FilingBase(ev, out reason);
                case EventKind.FedEvent:     return FedBase(ev, out reason);
                case EventKind.TreasuryAuction: return TreasuryBase(ev, out reason);
                case EventKind.EconomicRelease: return EconomicBase(ev, out reason);
                case EventKind.News:
                default:                     return NewsBase(ev, out reason);
            }
        }

        private double FilingBase(MarketEvent ev, out string reason)
        {
            if (ev.FilingItems == null || ev.FilingItems.Count == 0)
            {
                reason = "filing(no items)=5";
                return 5.0;
            }
            double best = 0;
            string bestItem = "";
            foreach (var item in ev.FilingItems)
            {
                if (FilingItemScores.TryGetValue(item, out var s) && s > best)
                {
                    best = s;
                    bestItem = item;
                }
            }
            if (best == 0) best = 4.0;   // unknown item code
            reason = "8-K Item " + (bestItem.Length > 0 ? bestItem : "?") + "=" + best.ToString("0.#");
            return best;
        }

        private double FedBase(MarketEvent ev, out string reason)
        {
            var h = (ev.Headline ?? string.Empty).ToLowerInvariant();
            if (h.Contains("fomc") && (h.Contains("decision") || h.Contains("statement") || h.Contains("rate")))
            { reason = "FOMC decision=10"; return 10.0; }
            if (h.Contains("minutes")) { reason = "FOMC minutes=8"; return 8.0; }
            if (h.Contains("powell") || h.Contains("chair"))
            { reason = "Chair speech=8"; return 8.0; }
            reason = "Fed speaker=5"; return 5.0;
        }

        private double TreasuryBase(MarketEvent ev, out string reason)
        {
            var h = (ev.Headline ?? string.Empty).ToLowerInvariant();
            if (h.Contains("30-year") || h.Contains("30 year"))
            { reason = "30Y auction=6"; return 6.0; }
            if (h.Contains("10-year") || h.Contains("10 year"))
            { reason = "10Y auction=6"; return 6.0; }
            if (h.Contains("bill")) { reason = "T-bill auction=2"; return 2.0; }
            reason = "Treasury auction=4"; return 4.0;
        }

        private double EconomicBase(MarketEvent ev, out string reason)
        {
            // Economic releases pass through News path normally; this is a fallback if
            // someone constructs a MarketEvent with EventKind.EconomicRelease directly.
            reason = "economic release=7"; return 7.0;
        }

        private double NewsBase(MarketEvent ev, out string reason)
        {
            double boost = HasAny(ev.Headline, BoonBustKeywords) ? 3.0 : 0.0;
            double baseScore = 5.0 + boost;
            reason = boost > 0 ? "news+boon/bust=" + baseScore.ToString("0.#") : "news=5";
            return baseScore;
        }

        // ── Multipliers ───────────────────────────────────────────────────────

        private double SourceTier(string? source)
        {
            if (string.IsNullOrEmpty(source)) return 0.5;
            var lc = source!.ToLowerInvariant();
            foreach (var kvp in SourceTiers)
                if (lc.Contains(kvp.Key)) return kvp.Value;
            return 0.5;
        }

        private static double BreadthMultiplier(EventBreadth b) => b switch
        {
            EventBreadth.Macro        => MacroBreadth,
            EventBreadth.Sector       => SectorBreadth,
            EventBreadth.SingleTicker => SingleBreadth,
            _                         => SingleBreadth
        };

        // ── Inference helpers ────────────────────────────────────────────────

        private EventBreadth InferBreadth(MarketEvent ev)
        {
            // Macro events are almost always of kind FedEvent / TreasuryAuction / EconomicRelease.
            if (ev.Kind == EventKind.FedEvent
                || ev.Kind == EventKind.TreasuryAuction
                || ev.Kind == EventKind.EconomicRelease)
                return EventBreadth.Macro;

            // News/filings: macro keywords win, then sector hint, else single ticker.
            string text = (ev.Headline ?? string.Empty) + " " + (ev.Summary ?? string.Empty);
            if (HasAny(text, MacroKeywords)) return EventBreadth.Macro;

            if (ev.Sector != GicsSector.Unknown) return EventBreadth.Sector;

            var inferred = InferSectorFromText(text);
            return inferred != GicsSector.Unknown
                ? EventBreadth.Sector
                : EventBreadth.SingleTicker;
        }

        private GicsSector InferSectorFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return GicsSector.Unknown;
            var lc = text.ToLowerInvariant();
            foreach (var (sector, keywords) in SectorKeywords)
            {
                foreach (var kw in keywords)
                    if (lc.Contains(kw)) return sector;
            }
            return GicsSector.Unknown;
        }

        private static bool HasAny(string? text, string[] needles)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var lc = text!.ToLowerInvariant();
            for (int i = 0; i < needles.Length; i++)
                if (lc.Contains(needles[i])) return true;
            return false;
        }
    }
}
