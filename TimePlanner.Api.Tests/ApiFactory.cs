using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;
using TimePlanner.Core.Services;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //runs the real Dashboard in memory against a throwaway sqlite file with test only credentials and signing key.
    //Settings are added as the last configuration source so they outrank everything else, and the Testing environment never loads user-secrets,
    //so a developer's own configuration can never leak into a test run. Every instance has its own settings, so a test can start a differently configured host
    public class ApiFactory : WebApplicationFactory<Program>
    {
        public const string AdminEmail = "admin@test.local";
        public const string AdminPassword = "Test-Admin-Pass-1!";
        public const string JwtKey = "test-only-signing-key-that-is-long-enough-0123456789";

        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"timeplanner-apitests-{Guid.NewGuid():N}.db");

        //-----------------------------
        //the settings this host runs with. Rate limits are far above anything a test does, unless a test host lowers them
        protected virtual Dictionary<string, string?> Settings() => new()
        {
            ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = AdminPassword,
            ["Jwt:Key"] = JwtKey,
            ["RateLimiting:LoginPerMinute"] = "100000",
            ["RateLimiting:ImportPerMinute"] = "100000"
        };

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            //a published site serves the build's generated static files (the scoped css bundle). A host outside Development does not unless asked, and the
            //tests that check every file a page names should see the pages the way a visitor of the hosted site does
            builder.UseStaticWebAssets();
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(Settings()));
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
        //stores one entry of an exact length for a user, creating the company, project and task when new. Category ids are the seeded ones
        public async Task<Project> AddEntryAsync(int userId, string company, string project, int categoryId, DateTime start, int minutes, string? note = null)
        {
            using var scope = Services.CreateScope();
            var sp = scope.ServiceProvider;
            var entries = sp.GetRequiredService<EntryService>();

            var stored = await entries.AddProjectAsync(company, project, null);
            var task = await entries.FindOrCreateTaskAsync(userId, stored.ProjectID, categoryId);
            await sp.GetRequiredService<ITimeEntryRepository>().AddAsync(new TimeEntry
            {
                UserId = userId, TaskId = task.TaskID, StartTime = start, EndTime = start.AddMinutes(minutes), Note = note, Method = EntryMethod.Manual
            });
            return stored;
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

        //-----------------------------
        //every audit row written so far, oldest first
        public async Task<List<AuditEvent>> AuditEventsAsync()
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AuthDbContext>().AuditEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
        }

        //-----------------------------
        //the account's id and current security stamp, which a token must carry to be accepted
        public async Task<(string Id, string Stamp)> AccountStampAsync(string email)
        {
            using var scope = Services.CreateScope();
            var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!;
            return (user.Id, user.SecurityStamp!);
        }

        //-----------------------------
        //gives an account a password of the test's choosing and clears its temporary flag, standing in for the person having signed in and changed it
        public async Task SetPasswordAsync(string email, string password)
        {
            using var scope = Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            Assert.True((await users.ResetPasswordAsync(user, await users.GeneratePasswordResetTokenAsync(user), password)).Succeeded);
            user.MustChangePassword = false;
            await users.UpdateAsync(user);
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
            //only this host's database is released
            //clearing every pool closed connections other test hosts were using
            using (var own = new SqliteConnection($"Data Source={_dbPath}"))
            {
                SqliteConnection.ClearPool(own);
            }
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
