using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //runs the real Dashboard in memory against a throwaway sqlite file with test only credentials and signing key.
    //Environment variables are used because they outrank every other configuration source, so a developer's user-secrets can never leak into a test run
    public class ApiFactory : WebApplicationFactory<Program>
    {
        public const string AdminEmail = "admin@test.local";
        public const string AdminPassword = "Test-Admin-Pass-1!";
        public const string JwtKey = "test-only-signing-key-that-is-long-enough-0123456789";

        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"timeplanner-apitests-{Guid.NewGuid():N}.db");

        public ApiFactory()
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", $"Data Source={_dbPath}");
            Environment.SetEnvironmentVariable("Seed__AdminEmail", AdminEmail);
            Environment.SetEnvironmentVariable("Seed__AdminPassword", AdminPassword);
            Environment.SetEnvironmentVariable("Jwt__Key", JwtKey);
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
        }

        //-----------------------------
        //https base address because the login cookie is marked Secure and would otherwise never be sent back. Redirects are left for the tests to inspect
        public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        //-----------------------------
        //creates a login with a unique email so a test can fail or lock it out without touching the seeded admin
        public async Task<(string Email, string Password)> CreateUserAsync()
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var email = $"user-{Guid.NewGuid():N}@test.local";
            var password = "Throwaway-Pass-9!x";
            var result = await users.CreateAsync(new ApplicationUser { UserName = email, Email = email }, password);
            Assert.True(result.Succeeded);
            return (email, password);
        }

        //-----------------------------
        //creates a login linked to a fresh time tracking profile with the given role, like a real developer or billing account
        public async Task<(string Email, string Password, int AppUserId)> CreateLinkedUserAsync(string role = "Developer")
        {
            using var scope = Services.CreateScope();
            var sp = scope.ServiceProvider;
            var email = $"{role.ToLower()}-{Guid.NewGuid():N}@test.local";
            var password = "Throwaway-Pass-9!x";

            var profile = new AppUser { Name = email, Email = email, Role = Enum.Parse<UserRole>(role) };
            await sp.GetRequiredService<IAppUserRepository>().AddAsync(profile);

            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var account = new ApplicationUser { UserName = email, Email = email, AppUserId = profile.UserId };
            Assert.True((await users.CreateAsync(account, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(account, role)).Succeeded);
            return (email, password, profile.UserId);
        }

        //-----------------------------
        //saves a company, an active project, a task for the user and one time entry on it, with names unique to this call
        public async Task<(int CompanyId, int ProjectId, int TaskId, int EntryId)> SeedWorkAsync(int userId, DateTime start, ProjectStatus status = ProjectStatus.Active)
        {
            using var scope = Services.CreateScope();
            var sp = scope.ServiceProvider;
            var tag = Guid.NewGuid().ToString("N")[..8];

            var company = new Company { Name = $"Client {tag}" };
            await sp.GetRequiredService<ICompanyRepository>().AddAsync(company);
            var project = new Project { Name = $"Project {tag}", CompanyId = company.CompanyId, Status = status };
            await sp.GetRequiredService<IProjectRepository>().AddAsync(project);

            var categoryId = (await sp.GetRequiredService<ICategoryRepository>().GetAllAsync()).First().CategoryId;
            var task = new WorkTask { Name = $"Task {tag}", ProjectID = project.ProjectID, CategoryId = categoryId, AssignedUserID = userId, Status = WorkTaskStatus.InProgress };
            await sp.GetRequiredService<IWorkTaskRepository>().AddAsync(task);

            var entry = new TimeEntry { UserId = userId, TaskId = task.TaskID, StartTime = start, EndTime = start.AddMinutes(45), Note = $"Note {tag}", Method = EntryMethod.Manual };
            await sp.GetRequiredService<ITimeEntryRepository>().AddAsync(entry);
            return (company.CompanyId, project.ProjectID, task.TaskID, entry.TimeEntryId);
        }

        //-----------------------------
        //logs in over the api and returns a client that sends the bearer token on every request
        public async Task<HttpClient> LoginClientAsync(string email, string password)
        {
            var client = NewClient();
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
            Assert.True(response.IsSuccessStatusCode, "test login failed");
            var token = (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("accessToken").GetString();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        public async Task<bool> IsLockedOutAsync(string email)
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            return await users.IsLockedOutAsync((await users.FindByEmailAsync(email))!);
        }

        //-----------------------------
        //loads the login page and returns the anti-forgery token from its form, the matching cookie is kept by the client
        public static async Task<string> GetAntiForgeryTokenAsync(HttpClient client)
        {
            var html = await client.GetStringAsync("/Account/Login");
            var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            Assert.True(match.Success, "login form has no anti-forgery token");
            return match.Groups[1].Value;
        }

        //-----------------------------
        //submits the login form the way a browser does
        public static async Task<HttpResponseMessage> PostLoginAsync(HttpClient client, string email, string password, string? returnUrl = null)
        {
            var token = await GetAntiForgeryTokenAsync(client);
            return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Email"] = email,
                ["Password"] = password,
                ["ReturnUrl"] = returnUrl ?? string.Empty,
                ["__RequestVerificationToken"] = token
            }));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
            {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }

    //-----------------------------
    //one shared host for every api test class. Classes in a collection run one after another, so they never fight over the sqlite file
    [CollectionDefinition("Api")]
    public class ApiCollection : ICollectionFixture<ApiFactory> { }
}
