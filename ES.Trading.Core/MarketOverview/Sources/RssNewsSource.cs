using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ES.Trading.Core.MarketOverview.Sources
{
    /// <summary>
    /// Fans out to a fixed set of free, keyless news RSS feeds, parses each as
    /// RSS 2.0 or Atom 1.0, and returns items newer than <c>sinceUtc</c>.
    ///
    /// Reuters pulled their public RSS in 2020 so it's not in the list. CNBC /
    /// MarketWatch / Yahoo Finance are the three most reliable free feeds. One
    /// failing feed doesn't sink the whole source — we aggregate best-effort.
    /// </summary>
    public class RssNewsSource : INewsSource
    {
        // (url, displaySource)
        private static readonly (string url, string source)[] Feeds = new[]
        {
            ("https://search.cnbc.com/rs/search/combinedcms/view.xml?partnerId=wrss01&id=10000664",
                "CNBC"),
            ("https://search.cnbc.com/rs/search/combinedcms/view.xml?partnerId=wrss01&id=100003114",
                "CNBC"),
            ("https://feeds.content.dowjones.io/public/rss/mw_topstories",       "MarketWatch"),
            ("https://feeds.content.dowjones.io/public/rss/mw_realtimeheadlines","MarketWatch"),
            ("https://finance.yahoo.com/news/rssindex",                          "Yahoo Finance")
        };

        // XML namespaces commonly seen in Atom / Dublin Core.
        private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
        private static readonly XNamespace Dc   = "http://purl.org/dc/elements/1.1/";

        private readonly int _maxItems;

        public RssNewsSource(int maxItems = 200) { _maxItems = maxItems; }

        public async Task<IReadOnlyList<MarketEvent>> GetHeadlinesSinceAsync(DateTime sinceUtc, CancellationToken ct)
        {
            var tasks = new List<Task<IReadOnlyList<MarketEvent>>>();
            foreach (var (url, source) in Feeds)
                tasks.Add(FetchOneAsync(url, source, sinceUtc, ct));

            var all = new List<MarketEvent>();
            foreach (var t in tasks)
            {
                try
                {
                    var items = await t.ConfigureAwait(false);
                    all.AddRange(items);
                }
                catch
                {
                    // Individual feed failure — skip. The orchestrator surfaces per-source
                    // errors at the source level; we don't want one bad feed to drop the lot.
                }
            }
            return all
                .OrderByDescending(e => e.TimestampUtc)
                .Take(_maxItems)
                .ToList();
        }

        private async Task<IReadOnlyList<MarketEvent>> FetchOneAsync(string url, string source, DateTime sinceUtc, CancellationToken ct)
        {
            using var resp = await MarketOverviewHttp.Generic.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            string xml = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null) return Array.Empty<MarketEvent>();

            return IsAtom(root)
                ? ParseAtom(root, source, sinceUtc)
                : ParseRss(root, source, sinceUtc);
        }

        private static bool IsAtom(XElement root)
            => root.Name.LocalName.Equals("feed", StringComparison.OrdinalIgnoreCase)
               || root.GetDefaultNamespace() == Atom;

        private IReadOnlyList<MarketEvent> ParseRss(XElement root, string source, DateTime sinceUtc)
        {
            var results = new List<MarketEvent>();
            foreach (var item in root.Descendants("item"))
            {
                string title = (string?)item.Element("title") ?? string.Empty;
                string link  = (string?)item.Element("link")  ?? string.Empty;
                string desc  = (string?)item.Element("description") ?? string.Empty;
                string dateStr = (string?)item.Element("pubDate")
                              ?? (string?)item.Element(Dc + "date")
                              ?? string.Empty;

                if (!TryParseDate(dateStr, out var ts)) ts = DateTime.UtcNow;
                if (ts < sinceUtc) continue;
                if (string.IsNullOrWhiteSpace(title)) continue;

                results.Add(new MarketEvent
                {
                    Kind         = EventKind.News,
                    Headline     = StripHtml(title),
                    Summary      = StripHtml(desc),
                    Source       = source,
                    Url          = link,
                    TimestampUtc = ts
                });
            }
            return results;
        }

        private IReadOnlyList<MarketEvent> ParseAtom(XElement root, string source, DateTime sinceUtc)
        {
            var results = new List<MarketEvent>();
            foreach (var entry in root.Descendants(Atom + "entry"))
            {
                string title = (string?)entry.Element(Atom + "title") ?? string.Empty;
                var linkEl   = entry.Element(Atom + "link");
                string link  = linkEl != null ? (string?)linkEl.Attribute("href") ?? string.Empty : string.Empty;
                string desc  = (string?)entry.Element(Atom + "summary")
                             ?? (string?)entry.Element(Atom + "content")
                             ?? string.Empty;
                string dateStr = (string?)entry.Element(Atom + "published")
                              ?? (string?)entry.Element(Atom + "updated")
                              ?? string.Empty;

                if (!TryParseDate(dateStr, out var ts)) ts = DateTime.UtcNow;
                if (ts < sinceUtc) continue;
                if (string.IsNullOrWhiteSpace(title)) continue;

                results.Add(new MarketEvent
                {
                    Kind         = EventKind.News,
                    Headline     = StripHtml(title),
                    Summary      = StripHtml(desc),
                    Source       = source,
                    Url          = link,
                    TimestampUtc = ts
                });
            }
            return results;
        }

        private static bool TryParseDate(string s, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var off))
            {
                utc = off.UtcDateTime;
                return true;
            }
            return false;
        }

        private static string StripHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            // Minimal tag strip — RSS descriptions routinely contain <p>, <a>, CDATA-wrapped HTML.
            var sb = new System.Text.StringBuilder(s.Length);
            bool inTag = false;
            foreach (var c in s)
            {
                if (c == '<') { inTag = true; continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return System.Net.WebUtility.HtmlDecode(sb.ToString().Trim());
        }
    }
}
