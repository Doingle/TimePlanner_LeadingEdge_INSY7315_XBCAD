using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimePlanner.Dashboard.Security;
using TimePlanner.Dashboard.Services;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Models;

namespace TimePlanner.Dashboard.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _users;
        private readonly AuditLogger _audit;
        private readonly ILogger<AccountController> _logger;

        public AccountController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> users, AuditLogger audit, ILogger<AccountController> logger)
        {
            _signInManager = signInManager;
            _users = users;
            _audit = audit;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        //-----------------------------
        //failed attempts count towards the lockout policy, and every failure (wrong password, unknown email, locked account)
        //shows the same message so the form cannot be used to discover which emails have accounts
        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(SecurityExtensions.LoginLimiter)]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            //an unknown email still pays for a password hash, so the response time does not reveal which emails have accounts
            var user = await _users.FindByEmailAsync(model.Email);
            if (user == null)
                _users.PasswordHasher.HashPassword(new ApplicationUser(), model.Password);
            var result = user == null
                ? Microsoft.AspNetCore.Identity.SignInResult.Failed
                : await _signInManager.PasswordSignInAsync(user, model.Password, isPersistent: false, lockoutOnFailure: true);
            //the password was right but the account has two-factor on, so nobody is signed in until the code is checked
            if (result.RequiresTwoFactor)
                return RedirectToAction(nameof(TwoFactor), new { returnUrl = model.ReturnUrl });

            if (result.Succeeded)
            {
                await _audit.LogAsync(AuditActions.LoginSucceeded, "website", model.Email, user!.Id);
                return AfterSignIn(user, model.ReturnUrl);
            }

            _logger.LogWarning("Failed login attempt (locked out: {LockedOut})", result.IsLockedOut);
            await _audit.LogAsync(result.IsLockedOut ? AuditActions.LoginLockedOut : result.IsNotAllowed ? AuditActions.LoginBlocked : AuditActions.LoginFailed, "website", model.Email);
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            model.Password = string.Empty;
            return View(model);
        }

        //-----------------------------
        //a temporary password must be replaced before anything else, and only local urls are followed because an open redirect
        //would let an attacker bounce users to another site
        private IActionResult AfterSignIn(ApplicationUser user, string? returnUrl)
        {
            if (user.MustChangePassword)
                return RedirectToAction("Index", "Settings");

            return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction("Index", "Home");
        }

        //-----------------------------
        //the second step: only reachable right after a correct password, which is remembered for a few minutes in a cookie of its own
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> TwoFactor(string? returnUrl = null, bool recovery = false)
        {
            if (await _signInManager.GetTwoFactorAuthenticationUserAsync() == null)
                return RedirectToAction(nameof(Login));

            return View(new TwoFactorViewModel { ReturnUrl = returnUrl, UseRecoveryCode = recovery });
        }

        //-----------------------------
        //a wrong code counts towards the same lockout as a wrong password, and every failure shows the same message
        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(SecurityExtensions.LoginLimiter)]
        public async Task<IActionResult> TwoFactor(TwoFactorViewModel model)
        {
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null)
                return RedirectToAction(nameof(Login));
            if (!ModelState.IsValid)
                return View(model);

            var result = model.UseRecoveryCode
                ? await _signInManager.TwoFactorRecoveryCodeSignInAsync(model.Code)
                : await _signInManager.TwoFactorAuthenticatorSignInAsync(model.Code, isPersistent: false, rememberClient: false);

            if (result.Succeeded)
            {
                await _audit.LogAsync(AuditActions.LoginSucceeded, model.UseRecoveryCode ? "website, two-factor recovery code" : "website, two-factor", user.Email, user.Id);
                if (model.UseRecoveryCode)
                    await _audit.LogAsync(AuditActions.RecoveryCodeUsed, "website", user.Email, user.Id);
                return AfterSignIn(user, model.ReturnUrl);
            }

            _logger.LogWarning("Failed two-factor code (locked out: {LockedOut})", result.IsLockedOut);
            await _audit.LogAsync(result.IsLockedOut ? AuditActions.LoginLockedOut : AuditActions.TwoFactorFailed, "website", user.Email, user.Id);
            ModelState.AddModelError(string.Empty, "That code is not right.");
            model.Code = string.Empty;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _audit.LogAsync(AuditActions.Logout, "website");
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }
    }
}
