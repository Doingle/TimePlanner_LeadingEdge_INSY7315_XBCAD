using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers refresh tokens: staying signed in without the password, and everything that must end a session early.
    //a refresh token is as powerful as the password for the weeks it lives, so most of these tests are about it NOT working
    [Collection("Api")]
    public class RefreshTokenTests
    {
        private record Tokens(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken, DateTime RefreshExpiresAtUtc);

        private readonly ApiFactory _factory;

        public RefreshTokenTests(ApiFactory factory) => _factory = factory;

        private async Task<(string Email, Tokens Tokens)> SignInNewUserAsync()
        {
            var user = await _factory.CreateLinkedUserAsync();
            return (user.Email, await LoginAsync(user.Email, user.Password));
        }

        private async Task<Tokens> LoginAsync(string email, string password)
        {
            var response = await _factory.NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email, password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<Tokens>())!;
        }

        private Task<HttpResponseMessage> RefreshAsync(string? token) =>
            _factory.NewClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = token });

        private Task<HttpResponseMessage> MeAsync(string accessToken)
        {
            var client = _factory.NewClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return client.GetAsync("/api/v1/auth/me");
        }

        //-----------------------------
        //reads or changes the stored rows directly, which is how a test ages a token without waiting weeks
        private async Task<T> WithDbAsync<T>(Func<AuthDbContext, Task<T>> work)
        {
            using var scope = _factory.Services.CreateScope();
            return await work(scope.ServiceProvider.GetRequiredService<AuthDbContext>());
        }

        private Task<RefreshToken> RowFor(string token) => WithDbAsync(async db =>
        {
            var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
            return await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.TokenHash == hash);
        });

        // ---------- signing in and refreshing ----------

        [Fact]
        public async Task Login_ReturnsARefreshTokenThatLastsAbout30Days()
        {
            var (_, tokens) = await SignInNewUserAsync();

            Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
            Assert.InRange(tokens.RefreshToken.Length, 40, 200);
            Assert.InRange((tokens.RefreshExpiresAtUtc - DateTime.UtcNow).TotalDays, 29.9, 30.1);
            //the access token is still short lived
            Assert.InRange((tokens.ExpiresAtUtc - DateTime.UtcNow).TotalMinutes, 29, 31);
        }

        [Fact]
        public async Task Refresh_GivesAWorkingAccessTokenAndANewRefreshToken()
        {
            var (email, first) = await SignInNewUserAsync();

            var response = await RefreshAsync(first.RefreshToken);
            var second = (await response.Content.ReadFromJsonAsync<Tokens>())!;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEqual(first.RefreshToken, second.RefreshToken);
            Assert.NotEqual(first.AccessToken, second.AccessToken);
            var me = await MeAsync(second.AccessToken);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.Contains(email, await me.Content.ReadAsStringAsync());
            Assert.Contains("Developer", await me.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Refresh_CanBeChainedForAsLongAsEachNewTokenIsUsed()
        {
            var (_, tokens) = await SignInNewUserAsync();

            for (var i = 0; i < 4; i++)
            {
                var response = await RefreshAsync(tokens.RefreshToken);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                tokens = (await response.Content.ReadFromJsonAsync<Tokens>())!;
            }

            Assert.Equal(HttpStatusCode.OK, (await MeAsync(tokens.AccessToken)).StatusCode);
        }

        [Fact]
        public async Task Refresh_KeepsTheAccountsRole()
        {
            var admin = await LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            var refreshed = (await (await RefreshAsync(admin.RefreshToken)).Content.ReadFromJsonAsync<Tokens>())!;

            Assert.Contains("Admin", await (await MeAsync(refreshed.AccessToken)).Content.ReadAsStringAsync());
        }

        // ---------- the tokens that must not work ----------

        [Fact]
        public async Task AnUnknownOrMalformedToken_IsRefused()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync("not-a-real-token")).StatusCode);
            //the access token is not a refresh token, it is also longer than any refresh token can be
            var (_, tokens) = await SignInNewUserAsync();
            Assert.NotEqual(HttpStatusCode.OK, (await RefreshAsync(tokens.AccessToken)).StatusCode);
            //missing, empty and oversized values never get as far as a lookup
            Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(null)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync("")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(new string('a', 201))).StatusCode);
        }

        [Fact]
        public async Task AllFailuresLookTheSame()
        {
            var (_, tokens) = await SignInNewUserAsync();
            await RefreshAsync(tokens.RefreshToken);

            var unknown = await RefreshAsync("not-a-real-token");
            var reused = await RefreshAsync(tokens.RefreshToken);

            //only the request trace id differs
            Assert.Equal(unknown.StatusCode, reused.StatusCode);
            Assert.Equal((await unknown.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("title").GetString(),
                (await reused.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("title").GetString());
        }

        [Fact]
        public async Task AnExpiredToken_IsRefused()
        {
            var (_, tokens) = await SignInNewUserAsync();
            var row = await RowFor(tokens.RefreshToken);
            await WithDbAsync(async db => await db.RefreshTokens.Where(t => t.Id == row.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresUtc, DateTime.UtcNow.AddMinutes(-1))));

            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task ASessionOlderThanTheMaximumAge_CannotBeRefreshedEvenIfEachTokenWasFresh()
        {
            var (_, tokens) = await SignInNewUserAsync();
            var row = await RowFor(tokens.RefreshToken);
            await WithDbAsync(async db => await db.RefreshTokens.Where(t => t.FamilyId == row.FamilyId).ExecuteUpdateAsync(s => s.SetProperty(t => t.FamilyStartedUtc, DateTime.UtcNow.AddDays(-91))));

            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task ANewTokenNeverOutlivesTheMaximumAge()
        {
            var (_, tokens) = await SignInNewUserAsync();
            var row = await RowFor(tokens.RefreshToken);
            var started = DateTime.UtcNow.AddDays(-80);
            await WithDbAsync(async db => await db.RefreshTokens.Where(t => t.FamilyId == row.FamilyId).ExecuteUpdateAsync(s => s.SetProperty(t => t.FamilyStartedUtc, started)));

            var next = (await (await RefreshAsync(tokens.RefreshToken)).Content.ReadFromJsonAsync<Tokens>())!;

            //30 days from now would be day 110, the cap is day 90
            Assert.InRange((next.RefreshExpiresAtUtc - started).TotalDays, 89.9, 90.1);
        }

        // ---------- theft ----------

        [Fact]
        public async Task UsingATokenASecondTime_EndsTheWholeSession()
        {
            var (_, first) = await SignInNewUserAsync();
            var second = (await (await RefreshAsync(first.RefreshToken)).Content.ReadFromJsonAsync<Tokens>())!;

            //whoever holds the old copy (the thief or the real client) presents it again
            var replay = await RefreshAsync(first.RefreshToken);

            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            //and the newest token is dead too, so a thief cannot keep the session going and the real owner has to sign in again
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(second.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task TheReplayIsRecordedInTheAuditLog()
        {
            var (_, first) = await SignInNewUserAsync();
            var before = (await _factory.AuditEventsAsync()).Count(e => e.Action == AuditActions.RefreshTokenReuse);
            await RefreshAsync(first.RefreshToken);
            await RefreshAsync(first.RefreshToken);

            var events = await _factory.AuditEventsAsync();

            Assert.Equal(before + 1, events.Count(e => e.Action == AuditActions.RefreshTokenReuse && e.Detail == "api, reuse"));
            //the token itself never reaches the log
            Assert.DoesNotContain(events, e => (e.Detail ?? "").Contains(first.RefreshToken) || (e.Email ?? "").Contains(first.RefreshToken));
        }

        [Fact]
        public async Task RefreshesAtTheSameMoment_OnlyOneSucceeds()
        {
            //a race is not guaranteed to happen on any one try, so it is tried on several tokens with the requests released together
            for (var round = 0; round < 8; round++)
            {
                var (_, tokens) = await SignInNewUserAsync();
                var gate = new TaskCompletionSource();
                var requests = Enumerable.Range(0, 8).Select(async _ =>
                {
                    await gate.Task;
                    return await RefreshAsync(tokens.RefreshToken);
                }).ToList();
                gate.SetResult();
                var results = await Task.WhenAll(requests);

                Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
            }
        }

        [Fact]
        public async Task OnlyAHashIsStored_NotTheToken()
        {
            var (_, tokens) = await SignInNewUserAsync();

            var stored = await WithDbAsync(db => db.RefreshTokens.AsNoTracking().Select(t => t.TokenHash).ToListAsync());

            Assert.DoesNotContain(tokens.RefreshToken, stored);
            Assert.All(stored, hash => Assert.Equal(44, hash.Length));
        }

        // ---------- the account changing ends the session ----------

        [Fact]
        public async Task ChangingThePassword_EndsEveryRefreshToken()
        {
            var (email, tokens) = await SignInNewUserAsync();

            await _factory.SetPasswordAsync(email, "A-Brand-New-Pass-77!");

            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task DeactivatingTheAccount_EndsEveryRefreshToken()
        {
            var (email, tokens) = await SignInNewUserAsync();
            using (var scope = _factory.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = (await users.FindByEmailAsync(email))!;
                user.IsActive = false;
                await users.UpdateAsync(user);
            }

            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.RefreshToken)).StatusCode);
        }

        [Fact]
        public async Task AnAccountThatMustChangeItsPassword_CannotRefresh()
        {
            var (email, tokens) = await SignInNewUserAsync();
            using (var scope = _factory.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var user = (await users.FindByEmailAsync(email))!;
                user.MustChangePassword = true;
                await users.UpdateAsync(user);
            }

            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(tokens.RefreshToken)).StatusCode);
        }

        // ---------- signing out ----------

        [Fact]
        public async Task Logout_EndsTheSession_AndOnlyThatSession()
        {
            var user = await _factory.CreateLinkedUserAsync();
            var widget = await LoginAsync(user.Email, user.Password);
            var otherDevice = await LoginAsync(user.Email, user.Password);

            var response = await _factory.NewClient().PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = widget.RefreshToken });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(widget.RefreshToken)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(otherDevice.RefreshToken)).StatusCode);
            Assert.Contains(await _factory.AuditEventsAsync(), e => e.Action == AuditActions.Logout && e.Detail == "api");
        }

        [Fact]
        public async Task Logout_SaysNothingAboutWhetherTheTokenWasKnown()
        {
            var response = await _factory.NewClient().PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = "not-a-real-token" });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task Logout_EndsASessionEvenAfterTheTokenWasSwappedForANewOne()
        {
            var (_, first) = await SignInNewUserAsync();
            var second = (await (await RefreshAsync(first.RefreshToken)).Content.ReadFromJsonAsync<Tokens>())!;

            await _factory.NewClient().PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = second.RefreshToken });

            Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(second.RefreshToken)).StatusCode);
        }

        // ---------- housekeeping ----------

        [Fact]
        public async Task Login_ClearsOutThatPersonsLongExpiredTokens()
        {
            var user = await _factory.CreateLinkedUserAsync();
            var old = await LoginAsync(user.Email, user.Password);
            var oldRow = await RowFor(old.RefreshToken);
            await WithDbAsync(async db => await db.RefreshTokens.Where(t => t.Id == oldRow.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresUtc, DateTime.UtcNow.AddDays(-2))));

            await LoginAsync(user.Email, user.Password);

            Assert.False(await WithDbAsync(db => db.RefreshTokens.AnyAsync(t => t.Id == oldRow.Id)));
        }

        [Fact]
        public async Task ARefreshToken_IsNotAcceptedAsAnAccessToken()
        {
            var (_, tokens) = await SignInNewUserAsync();

            Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(tokens.RefreshToken)).StatusCode);
        }
    }
}
