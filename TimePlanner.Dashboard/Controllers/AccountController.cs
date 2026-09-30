using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Models;

namespace TimePlanner.Dashboard.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<AccountController> _logger;

        public AccountController(SignInManager<ApplicationUser> signInManager, ILogger<AccountController> logger)
        {
            _signInManager = signInManager;
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
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                //only local urls are followed, an open redirect would let an attacker bounce users to another site
                return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction("Index", "Home");
            }

            _logger.LogWarning("Failed login attempt (locked out: {LockedOut})", result.IsLockedOut);
            ModelState.AddModelError(string.Empty, "Invalid email or password.");
            model.Password = string.Empty;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
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
