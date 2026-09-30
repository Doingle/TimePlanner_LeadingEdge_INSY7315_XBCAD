using System.Net;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the website login: cookie sign in, anti-forgery, redirects and lockout
    [Collection("Api")]
    public class CookieAuthTests
    {
        private readonly ApiFactory _factory;

        public CookieAuthTests(ApiFactory factory) => _factory = factory;

        [Fact]
        public async Task AnonymousRequest_IsRedirectedToLogin()
        {
            var response = await _factory.NewClient().GetAsync("/");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Account/Login", response.Headers.Location!.OriginalString.Replace("https://localhost", ""));
        }

        [Fact]
        public async Task LoginPage_IsReachableAnonymously()
        {
            var response = await _factory.NewClient().GetAsync("/Account/Login");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task PostWithoutAntiForgeryToken_IsRejected()
        {
            var response = await _factory.NewClient().PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = ApiFactory.AdminEmail,
                ["Password"] = ApiFactory.AdminPassword
            }));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task WrongPassword_ShowsGenericError_AndDoesNotSignIn()
        {
            var client = _factory.NewClient();

            var response = await ApiFactory.PostLoginAsync(client, ApiFactory.AdminEmail, "Wrong-Pass-123!");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Invalid email or password.", body);
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
        }

        [Fact]
        public async Task UnknownEmail_ShowsTheSameError()
        {
            var response = await ApiFactory.PostLoginAsync(_factory.NewClient(), "nobody@test.local", "Wrong-Pass-123!");

            Assert.Contains("Invalid email or password.", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task ValidLogin_SignsInAndAllowsProtectedPages()
        {
            var client = _factory.NewClient();

            var login = await ApiFactory.PostLoginAsync(client, ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        }

        [Fact]
        public async Task ExternalReturnUrl_IsNotFollowed()
        {
            var login = await ApiFactory.PostLoginAsync(_factory.NewClient(), ApiFactory.AdminEmail, ApiFactory.AdminPassword, "https://evil.example/steal");

            Assert.Equal("/", login.Headers.Location!.OriginalString);
        }

        [Fact]
        public async Task LocalReturnUrl_IsFollowed()
        {
            var login = await ApiFactory.PostLoginAsync(_factory.NewClient(), ApiFactory.AdminEmail, ApiFactory.AdminPassword, "/Home/Privacy");

            Assert.Equal("/Home/Privacy", login.Headers.Location!.OriginalString);
        }

        [Fact]
        public async Task FiveFailedLogins_LockTheAccount_EvenForTheCorrectPassword()
        {
            var (email, password) = await _factory.CreateUserAsync();
            var client = _factory.NewClient();

            for (var i = 0; i < 5; i++)
                await ApiFactory.PostLoginAsync(client, email, "Wrong-Pass-123!");
            var afterLockout = await ApiFactory.PostLoginAsync(client, email, password);

            Assert.True(await _factory.IsLockedOutAsync(email));
            Assert.Contains("Invalid email or password.", await afterLockout.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
        }

        [Fact]
        public async Task Logout_EndsTheSession()
        {
            var client = _factory.NewClient();
            await ApiFactory.PostLoginAsync(client, ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            //the logout form sits in the layout, so the token comes from the home page
            var home = await client.GetStringAsync("/");
            var token = System.Text.RegularExpressions.Regex.Match(home, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
            await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/")).StatusCode);
        }
    }
}
