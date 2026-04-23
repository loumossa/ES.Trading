using System;
using System.Net;
using System.Net.Http;

namespace ES.Trading.Core.MarketOverview
{
    /// <summary>
    /// Shared HttpClient plus TLS bootstrap. On net48 the default SecurityProtocol
    /// excludes TLS 1.2, which most modern endpoints require — we bump it here once.
    ///
    /// We expose two clients because SEC requires a contact User-Agent and refuses
    /// requests without one; other endpoints don't want to see that UA.
    /// </summary>
    public static class MarketOverviewHttp
    {
        private static readonly object _lock = new object();
        private static bool _tlsInitialized;

        public static HttpClient Generic   { get; } = CreateGeneric();
        public static HttpClient SecEdgar  { get; private set; } = null!;

        /// <summary>
        /// Call once at startup with the configured options (gives SEC client its UA).
        /// Safe to call multiple times — subsequent calls are ignored.
        /// </summary>
        public static void Configure(MarketOverviewOptions options)
        {
            EnsureTls();
            lock (_lock)
            {
                if (SecEdgar != null) return;
                var sec = new HttpClient { Timeout = options.RequestTimeout };
                sec.DefaultRequestHeaders.UserAgent.ParseAdd(options.SecUserAgent);
                sec.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                SecEdgar = sec;
                Generic.Timeout = options.RequestTimeout;
            }
        }

        private static HttpClient CreateGeneric()
        {
            EnsureTls();
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            // Some endpoints (Yahoo in particular) dislike the default .NET UA.
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; ES.Trading/1.0)");
            return c;
        }

        private static void EnsureTls()
        {
            if (_tlsInitialized) return;
            lock (_lock)
            {
                if (_tlsInitialized) return;
                // Tls12 is required for most modern endpoints. Tls13 may not be defined
                // on older net48 patch levels, so guard the flag.
                var protocols = SecurityProtocolType.Tls12;
                try
                {
                    var tls13 = (SecurityProtocolType)Enum.Parse(typeof(SecurityProtocolType), "Tls13");
                    protocols |= tls13;
                }
                catch { /* Tls13 not available — Tls12 is sufficient */ }
                ServicePointManager.SecurityProtocol |= protocols;
                _tlsInitialized = true;
            }
        }
    }
}
