using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimePlanner.Core.Sync;

namespace TimePlanner.Dashboard.Controllers.Api;

[ApiController]
[Route("api/v1/widget/version")]
public sealed class WidgetVersionController(IConfiguration configuration) : ControllerBase
{
    // Public release metadata, also available before dashboard sign-in. No database access.
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<WidgetRelease> Get()
    {
        var version = configuration["WidgetUpdate:LatestVersion"];
        return new WidgetRelease(WidgetRelease.Parse(version) == null ? null : version!.Trim());
    }
}
