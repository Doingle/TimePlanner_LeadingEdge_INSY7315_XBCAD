using TimePlanner.Widget;
using Xunit;

namespace TimePlanner.Widget.Tests.Services
{
    //-----------------------------
    //unit tests for build-time dashboard url selection
    public class DashboardUrlTests
    {
        //-----------------------------
        //developer environment override beats built url
        [Fact]
        public void EnvironmentBeatsBuiltUrl()
        {
            var env = "https://dev.local:5001/";
            var built = "https://staging.example.com/";

            var chosen = WidgetServices.ChooseDashboardUrl(env, built);

            Assert.Equal("https://dev.local:5001/", chosen);
        }

        //-----------------------------
        //built url is used when developer environment variable is empty
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void BuiltUrlUsedWhenEnvironmentEmpty(string? env)
        {
            var built = "https://custom-dashboard.example.com/";

            var chosen = WidgetServices.ChooseDashboardUrl(env, built);

            Assert.Equal("https://custom-dashboard.example.com/", chosen);
        }

        //-----------------------------
        //both empty returns null so core default url applies
        [Fact]
        public void BothEmptyReturnsNull()
        {
            var chosen = WidgetServices.ChooseDashboardUrl(null, null);

            Assert.Null(chosen);
        }

        //-----------------------------
        //whitespace built url treated as empty returning null
        [Fact]
        public void WhitespaceBuiltUrlReturnsNull()
        {
            var chosen = WidgetServices.ChooseDashboardUrl("", "   ");

            Assert.Null(chosen);
        }
    }
}
//------------------------------EOF-----------------------------\\
