using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimePlanner.Dashboard.Models;
using TimePlanner.Dashboard.Services.Users;

namespace TimePlanner.Dashboard.Controllers
{
    //-----------------------------
    //the admin page for accounts. Create and reset answer with the page itself because the temporary password is shown once and never kept
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly UserAdminService _admin;

        public UsersController(UserAdminService admin) => _admin = admin;

        [HttpGet]
        public async Task<IActionResult> Index() => View(await PageAsync());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string? name, string? email, string? role)
        {
            var result = await _admin.CreateAsync(name, email, role);
            var page = await PageAsync();
            if (result.Success)
            {
                page.Message = $"Account created for {result.User!.Email}.";
                page.TemporaryPassword = result.TemporaryPassword;
                page.TemporaryFor = result.User.Email;
            }
            else
            {
                page.Error = result.Error;
            }
            return View(nameof(Index), page);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string id)
        {
            var result = await _admin.ResetPasswordAsync(id);
            var page = await PageAsync();
            if (result.Success)
            {
                page.Message = $"Password reset for {result.User!.Email}.";
                page.TemporaryPassword = result.TemporaryPassword;
                page.TemporaryFor = result.User.Email;
            }
            else
            {
                page.Error = result.Error;
            }
            return View(nameof(Index), page);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Deactivate(string id) => ChangeActiveAsync(id, false, "Account deactivated.");

        [HttpPost]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Reactivate(string id) => ChangeActiveAsync(id, true, "Account reactivated.");

        private async Task<IActionResult> ChangeActiveAsync(string id, bool active, string success)
        {
            var result = await _admin.SetActiveAsync(id, active);
            TempData[result.Success ? "Message" : "Error"] = result.Success ? success : result.Error;
            return RedirectToAction(nameof(Index));
        }

        private async Task<UsersPageModel> PageAsync() => new()
        {
            Users = await _admin.ListAsync(),
            Message = TempData["Message"] as string,
            Error = TempData["Error"] as string
        };
    }
}
