using System.Net;
using System.Text.RegularExpressions;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //guards against pages quietly depending on other websites. The content security policy only allows scripts, styles, fonts and images from this site,
    //so a page that pulls a font or a library from elsewhere works on a developer's machine (the policy only reports there) and then breaks once hosted.
    //it also keeps visitors' addresses from being handed to third parties, which the design promises not to do.
    //these tests read every page, every stylesheet they link and every font those stylesheets name
    [Collection("Api")]
    public class ExternalResourceTests
    {
        //which attributes load something, for each kind of tag
        private static readonly Dictionary<string, string[]> LoadingAttributes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["link"] = new[] { "href" },
            ["script"] = new[] { "src" },
            ["img"] = new[] { "src", "srcset" },
            ["source"] = new[] { "src", "srcset" },
            ["iframe"] = new[] { "src" },
            ["embed"] = new[] { "src" },
            ["object"] = new[] { "data" },
            ["video"] = new[] { "src", "poster" },
            ["audio"] = new[] { "src" },
            ["track"] = new[] { "src" },
            ["form"] = new[] { "action" },
            ["base"] = new[] { "href" }
        };

        //the bootstrap and jquery files the older pages name are not in the repository yet (issue #20), so they cannot be served. Remove this when they are
        private static readonly string[] KnownMissing = { "/lib/" };

        private readonly ApiFactory _factory;

        public ExternalResourceTests(ApiFactory factory) => _factory = factory;

        private record Reference(string Page, string Tag, string Attribute, string Value);

        //-----------------------------
        //every page the site shows to each kind of visitor, with some data on it so lists and tables are rendered too
        private async Task<List<(string Url, string Html)>> PagesAsync()
        {
            var user = await _factory.CreateLinkedUserAsync();
            await _factory.AddEntryAsync(user.AppUserId, "Acme", "Web", 2, DateTime.Today.AddDays(-2).AddHours(9), 60, "note");
            var developer = _factory.NewClient();
            await ApiFactory.PostLoginAsync(developer, user.Email, user.Password);
            var admin = _factory.NewClient();
            await ApiFactory.PostLoginAsync(admin, ApiFactory.AdminEmail, ApiFactory.AdminPassword);
            var anonymous = _factory.NewClient();

            var pages = new List<(string, HttpClient)>
            {
                ("/Account/Login", anonymous), ("/Home/Privacy", anonymous), ("/Home/Error", anonymous),
                ("/", developer), ("/?period=week", developer), ("/Timesheet", developer), ("/Timesheet?view=month", developer),
                ("/Report?view=week", developer), ("/Report?view=month", developer), ("/CsvUpload", developer), ("/Settings", developer), ("/Account/AccessDenied", developer),
                ("/Admin", admin), ("/Admin/Submissions", admin), ("/Admin/Exports", admin), ("/Users", admin)
            };

            var result = new List<(string, string)>();
            foreach (var (url, client) in pages)
            {
                var response = await client.GetAsync(url);
                Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.InternalServerError, $"{url} answered {(int)response.StatusCode}");
                result.Add((url, await response.Content.ReadAsStringAsync()));
            }
            return result;
        }

        private static IEnumerable<Reference> ReferencesIn(string page, string html)
        {
            foreach (Match tag in Regex.Matches(html, @"<(?<name>[a-zA-Z0-9]+)\b(?<attributes>[^>]*)>"))
            {
                if (!LoadingAttributes.TryGetValue(tag.Groups["name"].Value, out var names))
                    continue;
                foreach (Match attribute in Regex.Matches(tag.Groups["attributes"].Value, @"(?<name>[a-zA-Z-]+)\s*=\s*""(?<value>[^""]*)"""))
                    if (names.Contains(attribute.Groups["name"].Value, StringComparer.OrdinalIgnoreCase))
                        yield return new Reference(page, tag.Groups["name"].Value, attribute.Groups["name"].Value, WebUtility.HtmlDecode(attribute.Groups["value"].Value));
            }
        }

        //an address on another site: a full web address or one that starts with two slashes
        private static bool IsExternal(string value) => Regex.IsMatch(value.Trim(), @"^(https?:)?//", RegexOptions.IgnoreCase);

        //the urls a stylesheet loads: every url(...) and @import
        private static IEnumerable<string> UrlsIn(string css)
        {
            foreach (Match m in Regex.Matches(css, @"url\(\s*['""]?(?<u>[^'"")]+)['""]?\s*\)", RegexOptions.IgnoreCase))
                yield return m.Groups["u"].Value.Trim();
            foreach (Match m in Regex.Matches(css, @"@import\s+(?:url\(\s*)?['""]?(?<u>[^'"")\s;]+)", RegexOptions.IgnoreCase))
                yield return m.Groups["u"].Value.Trim();
        }

        private static string StripQuery(string url) => url.Split('?', '#')[0];

        // ---------- nothing from another site ----------

        [Fact]
        public async Task NoPageAndNoStylesheetLoadsAnythingFromAnotherSite()
        {
            var pages = await PagesAsync();
            var offenders = new List<string>();
            var stylesheets = new HashSet<string>();

            foreach (var (url, html) in pages)
            {
                foreach (var reference in ReferencesIn(url, html))
                {
                    if (IsExternal(reference.Value))
                        offenders.Add($"{url}: <{reference.Tag} {reference.Attribute}=\"{reference.Value}\">");
                    else if (reference.Tag == "link" && reference.Value.Contains(".css", StringComparison.OrdinalIgnoreCase))
                        stylesheets.Add(StripQuery(reference.Value));
                }
            }

            //a stylesheet can pull in another site too, with url(...) or @import, so each one is read as well
            var client = _factory.NewClient();
            foreach (var sheet in stylesheets.Where(s => !KnownMissing.Any(s.StartsWith)))
            {
                var response = await client.GetAsync(sheet);
                //a stylesheet that is not served is reported by the other test, here there is nothing to read
                if (response.StatusCode != HttpStatusCode.OK)
                    continue;
                offenders.AddRange(UrlsIn(await response.Content.ReadAsStringAsync()).Where(IsExternal).Select(u => $"{sheet}: url {u}"));
            }

            Assert.True(offenders.Count == 0, "These load something from another site, which the content security policy blocks once hosted and which tells that site who is visiting:\n" + string.Join("\n", offenders.Distinct()));
        }

        // ---------- everything local is really there ----------

        [Fact]
        public async Task EveryStylesheetScriptAndFontAPageNamesIsServed()
        {
            var pages = await PagesAsync();
            var client = _factory.NewClient();
            var needed = new HashSet<string>();

            foreach (var (url, html) in pages)
                foreach (var reference in ReferencesIn(url, html).Where(r => r.Tag is "link" or "script" or "img" && !IsExternal(r.Value) && r.Value.StartsWith('/')))
                    needed.Add(StripQuery(reference.Value));

            //the fonts and images a stylesheet names count too
            foreach (var sheet in needed.Where(n => n.EndsWith(".css", StringComparison.OrdinalIgnoreCase) && !KnownMissing.Any(n.StartsWith)).ToList())
            {
                var sheetResponse = await client.GetAsync(sheet);
                if (sheetResponse.StatusCode != HttpStatusCode.OK)
                    continue;
                var css = await sheetResponse.Content.ReadAsStringAsync();
                foreach (var u in UrlsIn(css).Where(u => !IsExternal(u) && !u.StartsWith("data:", StringComparison.OrdinalIgnoreCase)))
                {
                    var resolved = new Uri(new Uri("https://localhost" + sheet), StripQuery(u)).AbsolutePath;
                    needed.Add(resolved);
                }
            }

            var missing = new List<string>();
            foreach (var path in needed.Where(n => !KnownMissing.Any(n.StartsWith)))
            {
                var response = await client.GetAsync(path);
                if (response.StatusCode != HttpStatusCode.OK || (await response.Content.ReadAsByteArrayAsync()).Length == 0)
                    missing.Add($"{path} answered {(int)response.StatusCode}");
            }

            Assert.True(missing.Count == 0, "A page refers to files this site does not serve:\n" + string.Join("\n", missing));
            Assert.Contains("/css/fonts.css", needed);
            Assert.Contains("/fonts/inter-latin.woff2", needed);
            Assert.Contains("/fonts/inter-latin-ext.woff2", needed);
        }

        // ---------- the typeface itself ----------

        [Fact]
        public async Task TheInterFont_IsServedFromThisSite_AndLoadedByTheLoginPageAndTheApp()
        {
            var client = _factory.NewClient();
            var login = await client.GetStringAsync("/Account/Login");
            var user = await _factory.CreateLinkedUserAsync();
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, user.Email, user.Password);
            var home = await browser.GetStringAsync("/");

            foreach (var html in new[] { login, home })
            {
                Assert.Matches(@"<link rel=""stylesheet"" href=""/css/fonts\.css\?v=[^""]+""", html);
                Assert.Contains("<link rel=\"preload\" href=\"/fonts/inter-latin.woff2\" as=\"font\" type=\"font/woff2\" crossorigin />", html);
            }

            foreach (var file in new[] { "inter-latin.woff2", "inter-latin-ext.woff2" })
            {
                var response = await client.GetAsync("/fonts/" + file);
                var bytes = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("font/woff2", response.Content.Headers.ContentType!.MediaType);
                Assert.True(bytes.Length > 10_000, $"{file} is only {bytes.Length} bytes");
                //every woff2 file starts with these four letters
                Assert.Equal("wOF2", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            }

            var fontCss = await client.GetStringAsync("/css/fonts.css");
            Assert.Contains("font-family: \"Inter\"", fontCss);
            Assert.Contains("font-weight: 400 700", fontCss);
            Assert.Contains("font-display: swap", fontCss);
            Assert.Contains("inter-latin.woff2", fontCss);
            Assert.Contains("inter-latin-ext.woff2", fontCss);
            Assert.Equal(2, Regex.Matches(fontCss, "unicode-range:").Count);
            //the stylesheets that ask for Inter still do, and fall back to the system fonts if it is somehow missing
            foreach (var sheet in new[] { "/css/app.css", "/css/login.css" })
                Assert.Contains("font-family: \"Inter\", -apple-system", await client.GetStringAsync(sheet));
        }

        [Fact]
        public async Task TheFontLicense_ShipsWithTheFont()
        {
            var license = await _factory.NewClient().GetStringAsync("/fonts/Inter-LICENSE.txt");

            Assert.Contains("SIL OPEN FONT LICENSE", license.ToUpperInvariant());
            Assert.Contains("Inter Project Authors", license);
        }

        // ---------- the policy stays strict ----------

        [Fact]
        public async Task TheContentSecurityPolicy_NamesNoOtherSite()
        {
            var response = await _factory.NewClient().GetAsync("/Account/Login");
            var csp = string.Join(" ", response.Headers.GetValues("Content-Security-Policy"));

            //loosening the policy to allow another site is not the way to fix a page that depends on one: host the file here instead
            Assert.DoesNotContain("http", csp);
            Assert.Contains("style-src 'self';", csp);
            Assert.Contains("font-src 'self';", csp);
            Assert.Contains("script-src 'self';", csp);
        }
    }
}
