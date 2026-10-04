using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers a person's own account: their display name and their password, over the api and on the settings page
    [Collection("Api")]
    public class AccountSettingsTests
    {
        private const string Current = "Current-Password-11!";
        private const string Next = "Brand-New-Password-22!";

        private readonly ApiFactory _factory;

        public AccountSettingsTests(ApiFactory factory) => _factory = factory;

        //a person with a profile, a known password and nothing pending
        private async Task<(string Email, HttpClient Api)> PersonAsync()
        {
            var user = await _factory.CreateLinkedUserAsync();
            await _factory.SetPasswordAsync(user.Email, Current);
            return (user.Email, await _factory.LoginClientAsync(user.Email, Current));
        }

        private static Task<HttpResponseMessage> ChangePassword(HttpClient client, string current, string next) =>
            client.PostAsJsonAsync("/api/v1/profile/password", new { currentPassword = current, newPassword = next });

        private async Task<HttpClient> BrowserAsync(string email, string password)
        {
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, email, password);
            return browser;
        }

        private static async Task<HttpResponseMessage> PostFormAsync(HttpClient browser, string url, Dictionary<string, string> fields)
        {
            var html = await browser.GetStringAsync("/Settings");
            fields["__RequestVerificationToken"] = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            return await browser.PostAsync(url, new FormUrlEncodedContent(fields));
        }

        // ---------- the api ----------

        [Fact]
        public async Task TheProfileEndpoints_RequireAToken()
        {
            var client = _factory.NewClient();

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/profile")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/api/v1/profile/name", new { name = "X" })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await ChangePassword(client, "a", "b")).StatusCode);
        }

        [Fact]
        public async Task TheProfile_ShowsWhoTheyAre()
        {
            var person = await PersonAsync();

            var profile = await person.Api.GetFromJsonAsync<JsonElement>("/api/v1/profile");

            Assert.Equal(person.Email, profile.GetProperty("email").GetString());
            Assert.Equal("Developer", profile.GetProperty("role").GetString());
            Assert.False(profile.GetProperty("mustChangePassword").GetBoolean());
            Assert.Equal(person.Email, profile.GetProperty("name").GetString());
        }

        [Fact]
        public async Task ChangingTheName_UpdatesItEverywhere()
        {
            var person = await PersonAsync();
            var admin = await _factory.LoginClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            var response = await person.Api.PutAsJsonAsync("/api/v1/profile/name", new { name = "  Renamed Person  " });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal("Renamed Person", (await person.Api.GetFromJsonAsync<JsonElement>("/api/v1/profile")).GetProperty("name").GetString());
            var list = (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/users"))!;
            Assert.Equal("Renamed Person", list.Single(u => u.GetProperty("email").GetString() == person.Email).GetProperty("name").GetString());
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("line\r\nbreak")]
        public async Task ChangingTheName_RejectsABlankOrUnsafeName(string name)
        {
            var person = await PersonAsync();

            Assert.Equal(HttpStatusCode.BadRequest, (await person.Api.PutAsJsonAsync("/api/v1/profile/name", new { name })).StatusCode);
        }

        [Fact]
        public async Task ChangingTheName_RejectsATooLongName()
        {
            var person = await PersonAsync();

            Assert.Equal(HttpStatusCode.BadRequest, (await person.Api.PutAsJsonAsync("/api/v1/profile/name", new { name = new string('n', 101) })).StatusCode);
        }

        [Fact]
        public async Task ALoginWithoutAProfile_CannotChangeAName_ButCanChangeAPassword()
        {
            var (email, password) = await _factory.CreateUserAsync();
            var client = await _factory.LoginClientAsync(email, password);

            var name = await client.PutAsJsonAsync("/api/v1/profile/name", new { name = "Someone" });

            Assert.Equal(HttpStatusCode.BadRequest, name.StatusCode);
            Assert.Contains("not linked", await name.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.NoContent, (await ChangePassword(client, password, Next)).StatusCode);
        }

        // ---------- changing the password ----------

        [Fact]
        public async Task ChangingThePassword_NeedsTheCurrentOne_AndEndsEveryOtherSession()
        {
            var person = await PersonAsync();
            var otherToken = await _factory.LoginClientAsync(person.Email, Current);
            var otherBrowser = await BrowserAsync(person.Email, Current);

            var response = await ChangePassword(person.Api, Current, Next);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _factory.NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = person.Email, password = Next })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = person.Email, password = Current })).StatusCode);
            //tokens and sessions made before the change no longer work
            Assert.Equal(HttpStatusCode.Unauthorized, (await person.Api.GetAsync("/api/v1/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await otherToken.GetAsync("/api/v1/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await otherBrowser.GetAsync("/Report")).StatusCode);
        }

        [Fact]
        public async Task ChangingThePassword_RejectsAWrongCurrentPassword()
        {
            var person = await PersonAsync();

            var response = await ChangePassword(person.Api, "Wrong-Pass-123!", Next);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("current password is not correct", await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await _factory.NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = person.Email, password = Current })).StatusCode);
        }

        [Fact]
        public async Task WrongCurrentPasswords_CountTowardsALockout_SoAStolenSessionCannotGuessThePassword()
        {
            var person = await PersonAsync();

            for (var i = 0; i < 5; i++)
                await ChangePassword(person.Api, "Wrong-Pass-123!", Next);
            var afterLockout = await ChangePassword(person.Api, Current, Next);

            Assert.True(await _factory.IsLockedOutAsync(person.Email));
            Assert.Equal(HttpStatusCode.BadRequest, afterLockout.StatusCode);
            Assert.Contains("Too many wrong attempts", await afterLockout.Content.ReadAsStringAsync());
        }

        [Theory]
        [InlineData("Current-Password-11!", "different from your current")]
        [InlineData("short1!", "at least")]
        [InlineData("alllowercase-no-digit!", "uppercase")]
        [InlineData("NoSymbolsHere12345", "non alphanumeric")]
        public async Task ChangingThePassword_EnforcesThePasswordRules(string next, string expected)
        {
            var person = await PersonAsync();

            var response = await ChangePassword(person.Api, Current, next);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(expected, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task ChangingThePassword_IsRateLimitedLikeSigningIn()
        {
            using var factory = new LimitedFactory();
            var user = await factory.CreateLinkedUserAsync();
            var client = await factory.LoginClientAsync(user.Email, user.Password);

            var statuses = new List<HttpStatusCode>();
            for (var i = 0; i < 8; i++)
                statuses.Add((await ChangePassword(client, "Wrong-Pass-123!", Next)).StatusCode);

            Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
            Assert.True(statuses.Count(s => s == HttpStatusCode.BadRequest) <= 4);
        }

        // ---------- the settings page ----------

        [Fact]
        public async Task TheSettingsPage_RequiresALogin()
        {
            Assert.Equal(HttpStatusCode.Redirect, (await _factory.NewClient().GetAsync("/Settings")).StatusCode);
        }

        [Fact]
        public async Task TheSettingsPage_ShowsTheAccount_AndPointsTrackingSettingsToTheWidget()
        {
            var person = await PersonAsync();
            var browser = await BrowserAsync(person.Email, Current);

            var html = await browser.GetStringAsync("/Settings");

            Assert.Contains(person.Email, html);
            Assert.Contains("Developer", html);
            Assert.Contains("Tracking settings are in the widget", html);
            Assert.DoesNotContain("temporary password", html);
        }

        [Fact]
        public async Task TheSettingsPage_ChangesTheNameAndShowsAMessage()
        {
            var person = await PersonAsync();
            var browser = await BrowserAsync(person.Email, Current);

            var response = await PostFormAsync(browser, "/Settings/Name", new() { ["name"] = "Page Rename" });
            var html = await browser.GetStringAsync(response.Headers.Location!.OriginalString);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("Your name was updated", html);
            Assert.Contains("Page Rename", html);
        }

        [Fact]
        public async Task TheSettingsPage_ExplainsEachPasswordProblem()
        {
            var person = await PersonAsync();
            var browser = await BrowserAsync(person.Email, Current);

            async Task<string> TryAsync(string current, string next, string confirm) =>
                await (await PostFormAsync(browser, "/Settings/Password", new() { ["currentPassword"] = current, ["newPassword"] = next, ["confirmPassword"] = confirm })).Content.ReadAsStringAsync();

            Assert.Contains("do not match", await TryAsync(Current, Next, "Something-Else-33!"));
            Assert.Contains("current password is not correct", await TryAsync("Wrong-Pass-123!", Next, Next));
            Assert.Contains("at least", await TryAsync(Current, "short1!", "short1!"), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task TheSettingsPage_ShowsEachOutcome_NextToTheFormItCameFrom()
        {
            var person = await PersonAsync();
            var browser = await BrowserAsync(person.Email, Current);

            //the name form's half of the page runs from its heading to the password heading
            static string NamePart(string html) => html[html.IndexOf("id=\"st-name-title\"", StringComparison.Ordinal)..html.IndexOf("id=\"st-password-title\"", StringComparison.Ordinal)];
            static string PasswordPart(string html) => html[html.IndexOf("id=\"st-password-title\"", StringComparison.Ordinal)..];

            var badName = await (await PostFormAsync(browser, "/Settings/Name", new() { ["name"] = "   " })).Content.ReadAsStringAsync();
            var badPassword = await (await PostFormAsync(browser, "/Settings/Password", new() { ["currentPassword"] = Current, ["newPassword"] = Next, ["confirmPassword"] = "Other-Password-44!" })).Content.ReadAsStringAsync();
            var renamed = await browser.GetStringAsync((await PostFormAsync(browser, "/Settings/Name", new() { ["name"] = "Outcome Person" })).Headers.Location!.OriginalString);

            Assert.Contains("st-status--error", NamePart(badName));
            Assert.Contains("aria-invalid=\"true\"", NamePart(badName));
            Assert.DoesNotContain("st-status", PasswordPart(badName));
            Assert.Contains("do not match", PasswordPart(badPassword));
            Assert.DoesNotContain("st-status", NamePart(badPassword));
            Assert.Contains("Your name was updated", NamePart(renamed));
            Assert.DoesNotContain("st-status", PasswordPart(renamed));
        }

        [Fact]
        public async Task TheSettingsPage_ChangesThePassword_KeepsThisSessionAndEndsTheOthers()
        {
            var person = await PersonAsync();
            var browser = await BrowserAsync(person.Email, Current);
            var other = await BrowserAsync(person.Email, Current);

            var response = await PostFormAsync(browser, "/Settings/Password", new() { ["currentPassword"] = Current, ["newPassword"] = Next, ["confirmPassword"] = Next });
            var page = await browser.GetStringAsync(response.Headers.Location!.OriginalString);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("Your password was changed", page);
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Report")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await other.GetAsync("/Report")).StatusCode);
        }

        [Fact]
        public async Task TheSettingsForms_RefuseAPostWithoutTheAntiForgeryToken()
        {
            var person = await PersonAsync();
            var browser = await BrowserAsync(person.Email, Current);

            var name = await browser.PostAsync("/Settings/Name", new FormUrlEncodedContent(new Dictionary<string, string> { ["name"] = "Sneaky" }));
            var password = await browser.PostAsync("/Settings/Password", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["currentPassword"] = Current, ["newPassword"] = Next, ["confirmPassword"] = Next
            }));

            Assert.Equal(HttpStatusCode.BadRequest, name.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, password.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _factory.NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = person.Email, password = Current })).StatusCode);
        }

        [Fact]
        public async Task TheSettingsPage_EncodesTheName()
        {
            var person = await PersonAsync();
            await person.Api.PutAsJsonAsync("/api/v1/profile/name", new { name = "\"><script>alert(1)</script>" });
            var browser = await BrowserAsync(person.Email, Current);

            var html = await browser.GetStringAsync("/Settings");

            Assert.DoesNotContain("<script>alert(1)</script>", html);
        }

        // ---------- audit ----------

        [Fact]
        public async Task NameAndPasswordChanges_AreAudited_WithoutAnyPassword()
        {
            using var factory = new ApiFactory();
            var user = await factory.CreateLinkedUserAsync();
            var client = await factory.LoginClientAsync(user.Email, user.Password);

            await client.PutAsJsonAsync("/api/v1/profile/name", new { name = "Audited Rename" });
            await ChangePassword(client, user.Password, Next);

            var events = await factory.AuditEventsAsync();
            Assert.Contains(events, e => e.Action == AuditActions.ProfileUpdated && e.Email == user.Email);
            Assert.Contains(events, e => e.Action == AuditActions.PasswordChanged && e.Email == user.Email);
            var everything = JsonSerializer.Serialize(events);
            Assert.DoesNotContain(user.Password, everything);
            Assert.DoesNotContain(Next, everything);
        }
    }
}
