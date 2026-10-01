using System.Net;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the dashboard report page: login required, own hours for developers, a person picker for admin and billing, and safe output
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
        public async Task ReportPage_ShowsADevelopersOwnHours_WithoutAPersonPicker()
        {
            var dev = await SignedInAsync();
            var other = await _factory.CreateLinkedUserAsync();
            var acme = "Acme " + Tag();
            await _factory.AddEntryAsync(dev.UserId, acme, "Web", Coding, Day.AddHours(9), 90);
            await _factory.AddEntryAsync(other.AppUserId, acme, "Web", Coding, Day.AddHours(9), 600);

            var html = await dev.Browser.GetStringAsync(Page(Day, Day));

            Assert.Contains($"{acme} / Web", html);
            Assert.Contains("1.50 hours", html);
            Assert.DoesNotContain("Everyone", html);
            Assert.Contains("Download timesheet", html);
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
        public async Task ReportPage_ADeveloperCannotPickSomeoneElse()
        {
            var dev = await SignedInAsync();
            var other = await _factory.CreateLinkedUserAsync();
            await _factory.AddEntryAsync(other.AppUserId, "Acme " + Tag(), "Web", Coding, Day.AddHours(9), 60, "not yours");

            var page = await (await dev.Browser.GetAsync(Page(Day, Day, $"&userId={other.AppUserId}"))).Content.ReadAsStringAsync();
            var export = await dev.Browser.GetAsync($"/Report/Export?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}&userId={other.AppUserId}");

            Assert.Contains("only view your own hours", page);
            Assert.DoesNotContain("not yours", page);
            Assert.NotEqual(HttpStatusCode.OK, export.StatusCode);
        }

        [Theory]
        [InlineData("Admin")]
        [InlineData("Billing")]
        public async Task ReportPage_PrivilegedUsersCanPickAPersonAndDownloadTheirTimesheet(string role)
        {
            var viewer = await SignedInAsync(role);
            var dev = await _factory.CreateLinkedUserAsync();
            await _factory.AddEntryAsync(dev.AppUserId, "Acme " + Tag(), "Web", Coding, Day.AddHours(9), 60, "devs work");

            var everyone = await viewer.Browser.GetStringAsync(Page(Day, Day));
            var one = await viewer.Browser.GetStringAsync(Page(Day, Day, $"&userId={dev.AppUserId}"));
            var export = await viewer.Browser.GetAsync($"/Report/Export?from={Day:yyyy-MM-dd}&to={Day:yyyy-MM-dd}&userId={dev.AppUserId}");

            Assert.Contains("Everyone", everyone);
            Assert.Contains(dev.Email, everyone);
            Assert.Contains("Pick a person to download", everyone);
            Assert.Contains("Download timesheet", one);
            Assert.Contains("devs work", await export.Content.ReadAsStringAsync());
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
        public async Task ReportPage_DefaultsToTheMonthSoFar()
        {
            var dev = await SignedInAsync();

            var response = await dev.Browser.GetAsync("/Report");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"value=\"{new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1):yyyy-MM-dd}\"", await response.Content.ReadAsStringAsync());
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
