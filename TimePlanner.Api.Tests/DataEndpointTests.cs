using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TimePlanner.Core.Domain.Enums;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //covers the read endpoints: authentication is required, results are correct, and people only see the time data they are allowed to
    [Collection("Api")]
    public class DataEndpointTests
    {
        private static readonly DateTime Day = new(2026, 9, 28, 9, 0, 0);
        private readonly ApiFactory _factory;

        public DataEndpointTests(ApiFactory factory) => _factory = factory;

        private static string Range(DateTime from, DateTime to) => $"from={from:s}&to={to:s}";
        private static string EntriesUrl(int? userId = null) => $"/api/v1/timeentries?{Range(Day.Date, Day.Date.AddDays(1))}" + (userId == null ? "" : $"&userId={userId}");

        private static async Task<JsonElement[]> ListAsync(HttpResponseMessage response) =>
            (await response.Content.ReadFromJsonAsync<JsonElement[]>())!;

        [Theory]
        [InlineData("/api/v1/companies")]
        [InlineData("/api/v1/projects")]
        [InlineData("/api/v1/projects/1")]
        [InlineData("/api/v1/categories")]
        [InlineData("/api/v1/tasks")]
        [InlineData("/api/v1/timeentries?from=2026-09-28T00:00:00&to=2026-09-29T00:00:00")]
        public async Task DataEndpoints_RequireAToken(string url)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.NewClient().GetAsync(url)).StatusCode);
        }

        [Fact]
        public async Task CookieSession_DoesNotGrantApiAccess()
        {
            var client = _factory.NewClient();
            await ApiFactory.PostLoginAsync(client, ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/companies")).StatusCode);
        }

        [Fact]
        public async Task Health_IsAnonymous_AndReportsOk()
        {
            var response = await _factory.NewClient().GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("ok", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Me_IncludesTheLinkedProfileId()
        {
            var dev = await _factory.CreateLinkedUserAsync();
            var client = await _factory.LoginClientAsync(dev.Email, dev.Password);

            var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");

            Assert.Equal(dev.AppUserId.ToString(), me.GetProperty("appUserId").GetString());
        }

        [Fact]
        public async Task Companies_ReturnsSeededCompany()
        {
            var dev = await _factory.CreateLinkedUserAsync();
            var seeded = await _factory.SeedWorkAsync(dev.AppUserId, Day);
            var client = await _factory.LoginClientAsync(dev.Email, dev.Password);

            var companies = await ListAsync(await client.GetAsync("/api/v1/companies"));

            Assert.Contains(companies, c => c.GetProperty("id").GetInt32() == seeded.CompanyId);
        }

        [Fact]
        public async Task Projects_ActiveOnly_ExcludesClosedProjects()
        {
            var dev = await _factory.CreateLinkedUserAsync();
            var active = await _factory.SeedWorkAsync(dev.AppUserId, Day);
            var closed = await _factory.SeedWorkAsync(dev.AppUserId, Day, ProjectStatus.Closed);
            var client = await _factory.LoginClientAsync(dev.Email, dev.Password);

            var all = await ListAsync(await client.GetAsync("/api/v1/projects"));
            var activeOnly = await ListAsync(await client.GetAsync("/api/v1/projects?activeOnly=true"));

            Assert.Contains(all, p => p.GetProperty("id").GetInt32() == closed.ProjectId);
            Assert.Contains(activeOnly, p => p.GetProperty("id").GetInt32() == active.ProjectId);
            Assert.DoesNotContain(activeOnly, p => p.GetProperty("id").GetInt32() == closed.ProjectId);
        }

        [Fact]
        public async Task Projects_CanBeFilteredByCompany_AndFetchedById()
        {
            var dev = await _factory.CreateLinkedUserAsync();
            var seeded = await _factory.SeedWorkAsync(dev.AppUserId, Day);
            var client = await _factory.LoginClientAsync(dev.Email, dev.Password);

            var byCompany = await ListAsync(await client.GetAsync($"/api/v1/projects?companyId={seeded.CompanyId}"));
            var single = await client.GetFromJsonAsync<JsonElement>($"/api/v1/projects/{seeded.ProjectId}");

            Assert.Equal(seeded.ProjectId, Assert.Single(byCompany).GetProperty("id").GetInt32());
            Assert.Equal(seeded.CompanyId, single.GetProperty("companyId").GetInt32());
            Assert.StartsWith("Client ", single.GetProperty("companyName").GetString());
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/projects/999999")).StatusCode);
        }

        [Fact]
        public async Task Categories_ReturnsTheActivityTree()
        {
            var dev = await _factory.CreateLinkedUserAsync();
            var client = await _factory.LoginClientAsync(dev.Email, dev.Password);

            var categories = await ListAsync(await client.GetAsync("/api/v1/categories"));

            Assert.NotEmpty(categories);
            Assert.Contains(categories, c => c.GetProperty("parentId").ValueKind == JsonValueKind.Null);
        }

        [Fact]
        public async Task Tasks_ReturnsOnlyTheCallersOwnTasks()
        {
            var alice = await _factory.CreateLinkedUserAsync();
            var bob = await _factory.CreateLinkedUserAsync();
            var aliceWork = await _factory.SeedWorkAsync(alice.AppUserId, Day);
            var bobWork = await _factory.SeedWorkAsync(bob.AppUserId, Day);
            var client = await _factory.LoginClientAsync(alice.Email, alice.Password);

            var tasks = await ListAsync(await client.GetAsync("/api/v1/tasks"));

            Assert.Equal(aliceWork.TaskId, Assert.Single(tasks).GetProperty("id").GetInt32());
            Assert.DoesNotContain(tasks, t => t.GetProperty("id").GetInt32() == bobWork.TaskId);
        }

        [Fact]
        public async Task TimeEntries_DeveloperSeesOnlyTheirOwn()
        {
            var alice = await _factory.CreateLinkedUserAsync();
            var bob = await _factory.CreateLinkedUserAsync();
            var aliceWork = await _factory.SeedWorkAsync(alice.AppUserId, Day);
            await _factory.SeedWorkAsync(bob.AppUserId, Day);
            var client = await _factory.LoginClientAsync(alice.Email, alice.Password);

            var entries = await ListAsync(await client.GetAsync(EntriesUrl()));

            var entry = Assert.Single(entries);
            Assert.Equal(aliceWork.EntryId, entry.GetProperty("id").GetInt32());
            Assert.Equal(alice.AppUserId, entry.GetProperty("userId").GetInt32());
            Assert.Equal(45, entry.GetProperty("durationMinutes").GetDouble());
        }

        [Fact]
        public async Task TimeEntries_DeveloperCannotReadAnotherUser()
        {
            var alice = await _factory.CreateLinkedUserAsync();
            var bob = await _factory.CreateLinkedUserAsync();
            await _factory.SeedWorkAsync(bob.AppUserId, Day);
            var client = await _factory.LoginClientAsync(alice.Email, alice.Password);

            var response = await client.GetAsync(EntriesUrl(bob.AppUserId));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Theory]
        [InlineData("Admin")]
        [InlineData("Billing")]
        public async Task TimeEntries_PrivilegedRolesCanReadAnotherUser(string role)
        {
            var bob = await _factory.CreateLinkedUserAsync();
            var bobWork = await _factory.SeedWorkAsync(bob.AppUserId, Day);
            var viewer = await _factory.CreateLinkedUserAsync(role);
            var client = await _factory.LoginClientAsync(viewer.Email, viewer.Password);

            var entries = await ListAsync(await client.GetAsync(EntriesUrl(bob.AppUserId)));

            Assert.Equal(bobWork.EntryId, Assert.Single(entries).GetProperty("id").GetInt32());
        }

        [Fact]
        public async Task TimeEntries_OutsideTheWindowAreNotReturned()
        {
            var dev = await _factory.CreateLinkedUserAsync();
            await _factory.SeedWorkAsync(dev.AppUserId, Day.AddDays(10));
            var client = await _factory.LoginClientAsync(dev.Email, dev.Password);

            Assert.Empty(await ListAsync(await client.GetAsync(EntriesUrl())));
        }

        [Theory]
        [InlineData("")]
        [InlineData("?from=2026-09-28T00:00:00")]
        [InlineData("?from=2026-09-29T00:00:00&to=2026-09-28T00:00:00")]
        [InlineData("?from=2026-01-01T00:00:00&to=2026-09-28T00:00:00")]
        [InlineData("?from=not-a-date&to=2026-09-28T00:00:00")]
        public async Task TimeEntries_RejectsInvalidRanges(string query)
        {
            var dev = await _factory.CreateLinkedUserAsync();
            var client = await _factory.LoginClientAsync(dev.Email, dev.Password);

            var response = await client.GetAsync("/api/v1/timeentries" + query);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task UserScopedEndpoints_RejectALoginWithoutAProfile()
        {
            var (email, password) = await _factory.CreateUserAsync();
            var client = await _factory.LoginClientAsync(email, password);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/tasks")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(EntriesUrl())).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/companies")).StatusCode);
        }
    }
}
