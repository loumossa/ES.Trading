namespace ES.Trading.Core.MarketOverview
{
    /// <summary>Where the event came from / what kind of event it is.</summary>
    public enum EventKind
    {
        EdgarFiling,
        News,
        FedEvent,
        TreasuryAuction,
        EconomicRelease
    }

    /// <summary>Hot / mid / low heat classification used by the economic calendar and by the UI.</summary>
    public enum ImpactTier
    {
        Low,
        Medium,
        High
    }

    /// <summary>How broadly the event is expected to reach across the tape.</summary>
    public enum EventBreadth
    {
        SingleTicker,
        Sector,
        Macro
    }

    /// <summary>GICS 11 sectors plus a catch-all. Used for sector heat aggregation.</summary>
    public enum GicsSector
    {
        Unknown,
        CommunicationServices,
        ConsumerDiscretionary,
        ConsumerStaples,
        Energy,
        Financials,
        HealthCare,
        Industrials,
        InformationTechnology,
        Materials,
        RealEstate,
        Utilities
    }

    /// <summary>Pretty-print helpers for UI binding.</summary>
    public static class EnumExtensions
    {
        public static string ToDisplay(this GicsSector s) => s switch
        {
            GicsSector.CommunicationServices => "Communication Services",
            GicsSector.ConsumerDiscretionary => "Consumer Discretionary",
            GicsSector.ConsumerStaples       => "Consumer Staples",
            GicsSector.HealthCare            => "Health Care",
            GicsSector.InformationTechnology => "Information Technology",
            GicsSector.RealEstate            => "Real Estate",
            GicsSector.Unknown               => "Other",
            _                                => s.ToString()
        };

        public static string ToDisplay(this ImpactTier t) => t switch
        {
            ImpactTier.High   => "HOT",
            ImpactTier.Medium => "MID",
            ImpactTier.Low    => "LOW",
            _                 => "—"
        };
    }
}
