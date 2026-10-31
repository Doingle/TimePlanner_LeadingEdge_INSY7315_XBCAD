using System.Net;
using System.Text;
using TimePlanner.Core.Sync;

namespace TimePlanner.Core.Tests.Sync;

public class WidgetReleaseTests
{
    [Theory]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("2.0.0", "1.99.0", true)]
    [InlineData("1.2.3", "1.2.3.0", false)]
    [InlineData("v1.2.3", "1.2.2+build123", true)]
    [InlineData("1.2.3", "1.2.3+build123", false)]
    [InlineData("1.2.3", "1.3.0", false)]
    [InlineData(null, "1.0.0", false)]
    [InlineData("broken", "1.0.0", false)]
    [InlineData("1.0.0-preview.1", "0.9.0", false)]
    [InlineData("1.2.3", "unknown", false)]
    public void ComparesStableReleaseNumbers(string? latest, string installed, bool newer) =>
        Assert.Equal(newer, new WidgetRelease(latest).IsNewerThan(installed));

    [Theory]
    [InlineData(404, "")]
    [InlineData(500, "error")]
    [InlineData(200, "not json")]
    [InlineData(200, "{\"latestVersion\":42}")]
    public async Task OptionalEndpointFailuresReturnNoRelease(int code, string body)
    {
        using var handler = new FakeDashboardHandler
        {
            Respond = request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/api/v1/widget/version", request.RequestUri!.AbsolutePath);
                Assert.Null(request.Headers.Authorization);
                return new HttpResponseMessage((HttpStatusCode)code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }
        };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://dashboard.test/") };
        Assert.Null(await new DashboardClient(http).GetWidgetReleaseAsync());
    }

    [Fact]
    public async Task OfflineReturnsNoRelease()
    {
        using var handler = new FakeDashboardHandler { ThrowOffline = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://dashboard.test/") };
        Assert.Null(await new DashboardClient(http).GetWidgetReleaseAsync());
    }

    //-----------------------------
    //path traversal input fails parsing returning null
    [Fact]
    public void PathTraversalReturnsNull()
    {
        var parsed = WidgetRelease.Parse("1.2.3/../../x");
        Assert.Null(parsed);
    }
}

