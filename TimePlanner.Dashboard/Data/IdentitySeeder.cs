using Microsoft.AspNetCore.Identity;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Dashboard.Data
{
    //-----------------------------
    //creates the login roles and the first admin account on startup, safe to run on every launch
    public static class IdentitySeeder
    {
        //-----------------------------
        //roles mirror the UserRole enum. The admin only exists if Seed:AdminEmail and Seed:AdminPassword are configured
        //(user-secrets locally, environment variables when hosted), so no credential ever lives in the repo
        public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
        {
            var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in Enum.GetNames<UserRole>())
            {
                if (!await roles.RoleExistsAsync(role))
                    await roles.CreateAsync(new IdentityRole(role));
            }

            var email = config["Seed:AdminEmail"];
            var password = config["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                return;

            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            if (await users.FindByEmailAsync(email) != null)
                return;

            //the domain AppUser holds the time tracking data, the ApplicationUser holds the login and points at it
            var appUsers = services.GetRequiredService<IAppUserRepository>();
            var domainUser = await appUsers.GetByEmailAsync(email);
            if (domainUser == null)
            {
                domainUser = new AppUser { Name = "Administrator", Email = email, Role = UserRole.Admin };
                await appUsers.AddAsync(domainUser);
            }

            var account = new ApplicationUser { UserName = email, Email = email, AppUserId = domainUser.UserId };
            var created = await users.CreateAsync(account, password);
            if (!created.Succeeded)
                throw new InvalidOperationException("Seeding the admin failed: " + string.Join("; ", created.Errors.Select(e => e.Code)));

            await users.AddToRoleAsync(account, nameof(UserRole.Admin));
        }
    }
}
//------------------------------EOF-----------------------------\\
