using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TimePlanner.Dashboard.Services;
using TimePlanner.Dashboard.Services.Reports;

namespace TimePlanner.Dashboard.Controllers.Api
{
    public class ReportsController : ApiControllerBase
    {
        private readonly ReportService _reports;
        private readonly CompanyClock _clock;

        public ReportsController(ReportService reports, CompanyClock clock)
        {
            _reports = reports;
            _clock = clock;
        }

        //-----------------------------
        //totals, billable and non-billable time, and breakdowns by category, project and person. Give from and to for any range, or view (week or month,
        //the default is the week) and a date inside it. Developers get their own, an admin gets everyone's unless they pick a person with userId
        [HttpGet("summary")]
        public async Task<ActionResult<ReportSummary>> Summary([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? view,
            [FromQuery] DateTime? date, [FromQuery] int? userId = null)
        {
            if (from != null || to != null)
            {
                if (ReportService.ValidateRange(from, to) is string problem)
                    return ValidationProblem(new ValidationProblemDetails { Title = problem });
            }
            else
            {
                if (!Period.TryResolve(view, date, _clock.Today, out var period))
                    return ValidationProblem(new ValidationProblemDetails { Title = Period.InvalidViewMessage });
                from = period.From;
                to = period.To;
            }

            if (CurrentAppUserId is not int me)
                return NoProfile();
            if (!ReportService.TryScope(userId, me, IsPrivileged, out var filter))
                return NotAllowed();

            return await _reports.GetSummaryAsync(from!.Value, to!.Value, filter);
        }

        //-----------------------------
        //hours per project, company, user, day or activity between two days (both included).
        //developers get their own hours, admin and billing get everyone's unless they ask for one user with userId
        [HttpGet("hours")]
        public async Task<ActionResult<HoursReport>> Hours([FromQuery, Required] DateTime? from, [FromQuery, Required] DateTime? to,
            [FromQuery] ReportGrouping groupBy = ReportGrouping.Project, [FromQuery] int? userId = null, [FromQuery] int? companyId = null)
        {
            if (ReportService.ValidateRange(from, to) is string problem)
                return ValidationProblem(new ValidationProblemDetails { Title = problem });
            if (CurrentAppUserId is not int me)
                return NoProfile();
            if (!ReportService.TryScope(userId, me, IsPrivileged, out var filter))
                return NotAllowed();

            return await _reports.GetHoursAsync(from!.Value, to!.Value, groupBy, filter, companyId);
        }

        //-----------------------------
        //one person's timesheet as a csv in the company's layout, the caller's own unless admin or billing ask for another user
        [HttpGet("timesheet.csv")]
        [Produces("text/csv")]
        public async Task<IActionResult> Timesheet([FromQuery, Required] DateTime? from, [FromQuery, Required] DateTime? to, [FromQuery] int? userId = null)
        {
            if (ReportService.ValidateRange(from, to) is string problem)
                return ValidationProblem(new ValidationProblemDetails { Title = problem });
            if (CurrentAppUserId is not int me)
                return NoProfile();
            if (!ReportService.TryScope(userId, me, IsPrivileged, out _))
                return NotAllowed();

            var bytes = await _reports.ExportTimesheetCsvAsync(userId ?? me, from!.Value, to!.Value);
            return File(bytes, "text/csv", $"timesheet_{from:yyyyMMdd}_{to:yyyyMMdd}.csv");
        }
    }
}
//------------------------------EOF-----------------------------\\
