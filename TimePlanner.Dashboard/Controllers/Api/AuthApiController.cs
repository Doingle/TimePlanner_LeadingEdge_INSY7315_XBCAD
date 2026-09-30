using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
        private readonly ILogger<AuthApiController> _logger;

        public AuthApiController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
            JwtTokenService tokens, ILogger<AuthApiController> logger)
        {
            _users = users;
            _signIn = signIn;
            _tokens = tokens;
            _logger = logger;
        }

        public record LoginRequest(
            [Required, EmailAddress, StringLength(256)] string Email,
            [Required, StringLength(128)] string Password);

        public record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc);

        //-----------------------------
        //every failure returns the same 401 whether the email is unknown, the password is wrong or the account is locked out,
        //and failures count towards the same lockout policy as the website
        //ponytail: an unknown email skips the password hash so it answers slightly faster, add a dummy hash if timing enumeration matters
        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            var user = await _users.FindByEmailAsync(request.Email);
            var result = user == null
                ? Microsoft.AspNetCore.Identity.SignInResult.Failed
                : await _signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

            if (!result.Succeeded)
            {
                _logger.LogWarning("Failed api login attempt (locked out: {LockedOut})", result.IsLockedOut);
                return Problem(title: "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);
            }

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
                Roles = User.FindAll("role").Select(c => c.Value)
            });
        }
    }
}
//------------------------------EOF-----------------------------\\
