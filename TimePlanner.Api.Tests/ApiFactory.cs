using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
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
