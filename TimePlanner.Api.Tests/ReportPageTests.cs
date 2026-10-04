using System.Net;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers My Reports: login required, always the person's own hours (the admins' detailed report is tested with the team pages), the download, and safe output
    [Collection("Api")]
    public class ReportPageTests
    {
        private const int Coding = 2;
        private static readonly DateTime Day = DateTime.Today.AddDays(-7);

        private readonly ApiFactory _factory;

        public ReportPageTests(ApiFactory factory) => _factory = factory;

        private static string Tag() => Guid.NewGuid().ToString("N")[..8];
        private static string Page(DateTime from, DateTime to, string? extra = null) =>
            $"/Report?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}{extra}";

        private async Task<(HttpClient Browser, int UserId, string Email)> SignedInAsync(string role = "Developer")
        {
            var user = await _factory.CreateLinkedUserAsync(role);
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, user.Email, user.Password);
            return (browser, user.AppUserId, user.Email);
        }

        [Fact]
        public async Task ReportPage_RequiresALogin()
        {
            var response = await _factory.NewClient().GetAsync("/Report");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        [Fact]
        public async Task MyReports_ShowsOnlyTheirOwnHours_EvenForAnAdmin()
        {
            var dev = await SignedInAsync();
            var admin = await SignedInAsync("Admin");
            var other = await _factory.CreateLinkedUserAsync();
            var acme = "Acme " + Tag();
            await _factory.AddEntryAsync(dev.UserId, acme, "Web", Coding, Day.AddHours(9), 90);
            await _factory.AddEntryAsync(other.AppUserId, acme, "Web", Coding, Day.AddHours(9), 600);

            var html = await dev.Browser.GetStringAsync(Page(Day, Day));
            var adminHtml = await admin.Browser.GetStringAsync(Page(Day, Day));

            Assert.Contains($"{acme} / Web", html);
            Assert.Contains("<span class=\"rp-hero__num\">1:30</span>", html);
            Assert.Contains("Download CSV", html);
            Assert.Contains("<span class=\"rp-hero__num\">0:00</span>", adminHtml);
            //picking a person and grouping the hours is the detailed report, an admin tool on the team's Submissions page
            foreach (var page in new[] { html, adminHtml })
            {
                Assert.DoesNotContain("Everyone", page);
                Assert.DoesNotContain("Detailed report", page);
            }
        }

        [Fact]
        public async Task ReportPage_TheDownloadLinkReturnsTheTimesheet()
        {
            var dev = await SignedInAsync();
            await _factory.AddEntryAsync(dev.UserId, "Acme " + Tag(), "Web", Coding, Day.AddHours(9), 60, "my work");

            var response = await dev.Browser.GetAsync($"/Report/Export?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
            Assert.Contains("my work", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task MyReports_IgnoresAPersonInTheAddress_AndTheExportStaysTheirOwn()
        {
            var dev = await SignedInAsync();
            var other = await _factory.CreateLinkedUserAsync();
            var secret = "Secret " + Tag();
            await _factory.AddEntryAsync(other.AppUserId, secret, "Web", Coding, Day.AddHours(9), 60, "not yours");

            var page = await dev.Browser.GetStringAsync(Page(Day, Day, $"&userId={other.AppUserId}"));
            var export = await dev.Browser.GetAsync($"/Report/Export?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}&userId={other.AppUserId}");

            Assert.DoesNotContain(secret, page);
            Assert.Contains("<span class=\"rp-hero__num\">0:00</span>", page);
            Assert.NotEqual(HttpStatusCode.OK, export.StatusCode);
        }

        [Fact]
        public async Task ReportPage_ExplainsAnInvalidRangeInsteadOfFailing()
        {
            var dev = await SignedInAsync();

            var response = await dev.Browser.GetAsync(Page(Day, Day.AddDays(-3)));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("must not be after", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task MyReports_OpensOnTheCurrentMonth()
        {
            var dev = await SignedInAsync();

            var response = await dev.Browser.GetAsync("/Report");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("value=\"month\" aria-pressed=\"true\"", html);
            Assert.Contains("submitted in ", html);
        }

        [Fact]
        public async Task ReportPage_EncodesNamesThatContainHtml()
        {
            var dev = await SignedInAsync();
            await _factory.AddEntryAsync(dev.UserId, "<script>alert(1)</script> " + Tag(), "Web", Coding, Day.AddHours(9), 60);

            var html = await dev.Browser.GetStringAsync(Page(Day, Day));

            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        }

        [Fact]
        public async Task ReportPage_TellsAnUnlinkedAccountWhy()
        {
            var (email, password) = await _factory.CreateUserAsync();
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, email, password);

            Assert.Contains("not linked to a time tracking profile", await browser.GetStringAsync("/Report"));
        }
    }
}
