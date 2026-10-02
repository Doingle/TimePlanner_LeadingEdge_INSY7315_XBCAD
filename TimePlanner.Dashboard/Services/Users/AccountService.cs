using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TimePlanner.Core.Data;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Dashboard.Services.Users
{
    //-----------------------------
    //what a person can do to their own account: see it, change their display name, change their password
    public class AccountService
    {
        private readonly UserManager<ApplicationUser> _users;
        private readonly AppDbContext _app;
        private readonly AuditLogger _audit;

        public AccountService(UserManager<ApplicationUser> users, AppDbContext app, AuditLogger audit)
        {
            _users = users;
            _app = app;
            _audit = audit;
        }

        public async Task<AccountInfo> GetAsync(ApplicationUser user)
        {
            var profile = user.AppUserId == null ? null : await _app.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == user.AppUserId);
            return new AccountInfo(profile?.Name ?? user.Email!, user.Email ?? "", (await _users.GetRolesAsync(user)).FirstOrDefault() ?? "", user.MustChangePassword);
        }

        //-----------------------------
        //the name shown in reports and lists, the sign in email does not change
        public async Task<OpResult> UpdateNameAsync(ApplicationUser user, string? name)
        {
            var clean = name?.Trim() ?? string.Empty;
            if (clean.Length == 0 || clean.Length > UserAdminService.MaxNameLength || clean.Any(char.IsControl))
                return OpResult.Fail($"Name must be 1 to {UserAdminService.MaxNameLength} characters.");

            var profile = user.AppUserId == null ? null : await _app.Users.FirstOrDefaultAsync(u => u.UserId == user.AppUserId);
            if (profile == null)
                return OpResult.Fail("Your account is not linked to a time tracking profile, ask an administrator.");

            profile.Name = clean;
            await _app.SaveChangesAsync();
            await _audit.LogAsync(AuditActions.ProfileUpdated, "name changed");
            return OpResult.Ok();
        }

        //-----------------------------
        //needs the current password, so a stolen session alone cannot take over the account. A wrong guess counts towards the account lockout.
        //success replaces the security stamp, which signs the person out everywhere else, and ends the temporary password state
        public async Task<OpResult> ChangePasswordAsync(ApplicationUser user, string? current, string? next)
        {
            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(next))
                return OpResult.Fail("Enter your current and your new password.");
            if (await _users.IsLockedOutAsync(user))
                return OpResult.Fail("Too many wrong attempts, try again later.");
            if (!await _users.CheckPasswordAsync(user, current))
            {
                await _users.AccessFailedAsync(user);
                return OpResult.Fail("Your current password is not correct.");
            }
            if (current == next)
                return OpResult.Fail("Choose a password that is different from your current one.");

            var changed = await _users.ChangePasswordAsync(user, current, next);
            if (!changed.Succeeded)
                return OpResult.Fail(string.Join(" ", changed.Errors.Select(e => e.Description)));

            await _users.ResetAccessFailedCountAsync(user);
            user.MustChangePassword = false;
            await _users.UpdateAsync(user);
            await _audit.LogAsync(AuditActions.PasswordChanged, user.Email);
            return OpResult.Ok();
        }
    }
}
//------------------------------EOF-----------------------------\\
