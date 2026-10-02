using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimePlanner.Dashboard.Services;
using TimePlanner.Dashboard.Services.Overview;
using TimePlanner.Dashboard.Services.Reports;

namespace TimePlanner.Dashboard.Controllers.Api
{
    //-----------------------------
    //the team view, for administrators only: overview numbers, who has submitted which day, and exports.
    //every endpoint takes view (week, the default, or month) and a date inside the period
    [Authorize(Roles = "Admin")]
    public class AdministrationController : ApiControllerBase
    {
        private readonly AdminService _admin;
        private readonly CompanyClock _clock;

        public AdministrationController(AdminService admin, CompanyClock clock)
        {
            _admin = admin;
            _clock = clock;
        }

        //team hours, hours by category, project and person, and the submission rate for today and for the period
        [HttpGet("overview")]
        public async Task<ActionResult<AdminOverview>> Overview([FromQuery] string? view, [FromQuery] DateTime? date)
        {
            if (!Period.TryResolve(view, date, _clock.Today, out var period))
                return ValidationProblem(new ValidationProblemDetails { Title = Period.InvalidViewMessage });
            return await _admin.OverviewAsync(period);
        }

        //each person against each working day, marked Submitted, Pending, Missing or NotDue
        [HttpGet("submissions")]
        public async Task<ActionResult<SubmissionGrid>> Submissions([FromQuery] string? view, [FromQuery] DateTime? date)
        {
            if (!Period.TryResolve(view, date, _clock.Today, out var period))
                return ValidationProblem(new ValidationProblemDetails { Title = Period.InvalidViewMessage });
            return await _admin.GridAsync(period);
        }

        //-----------------------------
        //one person's timesheet as a csv, or without userId a zip holding one csv per person who logged time in the period
        [HttpGet("exports/timesheets")]
        public async Task<IActionResult> Export([FromQuery] string? view, [FromQuery] DateTime? date, [FromQuery] int? userId)
        {
            if (!Period.TryResolve(view, date, _clock.Today, out var period))
                return ValidationProblem(new ValidationProblemDetails { Title = Period.InvalidViewMessage });

            var file = await _admin.ExportAsync(period, userId);
            return file == null
                ? Problem(title: "There is nothing to export for that person and period.", statusCode: StatusCodes.Status404NotFound)
                : File(file.Bytes, file.ContentType, file.FileName);
        }
    }
}
//------------------------------EOF-----------------------------\\
