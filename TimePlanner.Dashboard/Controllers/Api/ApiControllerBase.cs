using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimePlanner.Dashboard.Security;

namespace TimePlanner.Dashboard.Controllers.Api
{
    //-----------------------------
    //shared rules for every data endpoint: a valid bearer token is required, and failures come back as json problem details
    [ApiController]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [Route("api/v1/[controller]")]
    [Produces("application/json")]
    public abstract class ApiControllerBase : ControllerBase
    {
        //the time tracking profile of the caller, null for a login that is not linked to one
        protected int? CurrentAppUserId => User.ProfileId();

        //admins may read other people's time data, developers only their own
        protected bool IsPrivileged => User.IsInRole("Admin");

        protected ObjectResult NoProfile() =>
            Problem(title: "This account is not linked to a time tracking profile.", statusCode: StatusCodes.Status403Forbidden);

        protected ObjectResult NotAllowed() =>
            Problem(title: "You do not have access to this data.", statusCode: StatusCodes.Status403Forbidden);
    }
}
//------------------------------EOF-----------------------------\\
