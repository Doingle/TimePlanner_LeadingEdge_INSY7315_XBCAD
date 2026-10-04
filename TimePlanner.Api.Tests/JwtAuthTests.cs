using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the bearer token login used by the widget and other api clients
    [Collection("Api")]
    public class JwtAuthTests
    {
        private record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc);
        private record MeResponse(string Email, string[] Roles);

        private readonly ApiFactory _factory;

        public JwtAuthTests(ApiFactory factory) => _factory = factory;

        private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
            client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });

        private async Task<string> TokenForAdminAsync()
        {
            var response = await LoginAsync(_factory.NewClient(), ApiFactory.AdminEmail, ApiFactory.AdminPassword);
            return (await response.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;
        }

        private Task<HttpResponseMessage> MeAsync(string? token)
        {
            var client = _factory.NewClient();
            if (token != null)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client.GetAsync("/api/v1/auth/me");
        }

        //-----------------------------
        //builds a token the way the server does, so a test can make it expired or sign it with the wrong key
        private static string MakeToken(string signingKey, DateTime notBefore, DateTime expires, params Claim[] account) =>
            new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim("email", ApiFactory.AdminEmail), new Claim("role", "Admin") }.Concat(account)),
                Issuer = "TimePlanner.Dashboard",
                Audience = "TimePlanner.Clients",
                NotBefore = notBefore,
                Expires = expires,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256)
            });

        [Fact]
        public async Task ValidLogin_ReturnsABearerToken()
        {
            var response = await LoginAsync(_factory.NewClient(), ApiFactory.AdminEmail, ApiFactory.AdminPassword);
            var body = await response.Content.ReadFromJsonAsync<TokenResponse>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Bearer", body!.TokenType);
            Assert.True(body.ExpiresAtUtc > DateTime.UtcNow);
        }

        [Fact]
        public async Task Me_ReturnsTheEmailAndRolesInTheToken()
        {
            var response = await MeAsync(await TokenForAdminAsync());
            var me = await response.Content.ReadFromJsonAsync<MeResponse>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(ApiFactory.AdminEmail, me!.Email);
            Assert.Contains("Admin", me.Roles);
        }

        [Fact]
        public async Task WrongPassword_And_UnknownEmail_Return401()
        {
            var client = _factory.NewClient();

            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, ApiFactory.AdminEmail, "Wrong-Pass-123!")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "nobody@test.local", "Wrong-Pass-123!")).StatusCode);
        }

        [Fact]
        public async Task MalformedLoginBody_Returns400()
        {
            var response = await _factory.NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = "not-an-email" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task FiveFailedLogins_LockTheAccount_EvenForTheCorrectPassword()
        {
            var (email, password) = await _factory.CreateUserAsync();
            var client = _factory.NewClient();

            for (var i = 0; i < 5; i++)
                await LoginAsync(client, email, "Wrong-Pass-123!");
            var afterLockout = await LoginAsync(client, email, password);

            Assert.Equal(HttpStatusCode.Unauthorized, afterLockout.StatusCode);
            Assert.True(await _factory.IsLockedOutAsync(email));
        }

        [Fact]
        public async Task Me_WithoutToken_Returns401()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(null)).StatusCode);
        }

        [Fact]
        public async Task Me_WithTamperedToken_Returns401()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(await TokenForAdminAsync() + "x")).StatusCode);
        }

        [Fact]
        public async Task Me_WithGarbageToken_Returns401()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync("abc.def.ghi")).StatusCode);
        }

        [Fact]
        public async Task Me_WithExpiredToken_Returns401()
        {
            var expired = MakeToken(ApiFactory.JwtKey, DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1));

            Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(expired)).StatusCode);
        }

        [Fact]
        public async Task Me_WithTokenSignedByAnotherKey_Returns401()
        {
            var forged = MakeToken("some-other-attacker-key-that-is-long-enough-0123456", DateTime.UtcNow, DateTime.UtcNow.AddHours(1));

            Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(forged)).StatusCode);
        }

        [Fact]
        public async Task ValidTokenForSameKey_IsAccepted()
        {
            var (id, stamp) = await _factory.AccountStampAsync(ApiFactory.AdminEmail);
            var valid = MakeToken(ApiFactory.JwtKey, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), new Claim("sub", id), new Claim("stamp", stamp));

            Assert.Equal(HttpStatusCode.OK, (await MeAsync(valid)).StatusCode);
        }

        //-----------------------------
        //a token that is correctly signed but does not match the account's current security stamp, or names no account, is refused
        [Fact]
        public async Task ASignedTokenThatDoesNotMatchTheAccount_IsRefused()
        {
            var (id, stamp) = await _factory.AccountStampAsync(ApiFactory.AdminEmail);

            var noAccount = MakeToken(ApiFactory.JwtKey, DateTime.UtcNow, DateTime.UtcNow.AddHours(1));
            var staleStamp = MakeToken(ApiFactory.JwtKey, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), new Claim("sub", id), new Claim("stamp", "an-old-stamp"));
            var noStamp = MakeToken(ApiFactory.JwtKey, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), new Claim("sub", id));
            var unknownAccount = MakeToken(ApiFactory.JwtKey, DateTime.UtcNow, DateTime.UtcNow.AddHours(1), new Claim("sub", Guid.NewGuid().ToString()), new Claim("stamp", stamp));

            foreach (var token in new[] { noAccount, staleStamp, noStamp, unknownAccount })
                Assert.Equal(HttpStatusCode.Unauthorized, (await MeAsync(token)).StatusCode);
        }
    }
}
