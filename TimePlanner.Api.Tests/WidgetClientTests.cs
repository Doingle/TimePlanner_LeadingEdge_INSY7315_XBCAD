using TimePlanner.Core.Sync;
using Xunit;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //integration tests for widget dashboard client authentication and refresh tokens against the real api
    [Collection("Api")]
    public class WidgetClientTests
    {
        private readonly ApiFactory _factory;

        public WidgetClientTests(ApiFactory factory) => _factory = factory;

        //-----------------------------
        //tests sign in refresh token rotation reuse detection and logout against the api
        [Fact]
        public async Task RefreshTokenFlow_RotatesTokensAndSupportsLogout()
        {
            var user = await _factory.CreateLinkedUserAsync();
            var httpClient = _factory.CreateClient();
            var client = new DashboardClient(httpClient);

            var signIn = await client.SignInAsync(user.Email, user.Password);
            Assert.Equal(SignInStatus.SignedIn, signIn.Status);
            Assert.NotNull(signIn.Token);
            Assert.NotNull(signIn.Token.RefreshToken);

            var oldToken = signIn.Token;

            var refreshed = await client.RefreshAsync(oldToken);
            Assert.Equal(SignInStatus.SignedIn, refreshed.Status);
            Assert.NotNull(refreshed.Token);
            Assert.NotNull(refreshed.Token.RefreshToken);
            Assert.NotEqual(oldToken.AccessToken, refreshed.Token.AccessToken);
            Assert.NotEqual(oldToken.RefreshToken, refreshed.Token.RefreshToken);

            var reuseAttempt = await client.RefreshAsync(oldToken);
            Assert.Equal(SignInStatus.InvalidDetails, reuseAttempt.Status);

            var freshSignIn = await client.SignInAsync(user.Email, user.Password);
            Assert.Equal(SignInStatus.SignedIn, freshSignIn.Status);

            await client.LogoutAsync(freshSignIn.Token!.RefreshToken!);

            var afterLogoutRefresh = await client.RefreshAsync(freshSignIn.Token);
            Assert.Equal(SignInStatus.InvalidDetails, afterLogoutRefresh.Status);
        }
    }
}
//------------------------------EOF-----------------------------\\
