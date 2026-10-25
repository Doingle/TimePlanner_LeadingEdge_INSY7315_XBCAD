using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Ai;
using Xunit;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //integration tests for organisation ai dashboard endpoint
    public class AiTests
    {
        //-----------------------------
        //status endpoint reports unavailable when unconfigured
        [Fact]
        public async Task Status_NotConfigured_ReportsUnavailable()
        {
            using var factory = new ApiFactory();
            var client = factory.NewClient();

            var response = await client.GetAsync("/api/v1/ai/status");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var status = await response.Content.ReadFromJsonAsync<DashboardAiStatus>();
            Assert.NotNull(status);
            Assert.False(status.Available);
            Assert.Null(status.Model);
        }

        //-----------------------------
        //build day returns 503 when organisation ai is not configured
        [Fact]
        public async Task BuildDay_NotConfigured_Returns503()
        {
            using var factory = new ApiFactory();
            var (email, password, _) = await factory.CreateLinkedUserAsync();
            var client = await factory.LoginClientAsync(email, password);

            var request = new DayBuildRequest(new DateOnly(2026, 10, 24), [], [], []);
            var response = await client.PostAsJsonAsync("/api/v1/ai/build-day", request);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        //-----------------------------
        //build day without token returns 401 unauthorized
        [Fact]
        public async Task BuildDay_NoToken_Returns401()
        {
            using var factory = new ApiFactory();
            var client = factory.NewClient();

            var request = new DayBuildRequest(new DateOnly(2026, 10, 24), [], [], []);
            var response = await client.PostAsJsonAsync("/api/v1/ai/build-day", request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        //-----------------------------
        //build day with more than 40 windows returns 400 bad request
        [Fact]
        public async Task BuildDay_TooManyWindows_Returns400()
        {
            using var factory = new ConfiguredAiFactory();
            var (email, password, _) = await factory.CreateLinkedUserAsync();
            var client = await factory.LoginClientAsync(email, password);

            var windows = Enumerable.Range(1, 41)
                .Select(i => new VoiceWindow($"W{i}", Guid.NewGuid(), DateTime.Now, DateTime.Now.AddHours(1), [], "note"))
                .ToList();

            var request = new DayBuildRequest(new DateOnly(2026, 10, 24), windows, [], []);
            var response = await client.PostAsJsonAsync("/api/v1/ai/build-day", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        //-----------------------------
        //configured organisation ai endpoint returns compilation result
        [Fact]
        public async Task BuildDay_Configured_ReturnsResult()
        {
            using var factory = new ConfiguredAiFactory();
            var (email, password, _) = await factory.CreateLinkedUserAsync();
            var client = await factory.LoginClientAsync(email, password);

            var window = new VoiceWindow("W1", Guid.NewGuid(), new DateTime(2026, 10, 24, 9, 0, 0), new DateTime(2026, 10, 24, 10, 0, 0),
                [new TimeRange(new DateTime(2026, 10, 24, 9, 0, 0), new DateTime(2026, 10, 24, 10, 0, 0))], "fixed bug");

            var request = new DayBuildRequest(
                new DateOnly(2026, 10, 24),
                [window],
                [new CatalogProject("P1", "Acme", "Portal")],
                [new CatalogActivity("A1", ["Coding"])]);

            var response = await client.PostAsJsonAsync("/api/v1/ai/build-day", request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content.ReadFromJsonAsync<DayBuildResult>();
            Assert.NotNull(result);
            Assert.Single(result.Entries);
            Assert.Equal("fixed bug", result.Entries[0].Note);
        }

        //-----------------------------
        //test factory with organisation ai settings and fake http handler
        private sealed class ConfiguredAiFactory : ApiFactory
        {
            protected override Dictionary<string, string?> Settings() => new(base.Settings())
            {
                ["Ai:Url"] = "https://fake-ai.local/v1",
                ["Ai:Model"] = "gpt-4o-mini",
                ["Ai:ApiKey"] = "test-api-key"
            };

            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                base.ConfigureWebHost(builder);
                builder.ConfigureServices(services =>
                {
                    services.AddHttpClient("ai").ConfigurePrimaryHttpMessageHandler(() => new FakeAiHttpMessageHandler());
                });
            }
        }

        //-----------------------------
        //fake http message handler for mock openai response
        private sealed class FakeAiHttpMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var content = JsonSerializer.Serialize(new
                {
                    choices = new[]
                    {
                        new
                        {
                            message = new
                            {
                                content = "{\"entries\":[{\"w\":\"W1\",\"s\":\"09:00\",\"e\":\"10:00\",\"p\":\"P1\",\"a\":\"A1\",\"note\":\"fixed bug\"}],\"flags\":[]}"
                            }
                        }
                    },
                    usage = new { completion_tokens = 25 }
                });

                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
                };

                return Task.FromResult(response);
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
