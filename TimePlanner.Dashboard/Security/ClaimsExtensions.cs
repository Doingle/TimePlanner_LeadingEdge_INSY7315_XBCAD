using System.Security.Claims;

namespace TimePlanner.Dashboard.Security
{
    public static class ClaimsExtensions
    {
        //the claim that links a signed in person to their time tracking profile, in the website cookie and in api tokens alike
        public const string ProfileClaim = "uid";

        //-----------------------------
        //the time tracking profile id of the signed in person, null for a login that is not linked to one
        public static int? ProfileId(this ClaimsPrincipal user) =>
            int.TryParse(user.FindFirst(ProfileClaim)?.Value, out var id) ? id : null;

        //-----------------------------
        //the login's id, whichever way the person signed in: the website cookie stores it as the name identifier, an api token as "sub"
        public static string? IdentityId(this ClaimsPrincipal user) =>
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
    }
}
//------------------------------EOF-----------------------------\\
