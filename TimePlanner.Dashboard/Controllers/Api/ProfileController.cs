using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Security;
using TimePlanner.Dashboard.Services.Users;

namespace TimePlanner.Dashboard.Controllers.Api
{
    //-----------------------------
    //the signed in person's own account: who they are, their display name and their password
    public class ProfileController : ApiControllerBase
    {
        private readonly UserManager<ApplicationUser> _users;
        private readonly AccountService _account;

        public ProfileController(UserManager<ApplicationUser> users, AccountService account)
        {
            _users = users;
            _account = account;
        }

        public record NameRequest([Required, StringLength(UserAdminService.MaxNameLength)] string Name);

        public record PasswordRequest([Required, StringLength(128)] string CurrentPassword, [Required, StringLength(128)] string NewPassword);

        private Task<ApplicationUser?> CurrentAsync() => _users.FindByIdAsync(User.IdentityId() ?? string.Empty);

        [HttpGet]
        public async Task<ActionResult<AccountInfo>> Get() => await _account.GetAsync((await CurrentAsync())!);

        [HttpPut("name")]
        public async Task<IActionResult> UpdateName(NameRequest request)
        {
            var result = await _account.UpdateNameAsync((await CurrentAsync())!, request.Name);
            return result.Success ? NoContent() : Problem(title: result.Error, statusCode: StatusCodes.Status400BadRequest);
        }

        //-----------------------------
        //needs the current password. Success ends every other session and token the person has, this one included, so they sign in again with the new password.
        //limited like signing in is, because a wrong current password is a password guess
        [HttpPost("password")]
        [EnableRateLimiting(SecurityExtensions.LoginLimiter)]
        public async Task<IActionResult> ChangePassword(PasswordRequest request)
        {
            var result = await _account.ChangePasswordAsync((await CurrentAsync())!, request.CurrentPassword, request.NewPassword);
            return result.Success ? NoContent() : Problem(title: result.Error, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
//------------------------------EOF-----------------------------\\
