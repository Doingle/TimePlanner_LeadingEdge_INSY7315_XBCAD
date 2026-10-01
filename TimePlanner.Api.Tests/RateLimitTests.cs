using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //a host with low limits so a test can reach them. Each test starts its own host, so the counters of one test never affect another.
    //LoginPerMinute 5 leaves room for a test to sign in a few users before it exercises the limit
    public class LimitedFactory : ApiFactory
    {
        private readonly bool _trustProxy;

        public LimitedFactory(bool trustProxy = false) => _trustProxy = trustProxy;

        protected override Dictionary<string, string?> Settings()
        {
            var settings = base.Settings();
            settings["RateLimiting:LoginPerMinute"] = "5";
            settings["RateLimiting:ImportPerMinute"] = "2";
            settings["Proxy:TrustForwardedHeaders"] = _trustProxy ? "true" : "false";
            return settings;
        }
    }

    //-----------------------------
    //covers the limits on signing in and importing, and what happens behind a reverse proxy
    public class RateLimitTests
    {
        private static Task<HttpResponseMessage> ApiLoginAsync(HttpClient client, string email, string password, string? forwardedFor = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login") { Content = JsonContent.Create(new { email, password }) };
            if (forwardedFor != null)
                request.Headers.Add("X-Forwarded-For", forwardedFor);
            return client.SendAsync(request);
        }

        private static StringContent ImportJson() => new(
            $"{{\"entries\":[{{\"company\":\"Acme {Guid.NewGuid():N}\",\"project\":\"Web\",\"activity\":\"Coding\",\"start\":\"{DateTime.Today.AddDays(-3).AddHours(9):s}\",\"end\":\"{DateTime.Today.AddDays(-3).AddHours(10):s}\"}}]}}",
            Encoding.UTF8, "application/json");

        [Fact]
        public async Task ApiLogin_IsLimitedPerAddress_EvenForTheCorrectPassword()
        {
            using var factory = new LimitedFactory();
            var client = factory.NewClient();

            for (var i = 0; i < 5; i++)
                Assert.Equal(HttpStatusCode.Unauthorized, (await ApiLoginAsync(client, "nobody@test.local", "Wrong-Pass-123!")).StatusCode);
            var limited = await ApiLoginAsync(client, ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal("60", limited.Headers.GetValues("Retry-After").Single());
            Assert.Equal("application/problem+json", limited.Content.Headers.ContentType!.MediaType);
        }

        [Fact]
        public async Task CookieLogin_IsLimited_WithAPlainMessage()
        {
            using var factory = new LimitedFactory();
            var client = factory.NewClient();

            for (var i = 0; i < 5; i++)
                Assert.Equal(HttpStatusCode.OK, (await ApiFactory.PostLoginAsync(client, "nobody@test.local", "Wrong-Pass-123!")).StatusCode);
            var limited = await ApiFactory.PostLoginAsync(client, ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Contains("Too many requests", await limited.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task TheLoginLimit_DoesNotBlockOtherPages()
        {
            using var factory = new LimitedFactory();
            var client = factory.NewClient();
            for (var i = 0; i < 6; i++)
                await ApiLoginAsync(client, "nobody@test.local", "Wrong-Pass-123!");

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Login")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        }

        [Fact]
        public async Task Import_IsLimitedPerUser_NotForEveryone()
        {
            using var factory = new LimitedFactory();
            var alice = await factory.CreateLinkedUserAsync();
            var bob = await factory.CreateLinkedUserAsync();
            var aliceClient = await factory.LoginClientAsync(alice.Email, alice.Password);
            var bobClient = await factory.LoginClientAsync(bob.Email, bob.Password);

            Assert.Equal(HttpStatusCode.OK, (await aliceClient.PostAsync("/api/v1/timesheets/import", ImportJson())).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await aliceClient.PostAsync("/api/v1/timesheets/import", ImportJson())).StatusCode);
            var limited = await aliceClient.PostAsync("/api/v1/timesheets/import", ImportJson());

            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await bobClient.PostAsync("/api/v1/timesheets/import", ImportJson())).StatusCode);
        }

        [Fact]
        public async Task TheUploadPage_IsLimitedPerUser()
        {
            using var factory = new LimitedFactory();
            var user = await factory.CreateLinkedUserAsync();
            var browser = factory.NewClient();
            await ApiFactory.PostLoginAsync(browser, user.Email, user.Password);

            async Task<HttpStatusCode> UploadAsync()
            {
                var token = System.Text.RegularExpressions.Regex.Match(await browser.GetStringAsync("/CsvUpload"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
                var file = new ByteArrayContent(Encoding.UTF8.GetBytes("Company,Project,Activity,Start,End\r\n"));
                var form = new MultipartFormDataContent { { file, "file", "t.csv" }, { new StringContent(token), "__RequestVerificationToken" } };
                return (await browser.PostAsync("/CsvUpload", form)).StatusCode;
            }

            Assert.Equal(HttpStatusCode.OK, await UploadAsync());
            Assert.Equal(HttpStatusCode.OK, await UploadAsync());
            Assert.Equal(HttpStatusCode.TooManyRequests, await UploadAsync());
        }

        // ---------- behind a reverse proxy ----------

        [Fact]
        public async Task BehindATrustedProxy_EachRealClientGetsItsOwnLimit()
        {
            using var factory = new LimitedFactory(trustProxy: true);
            var client = factory.NewClient();

            for (var i = 0; i < 5; i++)
                await ApiLoginAsync(client, "nobody@test.local", "Wrong-Pass-123!", forwardedFor: "203.0.113.1");

            Assert.Equal(HttpStatusCode.TooManyRequests, (await ApiLoginAsync(client, "nobody@test.local", "x", forwardedFor: "203.0.113.1")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await ApiLoginAsync(client, "nobody@test.local", "x", forwardedFor: "203.0.113.2")).StatusCode);
        }

        [Fact]
        public async Task WithoutATrustedProxy_ForwardedHeadersAreIgnored_SoTheyCannotDodgeTheLimit()
        {
            using var factory = new LimitedFactory(trustProxy: false);
            var client = factory.NewClient();

            for (var i = 0; i < 5; i++)
                await ApiLoginAsync(client, "nobody@test.local", "Wrong-Pass-123!", forwardedFor: $"198.51.100.{i}");

            Assert.Equal(HttpStatusCode.TooManyRequests, (await ApiLoginAsync(client, "nobody@test.local", "x", forwardedFor: "198.51.100.99")).StatusCode);
        }

        [Fact]
        public async Task TheAuditLog_RecordsTheForwardedAddress_OnlyBehindATrustedProxy()
        {
            using var trusted = new LimitedFactory(trustProxy: true);
            using var untrusted = new LimitedFactory(trustProxy: false);

            await ApiLoginAsync(trusted.NewClient(), "nobody@test.local", "x", forwardedFor: "203.0.113.50");
            await ApiLoginAsync(untrusted.NewClient(), "nobody@test.local", "x", forwardedFor: "203.0.113.50");

            Assert.Equal("203.0.113.50", (await trusted.AuditEventsAsync()).Single().IpAddress);
            Assert.NotEqual("203.0.113.50", (await untrusted.AuditEventsAsync()).Single().IpAddress);
        }
    }
}
