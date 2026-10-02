using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Dashboard.Security
{
    //-----------------------------
    //a deactivated account can never sign in, on the website or the api, whatever password is typed.
    //Identity asks this before it looks at the password, so the answer cannot be used to guess whether a password was right
    public class AppSignInManager : SignInManager<ApplicationUser>
    {
        public AppSignInManager(UserManager<ApplicationUser> userManager, IHttpContextAccessor contextAccessor,
            IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory, IOptions<IdentityOptions> optionsAccessor,
            ILogger<SignInManager<ApplicationUser>> logger, IAuthenticationSchemeProvider schemes,
            IUserConfirmation<ApplicationUser> confirmation)
            : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
        {
        }

        public override async Task<bool> CanSignInAsync(ApplicationUser user) => user.IsActive && await base.CanSignInAsync(user);
    }

    //-----------------------------
    //adds a claim to the signed in user while they still have a temporary password, so the pages can send them to choose a new one
    public class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>
    {
        public const string MustChangePasswordClaim = "must_change_password";

        public AppClaimsPrincipalFactory(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IOptions<IdentityOptions> options)
            : base(userManager, roleManager, options)
        {
        }

        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
        {
            var identity = await base.GenerateClaimsAsync(user);
            if (user.MustChangePassword)
                identity.AddClaim(new Claim(MustChangePasswordClaim, "1"));
            return identity;
        }
    }
}
//------------------------------EOF-----------------------------\\
