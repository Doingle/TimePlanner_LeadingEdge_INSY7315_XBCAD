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
            if (result.Succeeded)
            {
                await _audit.LogAsync(AuditActions.LoginSucceeded, "website", model.Email, user!.Id);
                //only local urls are followed, an open redirect would let an attacker bounce users to another site
                return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction("Index", "Home");
            }

            _logger.LogWarning("Failed login attempt (locked out: {LockedOut})", result.IsLockedOut);
            await _audit.LogAsync(result.IsLockedOut ? AuditActions.LoginLockedOut : AuditActions.LoginFailed, "website", model.Email);
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            model.Password = string.Empty;
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
