using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Services.Users;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers signing in with a second step: a code from an authenticator app, or a recovery code. On the api, on the website and on the settings page,
    //plus turning it on and off, and an admin resetting it for someone who lost their phone
    [Collection("Api")]
    public class TwoFactorTests
    {
        private readonly ApiFactory _factory;

        public TwoFactorTests(ApiFactory factory) => _factory = factory;

        // ---------- what an authenticator app does ----------

        //the six digit code an authenticator app shows (RFC 6238: HMAC-SHA1 over the 30 second counter). SHA-1 is what every authenticator app uses,
        //so the test has to as well. Identity can check a code but cannot make one, and this is only ever run on a throwaway test account
#pragma warning disable CA5350
        private static string Totp(string base32Key)
        {
            var key = Base32(base32Key.Replace(" ", "").ToUpperInvariant());
            var counter = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
            var hash = HMACSHA1.HashData(key, counter);
            var offset = hash[^1] & 0x0F;
            var number = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
            return (number % 1_000_000).ToString("D6");
        }
#pragma warning restore CA5350

        private static byte[] Base32(string text)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var bytes = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (var c in text.TrimEnd('='))
            {
                buffer = (buffer << 5) | alphabet.IndexOf(c);
                bits += 5;
                if (bits >= 8)
                {
                    bits -= 8;
                    bytes.Add((byte)((buffer >> bits) & 0xFF));
                }
            }
            return bytes.ToArray();
        }

        // ---------- helpers ----------

        //a person with two-factor already on, as if they had set it up in their app
        private async Task<(string Email, string Password, string Key, IReadOnlyList<string> RecoveryCodes)> PersonWithTwoFactorAsync(string role = "Developer")
        {
            var person = await _factory.CreateLinkedUserAsync(role);
            using var scope = _factory.Services.CreateScope();
            var account = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(person.Email))!;
            var service = scope.ServiceProvider.GetRequiredService<TwoFactorService>();

            var setup = (await service.BeginSetupAsync(account))!;
            var key = setup.Key.Replace(" ", "");
            var enabled = await service.EnableAsync(account, Totp(key));
            Assert.True(enabled.Success, enabled.Error);
            return (person.Email, person.Password, key, enabled.RecoveryCodes);
        }

        private static Task<HttpResponseMessage> ApiLoginAsync(HttpClient client, string email, string password, string? otp = null) =>
            client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, otp });

        private static string Token(string html) =>
            Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

        private async Task<ApplicationUser> AccountAsync(string email)
        {
            using var scope = _factory.Services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!;
        }

        private async Task<bool> TwoFactorIsOnAsync(string email)
        {
            using var scope = _factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            return await users.GetTwoFactorEnabledAsync((await users.FindByEmailAsync(email))!);
        }

        // ---------- the api ----------

        [Fact]
        public async Task Api_AccountWithTwoFactor_NeedsTheCode_AndSaysSo()
        {
            var person = await PersonWithTwoFactorAsync();

            var response = await ApiLoginAsync(_factory.NewClient(), person.Email, person.Password);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("twoFactorRequired").GetBoolean());
            Assert.False(body.TryGetProperty("accessToken", out _));
        }

        [Fact]
        public async Task Api_RightCode_SignsIn_AndWrongCodeDoesNot()
        {
            var person = await PersonWithTwoFactorAsync();
            var client = _factory.NewClient();

            var wrong = await ApiLoginAsync(client, person.Email, person.Password, "000000");
            var right = await ApiLoginAsync(client, person.Email, person.Password, Totp(person.Key));

            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.True((await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("twoFactorRequired").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, right.StatusCode);
            Assert.False(string.IsNullOrEmpty((await right.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()));
            Assert.Contains(await _factory.AuditEventsAsync(), e => e.Action == AuditActions.TwoFactorFailed && e.Email == person.Email);
        }

        [Fact]
        public async Task Api_RecoveryCode_WorksOnce()
        {
            var person = await PersonWithTwoFactorAsync();
            var client = _factory.NewClient();
            var code = person.RecoveryCodes[0];

            var first = await ApiLoginAsync(client, person.Email, person.Password, code);
            var again = await ApiLoginAsync(client, person.Email, person.Password, code);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
            Assert.Contains(await _factory.AuditEventsAsync(), e => e.Action == AuditActions.RecoveryCodeUsed && e.Email == person.Email);
        }

        [Fact]
        public async Task Api_WrongCodes_CountTowardsTheLockout_EvenWithTheRightPassword()
        {
            var person = await PersonWithTwoFactorAsync();
            var client = _factory.NewClient();

            for (var i = 0; i < 5; i++)
                await ApiLoginAsync(client, person.Email, person.Password, "000000");
            var afterLockout = await ApiLoginAsync(client, person.Email, person.Password, Totp(person.Key));

            Assert.True(await _factory.IsLockedOutAsync(person.Email));
            Assert.Equal(HttpStatusCode.Unauthorized, afterLockout.StatusCode);
            Assert.False((await afterLockout.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("twoFactorRequired", out _));
        }

        [Fact]
        public async Task Api_AccountWithoutTwoFactor_IgnoresTheCodeField()
        {
            var person = await _factory.CreateLinkedUserAsync();

            var response = await ApiLoginAsync(_factory.NewClient(), person.Email, person.Password, "123456");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Profile_ShowsWhetherTwoFactorIsOn()
        {
            var person = await PersonWithTwoFactorAsync();
            var client = _factory.NewClient();
            var login = await ApiLoginAsync(client, person.Email, person.Password, Totp(person.Key));
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",
                (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString());

            var profile = await client.GetFromJsonAsync<JsonElement>("/api/v1/profile");

            Assert.True(profile.GetProperty("twoFactorEnabled").GetBoolean());
        }

        // ---------- turning it on and off ----------

        [Fact]
        public async Task Enabling_NeedsARightCode_AndGivesTenRecoveryCodes()
        {
            var person = await _factory.CreateLinkedUserAsync();
            using var scope = _factory.Services.CreateScope();
            var account = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(person.Email))!;
            var service = scope.ServiceProvider.GetRequiredService<TwoFactorService>();
            var setup = (await service.BeginSetupAsync(account))!;

            var wrong = await service.EnableAsync(account, "000000");
            Assert.False(wrong.Success);
            Assert.False(await TwoFactorIsOnAsync(person.Email));

            var enabled = await service.EnableAsync(account, Totp(setup.Key));
            Assert.True(enabled.Success, enabled.Error);
            Assert.Equal(10, enabled.RecoveryCodes.Count);
            Assert.True(await TwoFactorIsOnAsync(person.Email));
            Assert.StartsWith("otpauth://totp/", setup.Uri);
            //a second setup can never swap the key out from under one that is on
            Assert.Null(await service.BeginSetupAsync(account));
        }

        [Fact]
        public async Task Disabling_NeedsThePasswordAndACode()
        {
            var person = await PersonWithTwoFactorAsync();

            //each attempt is its own request, with its own scope and its own copy of the account
            async Task<OpResult> TryAsync(string password, string? code)
            {
                using var scope = _factory.Services.CreateScope();
                var account = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(person.Email))!;
                return await scope.ServiceProvider.GetRequiredService<TwoFactorService>().DisableAsync(account, password, code);
            }

            Assert.False((await TryAsync(person.Password, null)).Success);
            Assert.False((await TryAsync("Wrong-Password-1!", Totp(person.Key))).Success);
            Assert.False((await TryAsync(person.Password, "000000")).Success);
            Assert.True(await TwoFactorIsOnAsync(person.Email));

            var turnedOff = await TryAsync(person.Password, Totp(person.Key));

            Assert.True(turnedOff.Success, turnedOff.Error);
            Assert.False(await TwoFactorIsOnAsync(person.Email));
            Assert.Equal(HttpStatusCode.OK, (await ApiLoginAsync(_factory.NewClient(), person.Email, person.Password)).StatusCode);
        }

        // ---------- an admin resetting it ----------

        [Fact]
        public async Task AdminReset_RemovesTwoFactor_ForSomeoneWhoLostTheirPhone()
        {
            var person = await PersonWithTwoFactorAsync();
            var admin = await _factory.LoginClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
            var id = (await _factory.AccountStampAsync(person.Email)).Id;

            var reset = await admin.PostAsync($"/api/v1/users/{id}/reset-two-factor", null);

            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
            Assert.False(await TwoFactorIsOnAsync(person.Email));
            Assert.Equal(HttpStatusCode.OK, (await ApiLoginAsync(_factory.NewClient(), person.Email, person.Password)).StatusCode);
            //like a password reset, the audit row names the admin who did it and the person it was done to
            Assert.Contains(await _factory.AuditEventsAsync(), e => e.Action == AuditActions.TwoFactorReset && e.Email == ApiFactory.AdminEmail && e.Detail == person.Email);
        }

        [Fact]
        public async Task AdminReset_IsForAdminsOnly_AndUnknownAccountsAreNotFound()
        {
            var person = await PersonWithTwoFactorAsync();
            var developer = await _factory.CreateLinkedUserAsync();
            var developerClient = await _factory.LoginClientAsync(developer.Email, developer.Password);
            var admin = await _factory.LoginClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
            var id = (await _factory.AccountStampAsync(person.Email)).Id;

            Assert.Equal(HttpStatusCode.Forbidden, (await developerClient.PostAsync($"/api/v1/users/{id}/reset-two-factor", null)).StatusCode);
            Assert.True(await TwoFactorIsOnAsync(person.Email));
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync("/api/v1/users/no-such-account/reset-two-factor", null)).StatusCode);
        }

        // ---------- the website ----------

        [Fact]
        public async Task Website_SignIn_StopsAtTheCodeStep_UntilTheCodeIsRight()
        {
            var person = await PersonWithTwoFactorAsync();
            var browser = _factory.NewClient();

            var login = await ApiFactory.PostLoginAsync(browser, person.Email, person.Password);

            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            Assert.Contains("/Account/TwoFactor", login.Headers.Location!.ToString());
            //the password alone signed nobody in
            Assert.Contains("/Account/Login", (await browser.GetAsync("/Settings")).Headers.Location!.ToString());

            var page = await browser.GetStringAsync("/Account/TwoFactor");
            Assert.Contains("Enter your code", page);

            var wrong = await browser.PostAsync("/Account/TwoFactor", Form(page, "000000", recovery: false));
            Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
            Assert.Contains("That code is not right.", await wrong.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/Settings")).StatusCode);

            var right = await browser.PostAsync("/Account/TwoFactor", Form(page, Totp(person.Key), recovery: false));
            Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/Settings")).StatusCode);
        }

        [Fact]
        public async Task Website_RecoveryCode_SignsInOnce()
        {
            var person = await PersonWithTwoFactorAsync();

            async Task<HttpStatusCode> SignInWithRecoveryAsync()
            {
                var browser = _factory.NewClient();
                await ApiFactory.PostLoginAsync(browser, person.Email, person.Password);
                var page = await browser.GetStringAsync("/Account/TwoFactor?recovery=true");
                Assert.Contains("Recovery code", page);
                var response = await browser.PostAsync("/Account/TwoFactor", Form(page, person.RecoveryCodes[0], recovery: true));
                return response.StatusCode;
            }

            Assert.Equal(HttpStatusCode.Redirect, await SignInWithRecoveryAsync());
            Assert.Equal(HttpStatusCode.OK, await SignInWithRecoveryAsync());
        }

        [Fact]
        public async Task Website_WrongCodes_LockTheAccount()
        {
            var person = await PersonWithTwoFactorAsync();
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, person.Email, person.Password);
            var page = await browser.GetStringAsync("/Account/TwoFactor");

            for (var i = 0; i < 5; i++)
                await browser.PostAsync("/Account/TwoFactor", Form(page, "000000", recovery: false));

            Assert.True(await _factory.IsLockedOutAsync(person.Email));
        }

        [Fact]
        public async Task Website_CodeStep_IsNotReachableWithoutThePassword()
        {
            var browser = _factory.NewClient();

            var response = await browser.GetAsync("/Account/TwoFactor");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
        }

        private static FormUrlEncodedContent Form(string pageWithToken, string code, bool recovery) => new(new Dictionary<string, string>
        {
            ["Code"] = code,
            ["UseRecoveryCode"] = recovery ? "true" : "false",
            ["ReturnUrl"] = string.Empty,
            ["__RequestVerificationToken"] = Token(pageWithToken)
        });

        // ---------- the settings page ----------

        [Fact]
        public async Task Settings_SetUpAndTurnOff()
        {
            var person = await _factory.CreateLinkedUserAsync();
            var browser = _factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, person.Email, person.Password);

            //posts a form the way the page does, with the anti-forgery token taken from the page the form was on
            async Task<(HttpStatusCode Status, string Html)> PostAsync(string url, string pageWithToken, params (string Name, string Value)[] fields)
            {
                var form = fields.ToDictionary(f => f.Name, f => f.Value);
                form["__RequestVerificationToken"] = Token(pageWithToken);
                var response = await browser.PostAsync(url, new FormUrlEncodedContent(form));
                return (response.StatusCode, await response.Content.ReadAsStringAsync());
            }

            var off = await browser.GetStringAsync("/Settings/TwoFactor");
            Assert.Contains("Two-factor sign in is off", off);

            var started = await PostAsync("/Settings/TwoFactorStart", off);
            Assert.Equal(HttpStatusCode.OK, started.Status);
            var key = Regex.Match(started.Html, "<code>([a-z2-7 ]+)</code>").Groups[1].Value;
            Assert.False(string.IsNullOrWhiteSpace(key));
            Assert.False(await TwoFactorIsOnAsync(person.Email));

            var wrong = await PostAsync("/Settings/TwoFactorEnable", started.Html, ("code", "000000"));
            Assert.Equal(HttpStatusCode.OK, wrong.Status);
            Assert.Contains("That code is not right.", wrong.Html);
            Assert.False(await TwoFactorIsOnAsync(person.Email));

            var enabled = await PostAsync("/Settings/TwoFactorEnable", wrong.Html, ("code", Totp(key)));
            Assert.Equal(HttpStatusCode.OK, enabled.Status);
            Assert.Equal(10, Regex.Matches(enabled.Html, "<li><code>[^<]+</code></li>").Count);
            Assert.True(await TwoFactorIsOnAsync(person.Email));

            //the codes were shown once: opening the page again does not show them
            var on = await browser.GetStringAsync("/Settings/TwoFactor");
            Assert.Contains("Two-factor sign in is on", on);
            Assert.DoesNotContain("<li><code>", on);

            var wrongPassword = await PostAsync("/Settings/TwoFactorDisable", on, ("password", "Wrong-Password-1!"), ("code", Totp(key)));
            Assert.Contains("Your password is not correct.", wrongPassword.Html);
            Assert.True(await TwoFactorIsOnAsync(person.Email));

            var disabled = await PostAsync("/Settings/TwoFactorDisable", wrongPassword.Html, ("password", person.Password), ("code", Totp(key)));
            Assert.Equal(HttpStatusCode.Redirect, disabled.Status);
            Assert.False(await TwoFactorIsOnAsync(person.Email));
        }

        [Fact]
        public async Task Settings_TwoFactorPage_RequiresASignedInPerson()
        {
            var response = await _factory.NewClient().GetAsync("/Settings/TwoFactor");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
        }
    }
}
