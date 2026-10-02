using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Core.Domain.Entities;
using TimePlanner.Core.Domain.Enums;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Security;

namespace TimePlanner.Dashboard.Services.Users
{
    //-----------------------------
    //what an administrator can do to accounts: list, create, reset a password, deactivate and reactivate.
    //callers must already have checked the caller is an admin, every action is audited and none of them ever writes a password to a log.
    //a login (AuthDbContext) and its time tracking profile (AppDbContext) live in different databases contexts, so creating one is not a single transaction:
    //if the login cannot be created the profile stays and is simply reused the next time the same email is used
    public class UserAdminService
    {
        public const int MaxNameLength = 100;

        //the one message that means "no such account", so callers can answer with a 404
        public const string NotFound = "That account does not exist.";

        private readonly UserManager<ApplicationUser> _users;
        private readonly AuthDbContext _auth;
        private readonly AppDbContext _app;
        private readonly AuditLogger _audit;
        private readonly IHttpContextAccessor _http;

        public UserAdminService(UserManager<ApplicationUser> users, AuthDbContext auth, AppDbContext app, AuditLogger audit, IHttpContextAccessor http)
        {
            _users = users;
            _auth = auth;
            _app = app;
            _audit = audit;
            _http = http;
        }

        //-----------------------------
        //everyone, with their role, profile name and last successful sign in
        public async Task<List<UserSummary>> ListAsync()
        {
            var accounts = await _auth.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();
            var roles = await (from ur in _auth.UserRoles join r in _auth.Roles on ur.RoleId equals r.Id select new { ur.UserId, r.Name }).ToListAsync();
            var names = (await _app.Users.AsNoTracking().Select(u => new { u.UserId, u.Name }).ToListAsync()).ToDictionary(u => u.UserId, u => u.Name);
            var logins = (await _auth.AuditEvents.AsNoTracking()
                    .Where(e => e.Action == AuditActions.LoginSucceeded && e.Email != null)
                    .GroupBy(e => e.Email!)
                    .Select(g => new { Email = g.Key, At = g.Max(x => x.TimestampUtc) })
                    .ToListAsync())
                .GroupBy(l => l.Email.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.Max(x => x.At));

            return accounts.Select(a => Summary(a,
                roles.FirstOrDefault(r => r.UserId == a.Id)?.Name ?? "",
                a.AppUserId != null && names.TryGetValue(a.AppUserId.Value, out var name) ? name : a.Email!,
                logins.TryGetValue((a.Email ?? "").ToLowerInvariant(), out var at) ? at : null)).ToList();
        }

        //-----------------------------
        //creates the profile and the login together, with a random temporary password the person must replace at their first sign in
        public async Task<PasswordResult> CreateAsync(string? name, string? email, string? role)
        {
            var cleanName = name?.Trim() ?? string.Empty;
            if (cleanName.Length == 0 || cleanName.Length > MaxNameLength || cleanName.Any(char.IsControl))
                return PasswordResult.Fail($"Name must be 1 to {MaxNameLength} characters.");

            var cleanEmail = email?.Trim() ?? string.Empty;
            if (cleanEmail.Length is 0 or > 256 || !new EmailAddressAttribute().IsValid(cleanEmail))
                return PasswordResult.Fail("Enter a valid email address.");

            if (int.TryParse(role, out _) || !Enum.TryParse<UserRole>(role?.Trim(), ignoreCase: true, out var parsedRole) || !Enum.IsDefined(parsedRole))
                return PasswordResult.Fail("Role must be Developer or Admin.");

            if (await _users.FindByEmailAsync(cleanEmail) != null)
                return PasswordResult.Fail("An account with that email already exists.");

            var lower = cleanEmail.ToLowerInvariant();
            var profile = await _app.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == lower);
            if (profile != null && await _auth.Users.AnyAsync(a => a.AppUserId == profile.UserId))
                return PasswordResult.Fail("That email already belongs to another account.");

