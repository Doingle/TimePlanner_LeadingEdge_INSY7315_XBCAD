using Microsoft.AspNetCore.Mvc;
using TimePlanner.Dashboard.Services.Overview;

namespace TimePlanner.Dashboard.Controllers.Api
{
    public class OverviewController : ApiControllerBase
    {
        private readonly OverviewService _overview;

        public OverviewController(OverviewService overview) => _overview = overview;

        //-----------------------------
        //the signed in person's home screen: today's timeline, hours and goal progress, last submission, and the breakdown of today (the default) or the week
        [HttpGet]
        public async Task<ActionResult<MyOverview>> Get([FromQuery] string? period)
        {
            if (!OverviewService.IsValidPeriod(period))
                return ValidationProblem(new ValidationProblemDetails { Title = "period must be today or week." });
            if (CurrentAppUserId is not int me)
                return NoProfile();

            return await _overview.GetAsync(me, period);
        }
    }
}
//------------------------------EOF-----------------------------\\
