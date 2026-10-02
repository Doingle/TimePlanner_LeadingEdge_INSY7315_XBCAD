using System.Security.Claims;

namespace TimePlanner.Dashboard.Security
{
    public static class ClaimsExtensions
    {
        //-----------------------------
        //the login's id, whichever way the person signed in: the website cookie stores it as the name identifier, an api token as "sub"
        public static string? IdentityId(this ClaimsPrincipal user) =>
            user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
    }
}
//------------------------------EOF-----------------------------\\
