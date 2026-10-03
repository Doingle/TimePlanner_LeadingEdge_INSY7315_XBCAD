using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Security;
using TimePlanner.Dashboard.Services.Users;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers account administration: who may do it, what creating, resetting and deactivating really change, and that a deactivated or reset account
    //is shut out at once rather than when its session or token happens to expire
    [Collection("Api")]
    public class UserAdminTests
    {
        private const string Chosen = "Chosen-Password-77!";

        private readonly ApiFactory _factory;

        public UserAdminTests(ApiFactory factory) => _factory = factory;

        private Task<HttpClient> AdminAsync() => _factory.LoginClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        private static string NewEmail(string prefix = "person") => $"{prefix}-{Guid.NewGuid():N}@test.local";

        private record Created(string Id, string Email, string TemporaryPassword);

        private static async Task<Created> CreateAsync(HttpClient admin, string role = "Developer", string? email = null, string name = "Test Person")
        {
            email ??= NewEmail();
            var response = await admin.PostAsJsonAsync("/api/v1/users", new { name, email, role });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            return new Created(body.GetProperty("user").GetProperty("id").GetString()!, email, body.GetProperty("temporaryPassword").GetString()!);
        }

        //a person who has been through the first sign in: temporary password replaced, ready to use
        private async Task<Created> ReadyAsync(HttpClient admin, string role = "Developer")
        {
            var created = await CreateAsync(admin, role);
            await _factory.SetPasswordAsync(created.Email, Chosen);
            return created with { TemporaryPassword = Chosen };
        }

        private static Task<HttpResponseMessage> ApiLogin(HttpClient client, string email, string password) =>
            client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

        private async Task<HttpClient> BrowserAsync(string email, string password)
        {
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, email, password);
            return browser;
        }

        private static async Task<string> TokenAsync(HttpClient browser, string url)
        {
            var html = await browser.GetStringAsync(url);
            return Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        }

        private static async Task<HttpResponseMessage> PostFormAsync(HttpClient browser, string pageForToken, string url, Dictionary<string, string> fields)
        {
            fields["__RequestVerificationToken"] = await TokenAsync(browser, pageForToken);
            return await browser.PostAsync(url, new FormUrlEncodedContent(fields));
        }

        private static async Task<JsonElement[]> ListAsync(HttpClient admin) =>
            (await admin.GetFromJsonAsync<JsonElement[]>("/api/v1/users"))!;

        // ---------- who may administer ----------

        [Fact]
        public async Task OnlyAdminsMayAdministerAccounts()
        {
            var admin = await AdminAsync();
            var dev = await ReadyAsync(admin);
            var developer = await _factory.LoginClientAsync(dev.Email, dev.TemporaryPassword);
            var other = await CreateAsync(admin);

            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.NewClient().GetAsync("/api/v1/users")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await developer.GetAsync("/api/v1/users")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await developer.PostAsJsonAsync("/api/v1/users", new { name = "X", email = NewEmail(), role = "Admin" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await developer.PostAsync($"/api/v1/users/{other.Id}/reset-password", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await developer.PostAsync($"/api/v1/users/{other.Id}/deactivate", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await developer.PostAsync($"/api/v1/users/{other.Id}/reactivate", null)).StatusCode);
        }

        [Fact]
        public async Task TheAdminPages_AreForAdminsOnly()
        {
            var admin = await AdminAsync();
            var dev = await ReadyAsync(admin);
            var developerBrowser = await BrowserAsync(dev.Email, dev.TemporaryPassword);
            var adminBrowser = await BrowserAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            Assert.Equal(HttpStatusCode.Redirect, (await _factory.NewClient().GetAsync("/Users")).StatusCode);
            var denied = await developerBrowser.GetAsync("/Users");
            Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
            Assert.Contains("AccessDenied", denied.Headers.Location!.OriginalString);
            Assert.Equal(HttpStatusCode.OK, (await adminBrowser.GetAsync("/Users")).StatusCode);
        }

        // ---------- listing ----------

        [Fact]
        public async Task TheList_ShowsEveryoneWithTheirRoleStatusAndLastSignIn()
        {
            var admin = await AdminAsync();
            var created = await CreateAsync(admin, "Developer", name: "Dana Developer");
            await _factory.SetPasswordAsync(created.Email, Chosen);
            await ApiLogin(_factory.NewClient(), created.Email, Chosen);

            var list = await ListAsync(admin);

            var me = list.Single(u => u.GetProperty("email").GetString() == ApiFactory.AdminEmail);
            Assert.Equal("Admin", me.GetProperty("role").GetString());
            Assert.True(me.GetProperty("isActive").GetBoolean());
            var dana = list.Single(u => u.GetProperty("email").GetString() == created.Email);
            Assert.Equal("Dana Developer", dana.GetProperty("name").GetString());
            Assert.Equal("Developer", dana.GetProperty("role").GetString());
            Assert.True(dana.GetProperty("appUserId").GetInt32() > 0);
            Assert.NotEqual(JsonValueKind.Null, dana.GetProperty("lastLoginUtc").ValueKind);
        }

        // ---------- creating ----------

        [Fact]
        public async Task Creating_MakesTheProfileAndTheLogin_WithATemporaryPasswordShownOnce()
        {
            var admin = await AdminAsync();

            var created = await CreateAsync(admin, "Admin", name: "Alex Admin");

            Assert.Equal(PasswordGenerator.Length, created.TemporaryPassword.Length);
            var row = (await ListAsync(admin)).Single(u => u.GetProperty("id").GetString() == created.Id);
            Assert.True(row.GetProperty("mustChangePassword").GetBoolean());
            Assert.Equal("Admin", row.GetProperty("role").GetString());
            Assert.Equal("Alex Admin", row.GetProperty("name").GetString());
            //the password cannot be fetched again: no endpoint returns it
            Assert.DoesNotContain(created.TemporaryPassword, await admin.GetStringAsync("/api/v1/users"));
        }

        [Theory]
        [InlineData("", "valid@test.local", "Developer", "Name")]
        [InlineData("   ", "valid@test.local", "Developer", "Name")]
        [InlineData("Ok", "not-an-email", "Developer", "mail")]
        [InlineData("Ok", "", "Developer", "mail")]
        [InlineData("Ok", "valid@test.local", "Billing", "Role must be")]
        [InlineData("Ok", "valid@test.local", "1", "Role must be")]
        [InlineData("Ok", "valid@test.local", "", "Role")]
        public async Task Creating_RejectsInvalidDetails(string name, string email, string role, string expected)
        {
            var admin = await AdminAsync();

            var response = await admin.PostAsJsonAsync("/api/v1/users", new { name, email, role });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(expected, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Creating_RejectsATooLongName_AndADuplicateEmailInAnyCase()
        {
            var admin = await AdminAsync();
            var existing = await CreateAsync(admin);

            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/users", new { name = new string('n', 101), email = NewEmail(), role = "Developer" })).StatusCode);
            var duplicate = await admin.PostAsJsonAsync("/api/v1/users", new { name = "Again", email = existing.Email.ToUpperInvariant(), role = "Developer" });

            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
            Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync());
        }

        [Fact]
        public void TheTemporaryPasswords_AlwaysMeetThePolicy_AndNeverRepeat()
        {
            var passwords = Enumerable.Range(0, 500).Select(_ => PasswordGenerator.Create()).ToList();

            Assert.All(passwords, p =>
            {
                Assert.Equal(PasswordGenerator.Length, p.Length);
                Assert.Matches("[a-z]", p);
                Assert.Matches("[A-Z]", p);
                Assert.Matches("[0-9]", p);
                Assert.Matches("[^a-zA-Z0-9]", p);
                Assert.DoesNotMatch("[0OoIl1]", p);
            });
            Assert.Equal(passwords.Count, passwords.Distinct().Count());
        }

        [Fact]
        public async Task ANewPerson_MustChooseAPassword_BeforeDoingAnythingElse()
        {
            var admin = await AdminAsync();
            var created = await CreateAsync(admin);

            // the api refuses to hand out a token for a temporary password
            var api = await ApiLogin(_factory.NewClient(), created.Email, created.TemporaryPassword);
            Assert.Equal(HttpStatusCode.Forbidden, api.StatusCode);
            Assert.Contains("new password", await api.Content.ReadAsStringAsync());

            // the website signs them in but sends them to settings, from everywhere
            var browser = _factory.NewClient();
            var login = await ApiFactory.PostLoginAsync(browser, created.Email, created.TemporaryPassword);
            Assert.Equal("/Settings", login.Headers.Location!.OriginalString);
            Assert.Equal("/Settings", (await browser.GetAsync("/")).Headers.Location!.OriginalString);
            Assert.Equal("/Settings", (await browser.GetAsync("/Report")).Headers.Location!.OriginalString);
            Assert.Equal("/Settings", (await browser.GetAsync("/CsvUpload")).Headers.Location!.OriginalString);
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Settings")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/health")).StatusCode);

            // choosing a password frees them, in the same session
            var changed = await PostFormAsync(browser, "/Settings", "/Settings/Password",
                new() { ["currentPassword"] = created.TemporaryPassword, ["newPassword"] = Chosen, ["confirmPassword"] = Chosen });
            Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ApiLogin(_factory.NewClient(), created.Email, Chosen)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await ApiLogin(_factory.NewClient(), created.Email, created.TemporaryPassword)).StatusCode);
        }

        [Fact]
        public async Task TheSettingsPage_OnATemporaryPassword_ShowsOnlyThatTask_WithoutTheTabs()
        {
            var admin = await AdminAsync();
            var created = await CreateAsync(admin);
            var browser = await BrowserAsync(created.Email, created.TemporaryPassword);

            var before = await browser.GetStringAsync("/Settings");
            await PostFormAsync(browser, "/Settings", "/Settings/Password",
                new() { ["currentPassword"] = created.TemporaryPassword, ["newPassword"] = Chosen, ["confirmPassword"] = Chosen });
            var after = await browser.GetStringAsync("/Settings");

            Assert.Contains("Choose your own password", before);
            Assert.Contains($"You're signed in as {created.Email} with a temporary password from your admin.", WebUtility.HtmlDecode(before));
            Assert.Contains(">Temporary password</label>", before);
            Assert.Contains(">Set password</button>", before);
            //no name to change and no tabs to wander off to until the task is done, signing out still works
            Assert.DoesNotContain("Your name", before);
            Assert.DoesNotContain("class=\"app-nav\"", before);
            Assert.Contains("/Account/Logout", before);
            //once chosen, the page is the usual one again
            Assert.Contains("class=\"app-nav\"", after);
            Assert.Contains("Your name", after);
            Assert.DoesNotContain("temporary password", after);
        }

        [Fact]
        public async Task TheCreatePage_ShowsTheTemporaryPasswordOnce_AndNeverAgain()
        {
            var browser = await BrowserAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
            var email = NewEmail();

            var response = await PostFormAsync(browser, "/Users", "/Users/Create", new() { ["name"] = "Page Person", ["email"] = email, ["role"] = "Developer" });
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            //the page shows the password HTML-encoded (a & becomes &amp;), so decode it the way a browser would before using it
            var password = WebUtility.HtmlDecode(Regex.Match(html, "<code class=\"tm-temp__code\">([^<]+)</code>").Groups[1].Value);
            Assert.Equal(PasswordGenerator.Length, password.Length);
            // the password on the page is the real one: the account exists and is waiting for its first password change
            Assert.Equal(HttpStatusCode.Forbidden, (await ApiLogin(_factory.NewClient(), email, password)).StatusCode);
            Assert.DoesNotContain(password, await browser.GetStringAsync("/Users"));
        }

        [Fact]
        public async Task TheCreatePage_ExplainsAMistake_WithoutAnyPassword()
        {
            var browser = await BrowserAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            var html = await (await PostFormAsync(browser, "/Users", "/Users/Create", new() { ["name"] = "X", ["email"] = "bad", ["role"] = "Developer" })).Content.ReadAsStringAsync();

            Assert.Contains("valid email", html);
            //no password block (the list may still tag other people as having a temporary password)
            Assert.DoesNotContain("Temporary password for", html);
            Assert.DoesNotContain("<code", html);
        }

        // ---------- resetting a password ----------

        [Fact]
        public async Task Resetting_ReplacesThePassword_ClearsALockout_AndShutsOldAccessOut()
        {
            var admin = await AdminAsync();
            var person = await ReadyAsync(admin);
            var oldToken = await _factory.LoginClientAsync(person.Email, person.TemporaryPassword);
            var oldBrowser = await BrowserAsync(person.Email, person.TemporaryPassword);
            var plain = _factory.NewClient();
            for (var i = 0; i < 5; i++)
                await ApiLogin(plain, person.Email, "Wrong-Pass-123!");
            Assert.True(await _factory.IsLockedOutAsync(person.Email));

            var reset = await admin.PostAsync($"/api/v1/users/{person.Id}/reset-password", null);
            var fresh = (await reset.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("temporaryPassword").GetString()!;

            Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
            Assert.NotEqual(person.TemporaryPassword, fresh);
            Assert.False(await _factory.IsLockedOutAsync(person.Email));
            Assert.Equal(HttpStatusCode.Unauthorized, (await ApiLogin(plain, person.Email, person.TemporaryPassword)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ApiLogin(plain, person.Email, fresh)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await oldToken.GetAsync("/api/v1/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await oldBrowser.GetAsync("/Report")).StatusCode);
            Assert.True((await ListAsync(admin)).Single(u => u.GetProperty("id").GetString() == person.Id).GetProperty("mustChangePassword").GetBoolean());
        }

        [Fact]
        public async Task Resetting_AUnknownAccount_IsNotFound()
        {
            var admin = await AdminAsync();

            Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/v1/users/does-not-exist/reset-password", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/v1/users/does-not-exist/deactivate", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/v1/users/does-not-exist/reactivate", null)).StatusCode);
        }

        // ---------- deactivating ----------

        [Fact]
        public async Task Deactivating_ShutsEveryWayInAtOnce()
        {
            var admin = await AdminAsync();
            var person = await ReadyAsync(admin);
            var token = await _factory.LoginClientAsync(person.Email, person.TemporaryPassword);
            var browser = await BrowserAsync(person.Email, person.TemporaryPassword);
            Assert.Equal(HttpStatusCode.OK, (await token.GetAsync("/api/v1/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Report")).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/users/{person.Id}/deactivate", null)).StatusCode);

            //a session and a token that were already open stop working immediately
            Assert.Equal(HttpStatusCode.Unauthorized, (await token.GetAsync("/api/v1/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Report")).StatusCode);
            //and the correct password no longer signs in, with the same message a wrong one gets
            var api = await ApiLogin(_factory.NewClient(), person.Email, person.TemporaryPassword);
            Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
            Assert.Contains("Invalid email or password", await api.Content.ReadAsStringAsync());
            var site = await (await ApiFactory.PostLoginAsync(_factory.NewClient(), person.Email, person.TemporaryPassword)).Content.ReadAsStringAsync();
            Assert.Contains("Invalid email or password", site);
            Assert.False((await ListAsync(admin)).Single(u => u.GetProperty("id").GetString() == person.Id).GetProperty("isActive").GetBoolean());
        }

        [Fact]
        public async Task Reactivating_LetsThemBackIn_AndKeepsTheirHistory()
        {
            var admin = await AdminAsync();
            var person = await ReadyAsync(admin);
            var profile = (await ListAsync(admin)).Single(u => u.GetProperty("id").GetString() == person.Id).GetProperty("appUserId").GetInt32();
            await admin.PostAsync($"/api/v1/users/{person.Id}/deactivate", null);

            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/users/{person.Id}/reactivate", null)).StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await ApiLogin(_factory.NewClient(), person.Email, person.TemporaryPassword)).StatusCode);
            Assert.Equal(profile, (await ListAsync(admin)).Single(u => u.GetProperty("id").GetString() == person.Id).GetProperty("appUserId").GetInt32());
        }

        //-----------------------------
        //deactivating stops sign in, it must not erase or hide the hours the person logged: the company still needs them for reports and billing
        [Fact]
        public async Task Deactivating_KeepsThePersonsHoursInReports()
        {
            var admin = await AdminAsync();
            var person = await ReadyAsync(admin);
            var profile = (await ListAsync(admin)).Single(u => u.GetProperty("id").GetString() == person.Id).GetProperty("appUserId").GetInt32();
            var day = DateTime.Today.AddDays(-4);
            await _factory.AddEntryAsync(profile, "Acme " + Guid.NewGuid().ToString("N")[..8], "Web", 2, day.AddHours(9), 90);
            await admin.PostAsync($"/api/v1/users/{person.Id}/deactivate", null);

            var report = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/reports/hours?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}&userId={profile}");

            Assert.Equal(1.5, report.GetProperty("totalHours").GetDouble());
        }

        [Fact]
        public async Task Deactivating_IsRepeatSafe()
        {
            var admin = await AdminAsync();
            var person = await ReadyAsync(admin);

            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/users/{person.Id}/deactivate", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/users/{person.Id}/deactivate", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/users/{person.Id}/reactivate", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/users/{person.Id}/reactivate", null)).StatusCode);
        }

        [Fact]
        public async Task AnAdmin_CannotDeactivateThemselves()
        {
            var admin = await AdminAsync();
            var self = (await ListAsync(admin)).Single(u => u.GetProperty("email").GetString() == ApiFactory.AdminEmail).GetProperty("id").GetString();

            var response = await admin.PostAsync($"/api/v1/users/{self}/deactivate", null);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("your own account", await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/auth/me")).StatusCode);
        }

        [Fact]
        public async Task TheLastActiveAdmin_CanNeverBeDeactivated()
        {
            using var factory = new ApiFactory();
            factory.NewClient();
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<UserAdminService>();
            var onlyAdmin = (await service.ListAsync()).Single(u => u.Role == "Admin");

            var result = await service.SetActiveAsync(onlyAdmin.Id, active: false);

            Assert.False(result.Success);
            Assert.Contains("at least one active administrator", result.Error);
        }

        [Fact]
        public async Task ASecondAdmin_CanBeDeactivated_WhileAnotherStaysActive()
        {
            var admin = await AdminAsync();
            var second = await CreateAsync(admin, "Admin");

            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/users/{second.Id}/deactivate", null)).StatusCode);
        }

        [Fact]
        public async Task TheUsersPage_CanResetDeactivateAndReactivate()
        {
            var admin = await AdminAsync();
            var person = await ReadyAsync(admin);
            var browser = await BrowserAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            var reset = await (await PostFormAsync(browser, "/Users", "/Users/ResetPassword", new() { ["id"] = person.Id })).Content.ReadAsStringAsync();
            var deactivate = await PostFormAsync(browser, "/Users", "/Users/Deactivate", new() { ["id"] = person.Id });
            var afterDeactivate = await browser.GetStringAsync(deactivate.Headers.Location!.OriginalString);
            var reactivate = await PostFormAsync(browser, "/Users", "/Users/Reactivate", new() { ["id"] = person.Id });
            var afterReactivate = await browser.GetStringAsync(reactivate.Headers.Location!.OriginalString);

            Assert.Contains("Password reset for " + person.Email, reset);
            Assert.Equal(HttpStatusCode.Redirect, deactivate.StatusCode);
            Assert.Contains("Account deactivated", afterDeactivate);
            Assert.Contains("Deactivated", afterDeactivate);
            Assert.Contains("Account reactivated", afterReactivate);
        }

        [Fact]
        public async Task TheUsersPage_RefusesAPostWithoutTheAntiForgeryToken()
        {
            var browser = await BrowserAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            var response = await browser.PostAsync("/Users/Create", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["name"] = "X", ["email"] = NewEmail(), ["role"] = "Developer"
            }));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task TheUsersPage_EncodesNamesThatContainHtml()
        {
            var admin = await AdminAsync();
            await CreateAsync(admin, name: "<script>alert(1)</script>");
            var browser = await BrowserAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            var html = await browser.GetStringAsync("/Users");

            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        }

        [Fact]
        public async Task TheUsersPage_ShowsEachPersonsState_AndTheButtonsThatApply()
        {
            var admin = await AdminAsync();
            var fresh = await CreateAsync(admin, name: "Fresh Person");
            var gone = await ReadyAsync(admin);
            await admin.PostAsync($"/api/v1/users/{gone.Id}/deactivate", null);
            var browser = await BrowserAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            var html = await browser.GetStringAsync("/Users");
            //one person's row of the table
            string Row(string email) => Regex.Match(html, $"<tr class=\"tm-users__row[^\"]*\">(?:(?!</tr>).)*{Regex.Escape(email)}(?:(?!</tr>).)*</tr>", RegexOptions.Singleline).Value;

            //the Team tab and the Users tab are the current ones, the other tabs open the current week
            Assert.Contains("aria-current=\"page\" href=\"/Admin\">Team", html);
            Assert.Contains("aria-current=\"page\" href=\"/Users\">Users", html);
            Assert.Contains("href=\"/Admin/Submissions\">Submissions", html);
            Assert.Matches(@"\d+ active, \d+ deactivated", html);
            //every form posts to this page's own actions, never to the api's users endpoints that share the controller name
            Assert.Contains("action=\"/Users/Create\"", html);
            Assert.Contains("action=\"/Users/ResetPassword\"", html);
            Assert.Contains("action=\"/Users/Deactivate\"", html);
            Assert.Contains("action=\"/Users/Reactivate\"", html);
            Assert.DoesNotContain("action=\"/api/", html);
            //a new account is waiting for its first sign in
            var freshRow = Row(fresh.Email);
            Assert.Contains(">Temporary password</span>", freshRow);
            Assert.Contains(">Never</td>", freshRow);
            Assert.Contains("aria-label=\"Reset password for Fresh Person\"", freshRow);
            Assert.Contains("aria-label=\"Deactivate Fresh Person\"", freshRow);
            //a deactivated person can be reactivated or given a new password, not deactivated again
            var goneRow = Row(gone.Email);
            Assert.Contains("tm-users__row--off", goneRow);
            Assert.Contains(">Deactivated</span>", goneRow);
            Assert.Contains(">Reactivate</button>", goneRow);
            Assert.Contains(">Reset password</button>", goneRow);
            Assert.DoesNotContain(">Deactivate</button>", goneRow);
            //the admin's own row says so and has no deactivate button, the server would refuse it anyway
            var mine = Row(ApiFactory.AdminEmail);
            Assert.Contains("(you)", mine);
            Assert.Contains(" UTC</td>", mine);
            Assert.DoesNotContain(">Deactivate</button>", mine);
        }

        // ---------- everything is audited, and no password is ever written down ----------

        [Fact]
        public async Task EveryAdminAction_IsAudited_WithoutAnyPassword()
        {
            using var factory = new ApiFactory();
            var admin = await factory.LoginClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
            var email = NewEmail();
            var created = await (await admin.PostAsJsonAsync("/api/v1/users", new { name = "Audited", email, role = "Developer" })).Content.ReadFromJsonAsync<JsonElement>();
            var id = created.GetProperty("user").GetProperty("id").GetString();
            var temporary = created.GetProperty("temporaryPassword").GetString()!;
            var reset = await (await admin.PostAsync($"/api/v1/users/{id}/reset-password", null)).Content.ReadFromJsonAsync<JsonElement>();
            await admin.PostAsync($"/api/v1/users/{id}/deactivate", null);
            await admin.PostAsync($"/api/v1/users/{id}/reactivate", null);

            var events = await factory.AuditEventsAsync();
            var actions = events.Where(e => e.Detail != null && e.Detail.Contains(email)).Select(e => e.Action).ToArray();

            Assert.Equal(new[] { AuditActions.UserCreated, AuditActions.PasswordReset, AuditActions.UserDeactivated, AuditActions.UserReactivated }, actions);
            Assert.All(events.Where(e => e.Action is AuditActions.UserCreated or AuditActions.PasswordReset or AuditActions.UserDeactivated or AuditActions.UserReactivated),
                e => Assert.Equal(ApiFactory.AdminEmail, e.Email));
            var everything = JsonSerializer.Serialize(events);
            Assert.DoesNotContain(temporary, everything);
            Assert.DoesNotContain(reset.GetProperty("temporaryPassword").GetString()!, everything);
        }

        [Fact]
        public async Task ABlockedSignIn_IsAuditedAsBlocked()
        {
            using var factory = new ApiFactory();
            var admin = await factory.LoginClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
            var created = await (await admin.PostAsJsonAsync("/api/v1/users", new { name = "Blocked", email = NewEmail(), role = "Developer" })).Content.ReadFromJsonAsync<JsonElement>();
            var id = created.GetProperty("user").GetProperty("id").GetString();
            var email = created.GetProperty("user").GetProperty("email").GetString()!;
            await admin.PostAsync($"/api/v1/users/{id}/deactivate", null);

            await ApiLogin(factory.NewClient(), email, created.GetProperty("temporaryPassword").GetString()!);

            Assert.Contains(await factory.AuditEventsAsync(), e => e.Action == AuditActions.LoginBlocked && e.Email == email);
        }
    }
}
