using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimePlanner.Dashboard.Security;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Services;

namespace TimePlanner.Dashboard.Controllers.Api
{
    //-----------------------------
    //token based login for the widget and other api clients, the dashboard pages use cookies instead
    [ApiController]
    [Route("api/v1/auth")]
    public class AuthApiController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _users;
        private readonly SignInManager<ApplicationUser> _signIn;
        private readonly JwtTokenService _tokens;
        private readonly AuditLogger _audit;
        private readonly ILogger<AuthApiController> _logger;

        public AuthApiController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
            JwtTokenService tokens, AuditLogger audit, ILogger<AuthApiController> logger)
        {
            _users = users;
            _signIn = signIn;
            _tokens = tokens;
            _audit = audit;
            _logger = logger;
        }

        public record LoginRequest(
            [Required, EmailAddress, StringLength(256)] string Email,
            [Required, StringLength(128)] string Password);

        public record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc);

        //-----------------------------
        //every failure returns the same 401 whether the email is unknown, the password is wrong or the account is locked out,
        //and failures count towards the same lockout policy as the website
        //an unknown email still pays for a password hash, so the response time does not reveal which emails have accounts
        [AllowAnonymous]
        [HttpPost("login")]
        [EnableRateLimiting(SecurityExtensions.LoginLimiter)]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            var user = await _users.FindByEmailAsync(request.Email);
            if (user == null)
                _users.PasswordHasher.HashPassword(new ApplicationUser(), request.Password);
            var result = user == null
                ? Microsoft.AspNetCore.Identity.SignInResult.Failed
                : await _signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

            if (!result.Succeeded)
            {
                _logger.LogWarning("Failed api login attempt (locked out: {LockedOut})", result.IsLockedOut);
                await _audit.LogAsync(result.IsLockedOut ? AuditActions.LoginLockedOut : result.IsNotAllowed ? AuditActions.LoginBlocked : AuditActions.LoginFailed, "api", request.Email);
                return Problem(title: "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);
            }

            //a temporary password is only good for choosing a real one on the dashboard
            if (user!.MustChangePassword)
            {
                await _audit.LogAsync(AuditActions.LoginBlocked, "api, password change required", request.Email, user.Id);
                return Problem(title: "Choose a new password on the dashboard before using the api.", statusCode: StatusCodes.Status403Forbidden);
            }

            await _audit.LogAsync(AuditActions.LoginSucceeded, "api", request.Email, user.Id);
            var (token, expires) = _tokens.Create(user!, await _users.GetRolesAsync(user!));
            return Ok(new TokenResponse(token, "Bearer", expires));
        }

        //-----------------------------
        //returns who the bearer token belongs to, lets a client check that its token still works
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpGet("me")]
        public IActionResult Me()
        {
            return Ok(new
            {
                Email = User.FindFirst("email")?.Value,
                AppUserId = User.FindFirst("uid")?.Value,
                Roles = User.FindAll("role").Select(c => c.Value)
            });
        }
    }
}
//------------------------------EOF-----------------------------\\
