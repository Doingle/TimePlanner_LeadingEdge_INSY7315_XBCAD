using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace TimePlanner.Api.Tests
{
    //-----------------------------
    //the dashboard has two roles. A third one used to exist and must not come back by accident
    public class RolesTests
    {
        [Fact]
        public async Task TheDashboard_HasOnlyTheDeveloperAndAdminRoles()
        {
            using var factory = new ApiFactory();
            factory.NewClient();
            await using var scope = factory.Services.CreateAsyncScope();

            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>().Roles.Select(r => r.Name).ToList();

            Assert.Equal(new[] { "Admin", "Developer" }, roles.OrderBy(r => r).ToArray());
        }

        [Fact]
        public async Task TheSeededAdministrator_IsAnAdmin()
        {
            using var factory = new ApiFactory();
            var admin = await factory.LoginClientAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

            var me = await admin.GetStringAsync("/api/v1/auth/me");

            Assert.Contains("Admin", me);
        }
    }
}
//------------------------------EOF-----------------------------\\
