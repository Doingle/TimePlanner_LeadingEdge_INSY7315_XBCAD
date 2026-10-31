using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimePlanner.Dashboard.Security;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Services;
using TimePlanner.Dashboard.Services.Users;

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
        private readonly RefreshTokenService _refresh;
        private readonly AuditLogger _audit;
        private readonly TwoFactorService _twoFactor;
        private readonly ILogger<AuthApiController> _logger;

        public AuthApiController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
            JwtTokenService tokens, RefreshTokenService refresh, AuditLogger audit, TwoFactorService twoFactor, ILogger<AuthApiController> logger)
        {
            _users = users;
            _signIn = signIn;
            _tokens = tokens;
            _refresh = refresh;
            _audit = audit;
            _twoFactor = twoFactor;
            _logger = logger;
        }

        //otp is the six digit code from the authenticator app, or a recovery code. It is only needed for accounts that turned two-factor on
        public record LoginRequest(
            [Required, EmailAddress, StringLength(256)] string Email,
            [Required, StringLength(128)] string Password,
            [StringLength(32)] string? Otp = null);

        public record RefreshRequest([Required, StringLength(200)] string RefreshToken);

        //the refresh token is what keeps a client signed in, the access token only lasts minutes
        public record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc, string RefreshToken, DateTime RefreshExpiresAtUtc);

        public record MeResponse(string? Email, string? AppUserId, IEnumerable<string> Roles);

        //-----------------------------
        //every failure returns the same 401 whether the email is unknown, the password is wrong or the account is locked out,
        //and failures count towards the same lockout policy as the website
        //an unknown email still pays for a password hash, so the response time does not reveal which emails have accounts
        [AllowAnonymous]
        [HttpPost("login")]
        [EnableRateLimiting(SecurityExtensions.LoginLimiter)]
        [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
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

            //an account with two-factor on needs the code before anything else is said about it. The answer carries twoFactorRequired so a client knows to ask,
            //and wrong codes count towards the same lockout as wrong passwords
            if (await _users.GetTwoFactorEnabledAsync(user!))
            {
                if (string.IsNullOrWhiteSpace(request.Otp))
                    return TwoFactorRequired("A two-factor code is required.");

                if (!await _twoFactor.CheckLoginCodeAsync(user!, request.Otp, "api"))
                {
                    var lockedOut = await _users.IsLockedOutAsync(user!);
                    _logger.LogWarning("Failed api two-factor code (locked out: {LockedOut})", lockedOut);
                    await _audit.LogAsync(lockedOut ? AuditActions.LoginLockedOut : AuditActions.TwoFactorFailed, "api", request.Email, user!.Id);
                    return lockedOut
                        ? Problem(title: "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized)
                        : TwoFactorRequired("The two-factor code is not right.");
                }
            }

            //a temporary password is only good for choosing a real one on the dashboard
            if (user!.MustChangePassword)
            {
                await _audit.LogAsync(AuditActions.LoginBlocked, "api, password change required", request.Email, user.Id);
                return Problem(title: "Choose a new password on the dashboard before using the api.", statusCode: StatusCodes.Status403Forbidden);
            }

            await _audit.LogAsync(AuditActions.LoginSucceeded, "api", request.Email, user.Id);
            var (token, expires) = _tokens.Create(user!, await _users.GetRolesAsync(user!));
            var (refresh, refreshExpires) = await _refresh.IssueAsync(user!);
            return Ok(new TokenResponse(token, "Bearer", expires, refresh, refreshExpires));
        }

        //-----------------------------
        //a 401 that tells the client a code is needed, so the widget can show its code box instead of "wrong password"
        private ObjectResult TwoFactorRequired(string title)
        {
            var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, StatusCodes.Status401Unauthorized, title);
            problem.Extensions["twoFactorRequired"] = true;
            return new ObjectResult(problem) { StatusCode = StatusCodes.Status401Unauthorized };
        }

        //-----------------------------
        //swaps a refresh token for a new access token and a new refresh token, the old one stops working.
        //every failure is the same 401 and the reason only goes to the audit log
        [AllowAnonymous]
        [HttpPost("refresh")]
        [EnableRateLimiting(SecurityExtensions.LoginLimiter)]
        [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> Refresh(RefreshRequest request)
        {
            var result = await _refresh.RotateAsync(request.RefreshToken);
            if (result.Outcome != RefreshTokenService.Outcome.Ok)
            {
                await _audit.LogAsync(result.Reason == "reuse" ? AuditActions.RefreshTokenReuse : AuditActions.RefreshFailed, "api, " + result.Reason);
                return Problem(title: "Sign in again.", statusCode: StatusCodes.Status401Unauthorized);
            }

            var (token, expires) = _tokens.Create(result.User!, await _users.GetRolesAsync(result.User!));
            return Ok(new TokenResponse(token, "Bearer", expires, result.Token!, result.ExpiresUtc!.Value));
        }

        //-----------------------------
        //ends the session a refresh token belongs to, so the client can sign out for real. Always 204, it does not say whether the token was known
        [AllowAnonymous]
        [HttpPost("logout")]
        [EnableRateLimiting(SecurityExtensions.LoginLimiter)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
        public async Task<IActionResult> Logout(RefreshRequest request)
        {
            var userId = await _refresh.RevokeAsync(request.RefreshToken);
            if (userId != null)
                await _audit.LogAsync(AuditActions.Logout, "api", userId: userId);
            return NoContent();
        }

        //-----------------------------
        //returns who the bearer token belongs to, lets a client check that its token still works
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpGet("me")]
        [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public IActionResult Me()
        {
            return Ok(new MeResponse(
                User.FindFirst("email")?.Value,
                User.FindFirst("uid")?.Value,
                User.FindAll("role").Select(c => c.Value)));
        }
    }
}
//------------------------------EOF-----------------------------\\
