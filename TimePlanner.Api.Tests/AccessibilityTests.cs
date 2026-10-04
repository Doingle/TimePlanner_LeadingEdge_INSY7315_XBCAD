using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the accessibility page (text size, bold and italic) and that the choice, kept in a cookie in the browser, is drawn onto every page
    [Collection("Api")]
    public class AccessibilityTests
    {
        private readonly ApiFactory _factory;

        public AccessibilityTests(ApiFactory factory) => _factory = factory;

        private async Task<HttpClient> SignedInAsync()
        {
            var user = await _factory.CreateLinkedUserAsync();
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, user.Email, user.Password);
            return browser;
        }

        //a page as the browser asks for it, with the saved choice the page's script would have written
        private static async Task<string> PageAsync(HttpClient browser, string url, string? choice)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (choice != null)
                request.Headers.TryAddWithoutValidation("Cookie", $"tp_text={choice}");
            var response = await browser.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }

        //the page's opening <html> tag, where the choice is written
        private static string HtmlTag(string page) => Regex.Match(page, "<html[^>]*>").Value;

        [Fact]
        public async Task TheAccessibilityPage_IsInTheAccountMenu_AndNeedsSignIn()
        {
            var anonymous = await _factory.NewClient().GetAsync("/Settings/Accessibility");
            var browser = await SignedInAsync();

            var home = await PageAsync(browser, "/", null);
            var page = await PageAsync(browser, "/Settings/Accessibility", null);

            Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
            Assert.Contains("/Account/Login", anonymous.Headers.Location!.OriginalString);
            Assert.Contains("href=\"/Settings/Accessibility\"", home);
            Assert.Contains("aria-current=\"page\" href=\"/Settings/Accessibility\"", page);
            //nothing chosen yet: the normal size, no bold or italic
            Assert.Equal("<html lang=\"en\" data-text-size=\"default\" data-bold=\"off\" data-italic=\"off\">", HtmlTag(page));
            Assert.Contains("value=\"default\" checked=\"checked\"", page);
            Assert.Contains("aria-describedby=\"ax-bold-hint\" />", page);
            Assert.Contains("aria-describedby=\"ax-italic-hint\" />", page);
            Assert.Contains("/js/accessibility.js", page);
        }

        [Fact]
        public async Task ASavedChoice_IsDrawnOntoEveryPage_AndShownOnTheAccessibilityPage()
        {
            var browser = await SignedInAsync();
            const string html = "<html lang=\"en\" data-text-size=\"larger\" data-bold=\"on\" data-italic=\"on\">";

            foreach (var url in new[] { "/", "/Timesheet", "/Report?view=week", "/Settings", "/Home/Privacy" })
                Assert.Equal(html, HtmlTag(await PageAsync(browser, url, "larger.bold.italic")));
            //the sign in page too, so the choice is there before anyone signs in again
            Assert.Equal(html, HtmlTag(await PageAsync(_factory.NewClient(), "/Account/Login", "larger.bold.italic")));

            var page = await PageAsync(browser, "/Settings/Accessibility", "larger.bold.italic");
            Assert.Contains("value=\"larger\" checked=\"checked\"", page);
            Assert.Contains("aria-describedby=\"ax-bold-hint\" checked=\"checked\"", page);
            Assert.Contains("aria-describedby=\"ax-italic-hint\" checked=\"checked\"", page);
        }

        [Theory]
        [InlineData("largest.italic", "<html lang=\"en\" data-text-size=\"largest\" data-bold=\"off\" data-italic=\"on\">")]
        [InlineData("default.bold", "<html lang=\"en\" data-text-size=\"default\" data-bold=\"on\" data-italic=\"off\">")]
        [InlineData("huge", "<html lang=\"en\" data-text-size=\"default\" data-bold=\"off\" data-italic=\"off\">")]
        [InlineData("large\"><script>alert(1)</script>", "<html lang=\"en\" data-text-size=\"default\" data-bold=\"off\" data-italic=\"off\">")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.bold", "<html lang=\"en\" data-text-size=\"default\" data-bold=\"off\" data-italic=\"off\">")]
        public async Task OnlyTheKnownChoices_ReachThePage(string choice, string expected)
        {
            var browser = await SignedInAsync();

            var page = await PageAsync(browser, "/", choice);

            Assert.Equal(expected, HtmlTag(page));
            Assert.DoesNotContain("<script>alert(1)", page);
        }

        [Fact]
        public async Task SomeoneOnATemporaryPassword_CanStillMakeTextEasierToRead()
        {
            var admin = await _factory.LoginClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
            var email = $"reader-{Guid.NewGuid():N}@test.local";
            var created = await (await admin.PostAsJsonAsync("/api/v1/users", new { name = "Temporary Reader", email, role = "Developer" })).Content.ReadFromJsonAsync<JsonElement>();
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, email, created.GetProperty("temporaryPassword").GetString()!);

            var page = await browser.GetAsync("/Settings/Accessibility");
            var home = await browser.GetAsync("/");

            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("href=\"/Settings/Accessibility\"", await page.Content.ReadAsStringAsync());
            //everything else still waits for their own password
            Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        }
    }
}
