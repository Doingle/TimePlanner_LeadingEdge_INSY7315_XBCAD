using System.Net.Http.Json;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //guards the test setup itself. If a setting is read too early in startup, a test host quietly falls back to the shared database in appsettings.json
    //and tests start seeing each other's data. These tests fail loudly when that happens
    public class TestHostIsolationTests
    {
        [Fact]
        public async Task EveryHost_RunsAgainstItsOwnDatabase()
        {
            using var first = new ApiFactory();
            using var second = new ApiFactory();
            await first.NewClient().PostAsJsonAsync("/api/v1/auth/login", new { email = "only-in-first@test.local", password = "Wrong-Pass-123!" });
            second.NewClient();

            Assert.Single(await first.AuditEventsAsync());
            Assert.Empty(await second.AuditEventsAsync());
        }

        [Fact]
        public async Task NoHost_FallsBackToTheDatabaseNamedInAppSettings()
        {
            using var factory = new ApiFactory();
            await factory.NewClient().GetAsync("/health");

            Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "timeplanner.db")),
                "a test host used the connection string from appsettings.json instead of its own");
        }
    }
}
//------------------------------EOF-----------------------------\\
