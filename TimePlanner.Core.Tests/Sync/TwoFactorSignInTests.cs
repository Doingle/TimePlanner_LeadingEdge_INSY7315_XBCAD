using System.Net;
using TimePlanner.Core.Sync;
using Xunit;

namespace TimePlanner.Core.Tests.Sync
{
    //-----------------------------
    //tests for how the widget's client reads the dashboard's answer when an account has two-factor sign in on
    public class TwoFactorSignInTests
    {
        private const string SignedIn = "{\"accessToken\":\"a\",\"tokenType\":\"Bearer\",\"expiresAtUtc\":\"2030-01-01T00:00:00Z\",\"refreshToken\":\"r\",\"refreshExpiresAtUtc\":\"2030-02-01T00:00:00Z\"}";

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

        private static DashboardClient ClientFor(FakeDashboardHandler handler) =>
            new(new HttpClient(handler) { BaseAddress = new Uri("https://dashboard.test/") });

        //-----------------------------
        //a 401 that says a code is needed is not "wrong password", so the widget can ask for the code instead
        [Fact]
        public async Task CodeNeeded_IsNotAWrongPassword()
        {
            var handler = new FakeDashboardHandler { Respond = _ => Json(HttpStatusCode.Unauthorized, "{\"title\":\"A two-factor code is required.\",\"status\":401,\"twoFactorRequired\":true}") };

            var outcome = await ClientFor(handler).SignInAsync("a@b.c", "password");

            Assert.Equal(SignInStatus.TwoFactorRequired, outcome.Status);
            Assert.Equal("A two-factor code is required.", outcome.Message);
            Assert.Null(outcome.Token);
        }

        //-----------------------------
        //any other 401, with or without a body, stays a wrong email or password
        [Theory]
        [InlineData("{\"title\":\"Invalid email or password.\",\"status\":401}")]
        [InlineData("")]
        [InlineData("not json")]
        public async Task PlainRefusal_StaysAWrongPassword(string body)
        {
            var handler = new FakeDashboardHandler { Respond = _ => Json(HttpStatusCode.Unauthorized, body) };

            var outcome = await ClientFor(handler).SignInAsync("a@b.c", "password", "123456");

            Assert.Equal(SignInStatus.InvalidDetails, outcome.Status);
        }

        //-----------------------------
        //the code goes to the dashboard with the password, and a blank code is left out
        [Fact]
        public async Task TheCode_IsSentWithThePassword_AndBlankIsLeftOut()
        {
            var handler = new FakeDashboardHandler { Respond = _ => Json(HttpStatusCode.OK, SignedIn) };
            var client = ClientFor(handler);

            var withCode = await client.SignInAsync("a@b.c", "password", " 123456 ");
            await client.SignInAsync("a@b.c", "password", "  ");
            await client.SignInAsync("a@b.c", "password");

            Assert.Equal(SignInStatus.SignedIn, withCode.Status);
            Assert.Contains("\"otp\":\"123456\"", handler.Bodies[0]);
            Assert.Contains("\"otp\":null", handler.Bodies[1]);
            Assert.Contains("\"otp\":null", handler.Bodies[2]);
        }
    }
}
//------------------------------EOF-----------------------------\\
