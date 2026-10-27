using Microsoft.AspNetCore.Hosting;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //a test host that runs in the Staging environment
    public class StagingFactory : ApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Staging");
        }
    }

    //-----------------------------
    //checks that the staging banner and noindex meta tag are rendered only when hosted in Staging
    public class EnvironmentBannerTests : IClassFixture<StagingFactory>, IClassFixture<ApiFactory>
    {
        private readonly StagingFactory _staging;
        private readonly ApiFactory _normal;

        public EnvironmentBannerTests(StagingFactory staging, ApiFactory normal)
        {
            _staging = staging;
            _normal = normal;
        }

        [Fact]
        public async Task LoginPageShowsTheStagingBannerOnStaging()
        {
            var response = await _staging.NewClient().GetAsync("/Account/Login");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Contains("env-banner", html);
            Assert.Contains("noindex", html);
        }

        [Fact]
        public async Task LoginPageHasNoBannerOutsideStaging()
        {
            var response = await _normal.NewClient().GetAsync("/Account/Login");
            var html = await response.Content.ReadAsStringAsync();

            Assert.DoesNotContain("env-banner", html);
            Assert.DoesNotContain("noindex", html);
        }

        [Fact]
        public async Task SignedInPagesShowTheStagingBannerOnStaging()
        {
            var user = await _staging.CreateLinkedUserAsync();
            var browser = _staging.NewClient();
            await ApiFactory.PostLoginAsync(browser, user.Email, user.Password);

            var response = await browser.GetAsync("/");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Contains("env-banner", html);
        }
    }
}
//------------------------------EOF-----------------------------\\