            if (profile == null)
            {
                profile = new AppUser { Name = cleanName, Email = cleanEmail, Role = parsedRole };
                _app.Users.Add(profile);
            }
            else
            {
                profile.Name = cleanName;
                profile.Role = parsedRole;
            }
            await _app.SaveChangesAsync();

            var temporary = PasswordGenerator.Create();
            var account = new ApplicationUser { UserName = cleanEmail, Email = cleanEmail, AppUserId = profile.UserId, MustChangePassword = true };
            var created = await _users.CreateAsync(account, temporary);
            if (!created.Succeeded)
                return PasswordResult.Fail(created.Errors.First().Description);
            await _users.AddToRoleAsync(account, parsedRole.ToString());

            await _audit.LogAsync(AuditActions.UserCreated, $"{cleanEmail} as {parsedRole}");
            return new PasswordResult(true, null, Summary(account, parsedRole.ToString(), cleanName, null), temporary);
        }

        //-----------------------------
        //replaces the password with a new random one, signs the person out everywhere and clears any lockout. They must choose their own at the next sign in
        public async Task<PasswordResult> ResetPasswordAsync(string id)
        {
            var user = await _users.FindByIdAsync(id);
            if (user == null)
                return PasswordResult.Fail(NotFound);

            var temporary = PasswordGenerator.Create();
            var reset = await _users.ResetPasswordAsync(user, await _users.GeneratePasswordResetTokenAsync(user), temporary);
            if (!reset.Succeeded)
                return PasswordResult.Fail(reset.Errors.First().Description);

            user.MustChangePassword = true;
            await _users.UpdateAsync(user);
            await _users.ResetAccessFailedCountAsync(user);
            await _users.SetLockoutEndDateAsync(user, null);

            await _audit.LogAsync(AuditActions.PasswordReset, user.Email);
            return new PasswordResult(true, null, await SummaryAsync(user), temporary);
        }

        //-----------------------------
        //deactivating keeps the person and all their hours but stops every way in: they cannot sign in and any open session or token stops working at once.
        //an admin cannot lock themselves out, and the last active admin cannot be deactivated
        public async Task<OpResult> SetActiveAsync(string id, bool active)
        {
            var user = await _users.FindByIdAsync(id);
            if (user == null)
                return OpResult.Fail(NotFound);
            if (user.IsActive == active)
                return OpResult.Ok();

            if (!active)
            {
                if (user.Id == CurrentIdentityId)
                    return OpResult.Fail("You cannot deactivate your own account.");
                if (await _users.IsInRoleAsync(user, nameof(UserRole.Admin)) && await ActiveAdminCountAsync() <= 1)
                    return OpResult.Fail("There must be at least one active administrator.");
            }

            user.IsActive = active;
            await _users.UpdateAsync(user);
            if (active)
            {
                await _users.ResetAccessFailedCountAsync(user);
                await _users.SetLockoutEndDateAsync(user, null);
            }
            else
            {
                // a new security stamp makes every existing cookie and token for this account invalid
                await _users.UpdateSecurityStampAsync(user);
            }

            await _audit.LogAsync(active ? AuditActions.UserReactivated : AuditActions.UserDeactivated, user.Email);
            return OpResult.Ok();
        }

        private string? CurrentIdentityId => _http.HttpContext?.User.IdentityId();

        private async Task<int> ActiveAdminCountAsync() =>
            (await _users.GetUsersInRoleAsync(nameof(UserRole.Admin))).Count(u => u.IsActive);

        private async Task<UserSummary> SummaryAsync(ApplicationUser user)
        {
            var profile = user.AppUserId == null ? null : await _app.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == user.AppUserId);
            return Summary(user, (await _users.GetRolesAsync(user)).FirstOrDefault() ?? "", profile?.Name ?? user.Email!, null);
        }

        private static UserSummary Summary(ApplicationUser a, string role, string name, DateTime? lastLogin) =>
            new(a.Id, a.AppUserId, name, a.Email ?? "", role, a.IsActive, a.MustChangePassword,
                a.LockoutEnd != null && a.LockoutEnd > DateTimeOffset.UtcNow, lastLogin);
    }
}
//------------------------------EOF-----------------------------\\
