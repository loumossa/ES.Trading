using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ES.Trading.Core.MarketOverview.Sources
{
    /// <summary>
    /// Batched quote fetch from Yahoo Finance. One HTTP call returns every symbol
    /// on the overnight strip.
    ///
    /// Since ~2023 Yahoo requires a session cookie + crumb parameter on the v7
    /// quote endpoint — hitting it cold returns 401 Unauthorized. We do a one-time
    /// handshake per instance:
    ///
    ///   1. GET fc.yahoo.com   → seeds the A1/A3 consent cookies (returns 404 body,
    ///                            which is fine — we only care about Set-Cookie)
    ///   2. GET /v1/test/getcrumb (with those cookies) → returns the crumb string
    ///   3. Every quote call appends &amp;crumb=... and rides the same cookie jar
    ///
    /// If a quote call later returns 401 we invalidate the crumb and retry once —
    /// covers cookie/crumb expiry without surfacing the error to the user.
    ///
    /// This is the free placeholder until Schwab API is wired up as the primary
    /// market-data source. Yahoo can change this contract at any time; if it
    /// breaks again, swap in a Stooq CSV fallback or move straight to Schwab.
    /// </summary>
    public class YahooMarketDataSource : IMarketDataSource
    {
        private const string CrumbUrl  = "https://query2.finance.yahoo.com/v1/test/getcrumb";
        private const string CookieUrl = "https://fc.yahoo.com/";
        private const string QuoteBase = "https://query1.finance.yahoo.com/v7/finance/quote?symbols=";

        private static readonly (string sym, string name, string cat)[] DefaultSymbols = new[]
        {
            ("ES=F",   "S&P 500 Futures",  "US Futures"),
            ("NQ=F",   "Nasdaq Futures",   "US Futures"),
            ("YM=F",   "Dow Futures",      "US Futures"),
            ("RTY=F",  "Russell Futures",  "US Futures"),
            ("^VIX",   "VIX",              "Rates/Vol"),
            ("^TNX",   "10Y Yield",        "Rates/Vol"),
            ("DX=F",   "Dollar Index",     "Rates/Vol"),
            ("CL=F",   "Crude Oil",        "Commodities"),
            ("GC=F",   "Gold",             "Commodities"),
            ("^N225",  "Nikkei 225",       "Global"),
            ("^HSI",   "Hang Seng",        "Global"),
            ("^FTSE",  "FTSE 100",         "Global"),
            ("^GDAXI", "DAX",              "Global")
        };

        private readonly HttpClient    _client;
        private readonly SemaphoreSlim _crumbLock = new SemaphoreSlim(1, 1);
        private          string?      _crumb;

        public YahooMarketDataSource()
        {
            var handler = new HttpClientHandler
            {
                UseCookies       = true,
                CookieContainer  = new CookieContainer(),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

            // A real browser UA is required — Yahoo blocks traffic with generic / empty UAs.
            _client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
                + "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/plain, */*");
            _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        }

        public async Task<IReadOnlyList<OvernightQuote>> GetOvernightQuotesAsync(CancellationToken ct)
        {
            var raw = await FetchQuotesAsync(ct).ConfigureAwait(false);

            // Return in declared order so the UI strip is stable regardless of response order.
            var results = new List<OvernightQuote>();
            foreach (var (sym, name, cat) in DefaultSymbols)
            {
                if (!raw.TryGetValue(sym, out var q))
                    q = new OvernightQuote { Symbol = sym };
                q.DisplayName = name;
                q.Category    = cat;
                results.Add(q);
            }
            return results;
        }

        private async Task<Dictionary<string, OvernightQuote>> FetchQuotesAsync(CancellationToken ct)
        {
            await EnsureCrumbAsync(ct).ConfigureAwait(false);

            string url = BuildQuoteUrl(_crumb);
            using (var resp = await _client.GetAsync(url, ct).ConfigureAwait(false))
            {
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Cookie / crumb expired — force a fresh handshake and retry once.
                    _crumb = null;
                    await EnsureCrumbAsync(ct).ConfigureAwait(false);
                    using var retry = await _client.GetAsync(BuildQuoteUrl(_crumb), ct).ConfigureAwait(false);
                    retry.EnsureSuccessStatusCode();
                    return ParseQuotes(await retry.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
                resp.EnsureSuccessStatusCode();
                return ParseQuotes(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            }
        }

        private static string BuildQuoteUrl(string? crumb)
        {
            string symbols = string.Join(",", Array.ConvertAll(DefaultSymbols, s => s.sym));
            string url = QuoteBase + Uri.EscapeDataString(symbols);
            if (!string.IsNullOrEmpty(crumb))
                url += "&crumb=" + Uri.EscapeDataString(crumb!);
            return url;
        }

        private async Task EnsureCrumbAsync(CancellationToken ct)
        {
            if (_crumb != null) return;
            await _crumbLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_crumb != null) return;

                // Step 1: seed consent cookies. fc.yahoo.com answers with 404 — we only
                // care about the Set-Cookie headers it writes to our CookieContainer.
                try
                {
                    using var _ = await _client.GetAsync(CookieUrl, ct).ConfigureAwait(false);
                }
                catch { /* cookie seeding is best-effort */ }

                // Step 2: fetch the crumb. Now that cookies are seeded, this returns a
                // short opaque string (~11 chars) that must be echoed with each quote call.
                using var resp = await _client.GetAsync(CrumbUrl, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                var crumb = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                _crumb = crumb?.Trim();
                if (string.IsNullOrEmpty(_crumb))
                    throw new InvalidOperationException("Yahoo returned an empty crumb");
            }
            finally
            {
                _crumbLock.Release();
            }
        }

        private static Dictionary<string, OvernightQuote> ParseQuotes(string json)
        {
            var dict = new Dictionary<string, OvernightQuote>(StringComparer.OrdinalIgnoreCase);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("quoteResponse", out var qr)
                && qr.TryGetProperty("result", out var result)
                && result.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in result.EnumerateArray())
                {
                    string sym = GetString(el, "symbol") ?? string.Empty;
                    if (string.IsNullOrEmpty(sym)) continue;
                    dict[sym] = new OvernightQuote
                    {
                        Symbol       = sym,
                        Last         = GetDouble(el, "regularMarketPrice"),
                        ChangeAbs    = GetDouble(el, "regularMarketChange"),
                        ChangePct    = GetDouble(el, "regularMarketChangePercent"),
                        QuoteTimeUtc = GetUnixSeconds(el, "regularMarketTime")
                    };
                }
            }
            return dict;
        }

        private static string? GetString(JsonElement el, string prop)
            => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static double? GetDouble(JsonElement el, string prop)
        {
            if (!el.TryGetProperty(prop, out var v)) return null;
            if (v.ValueKind != JsonValueKind.Number) return null;
            return v.TryGetDouble(out var d) ? d : (double?)null;
        }

        private static DateTime? GetUnixSeconds(JsonElement el, string prop)
        {
            if (!el.TryGetProperty(prop, out var v)) return null;
            if (v.ValueKind != JsonValueKind.Number) return null;
            if (!v.TryGetInt64(out var secs)) return null;
            return DateTimeOffset.FromUnixTimeSeconds(secs).UtcDateTime;
        }
    }
}
