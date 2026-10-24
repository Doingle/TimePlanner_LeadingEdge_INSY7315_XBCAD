using System.Net;
using System.Net.Http.Json;
using TimePlanner.Core.Sync;

namespace TimePlanner.Api.Tests;

public class WidgetVersionTests
{
    private sealed class VersionFactory(string? version) : ApiFactory
    {
        protected override Dictionary<string, string?> Settings()
        {
            var settings = base.Settings();
            settings["Database:Provider"] = "Sqlite";
            settings["WidgetUpdate:LatestVersion"] = version;
            return settings;
        }
    }

    [Theory]
    [InlineData("1.10.0", "1.10.0")]
    [InlineData(" v1.2.3 ", "v1.2.3")]
    [InlineData(null, null)]
    [InlineData("broken", null)]
    [InlineData("2.0.0-preview", null)]
    public async Task ReportsConfiguredStableVersion_WithoutSignIn(string? configured, string? expected)
    {
        using var factory = new VersionFactory(configured);
        using var client = factory.NewClient();
        using var response = await client.GetAsync("/api/v1/widget/version");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, (await response.Content.ReadFromJsonAsync<WidgetRelease>())!.LatestVersion);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }
}
