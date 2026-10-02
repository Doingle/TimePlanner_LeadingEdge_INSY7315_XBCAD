using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Models;
using TimePlanner.Dashboard.Security;
using TimePlanner.Dashboard.Services.Users;

namespace TimePlanner.Dashboard.Controllers
{
    //-----------------------------
    //the signed in person's own account settings: display name and password. Tracking settings live in the widget, not here
    public class SettingsController : Controller
    {
        private readonly UserManager<ApplicationUser> _users;
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly AccountService _account;

        public SettingsController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, AccountService account)
        {
            _users = users;
            _signIn = signIn;
            _account = account;
        }

        [HttpGet]
        public async Task<IActionResult> Index() => View(await PageAsync());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Name(string? name)
        {
            var result = await _account.UpdateNameAsync((await CurrentAsync())!, name);
            return await RespondAsync(result, "Your name was updated.");
        }

        //-----------------------------
        //success replaces the security stamp, which ends every other session, so this one is signed in again with a fresh cookie.
        //limited like signing in is, because a wrong current password is a password guess
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(SecurityExtensions.LoginLimiter)]
        public async Task<IActionResult> Password(string? currentPassword, string? newPassword, string? confirmPassword)
        {
            if (newPassword != confirmPassword)
                return await RespondAsync(OpResult.Fail("The new password and its confirmation do not match."), "");

            var user = (await CurrentAsync())!;
            var result = await _account.ChangePasswordAsync(user, currentPassword, newPassword);
            if (result.Success)
                await _signIn.RefreshSignInAsync(user);
            return await RespondAsync(result, "Your password was changed.");
        }

        private Task<ApplicationUser?> CurrentAsync() => _users.FindByIdAsync(User.IdentityId() ?? string.Empty);

        //a success goes back to the page with a message, a failure shows the page again with the reason
        private async Task<IActionResult> RespondAsync(OpResult result, string success)
        {
            if (result.Success)
            {
                TempData["Message"] = success;
                return RedirectToAction(nameof(Index));
            }
            var page = await PageAsync();
            page.Error = result.Error;
            return View(nameof(Index), page);
        }

        private async Task<SettingsPageModel> PageAsync() => new()
        {
            Account = await _account.GetAsync((await CurrentAsync())!),
            Message = TempData["Message"] as string
        };
    }
}
