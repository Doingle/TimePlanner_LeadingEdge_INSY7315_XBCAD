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
    //the signed in person's own account settings: display name and password, and how text looks (accessibility). Tracking settings live in the widget, not here
    public class SettingsController : Controller
    {
        private readonly UserManager<ApplicationUser> _users;
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly AccountService _account;
        private readonly TwoFactorService _twoFactor;

        public SettingsController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, AccountService account, TwoFactorService twoFactor)
        {
            _users = users;
            _signIn = signIn;
            _account = account;
            _twoFactor = twoFactor;
        }

        [HttpGet]
        public async Task<IActionResult> Index() => View(await PageAsync());

        //-----------------------------
        //text size, bold and italic for the whole site. The choice is kept in this browser (see TextPreferences), so nothing is posted here.
        //it lives under /Settings so someone still on a temporary password can make text easier to read before choosing their own
        [HttpGet]
        public IActionResult Accessibility() => View(TextPreferences.From(Request));

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

        //-----------------------------
        //whether the second sign in step is on, with what is needed to turn it on or off
        [HttpGet]
        public async Task<IActionResult> TwoFactor() => View(await TwoFactorPageAsync());

        //-----------------------------
        //creates the key to add to an authenticator app. It is a post, so opening the page never creates anything
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TwoFactorStart()
        {
            var user = (await CurrentAsync())!;
            var setup = await _twoFactor.BeginSetupAsync(user);
            if (setup == null)
                return RedirectToAction(nameof(TwoFactor));

            //making a new key replaces the security stamp, which would end this very session on the next click, so it is signed in again with a fresh cookie
            await _signIn.RefreshSignInAsync(user);
            var page = await TwoFactorPageAsync();
            page.Setup = setup;
            return View(nameof(TwoFactor), page);
        }

        //-----------------------------
        //turns two-factor on once a code from the app is right, and shows the recovery codes this one time. That is the response itself, not a redirect,
        //so the codes are never kept anywhere. Limited like signing in is, because a code is a guess
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(SecurityExtensions.LoginLimiter)]
        public async Task<IActionResult> TwoFactorEnable(string? code)
        {
            var user = (await CurrentAsync())!;
            var result = await _twoFactor.EnableAsync(user, code);

            if (!result.Success)
            {
                var failed = await TwoFactorPageAsync();
                failed.Setup = await _twoFactor.BeginSetupAsync(user);
                failed.Error = result.Error;
                return View(nameof(TwoFactor), failed);
            }

            //turning it on replaces the security stamp, which ends every other session, so this one is signed in again with a fresh cookie
            await _signIn.RefreshSignInAsync(user);
            var page = await TwoFactorPageAsync();
            page.RecoveryCodes = result.RecoveryCodes;
            return View(nameof(TwoFactor), page);
        }

        //-----------------------------
        //turns it off again, which needs the password and a code so a stolen session alone cannot do it
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(SecurityExtensions.LoginLimiter)]
        public async Task<IActionResult> TwoFactorDisable(string? password, string? code)
        {
            var user = (await CurrentAsync())!;
            var result = await _twoFactor.DisableAsync(user, password, code);

            if (!result.Success)
            {
                var page = await TwoFactorPageAsync();
                page.Error = result.Error;
                return View(nameof(TwoFactor), page);
            }

            await _signIn.RefreshSignInAsync(user);
            TempData["Message"] = "Two-factor sign in is off.";
            return RedirectToAction(nameof(TwoFactor));
        }

        private async Task<TwoFactorPageModel> TwoFactorPageAsync()
        {
            var user = (await CurrentAsync())!;
            var enabled = await _users.GetTwoFactorEnabledAsync(user);
            return new TwoFactorPageModel
            {
                Enabled = enabled,
                RecoveryCodesLeft = enabled ? await _twoFactor.RecoveryCodesLeftAsync(user) : 0,
                Message = TempData["Message"] as string
            };
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
