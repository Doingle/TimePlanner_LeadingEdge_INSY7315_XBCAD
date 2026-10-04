using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Models;
using TimePlanner.Dashboard.Services;
using TimePlanner.Dashboard.Services.Reports;
using TimePlanner.Dashboard.Services.Submissions;

namespace TimePlanner.Dashboard.Controllers
{
    public class ReportController : Controller
    {
        private readonly ReportService _reports;
        private readonly SubmissionService _submissions;
        private readonly UserManager<ApplicationUser> _users;
        private readonly CompanyClock _clock;

        public ReportController(ReportService reports, SubmissionService submissions, UserManager<ApplicationUser> users, CompanyClock clock)
        {
            _reports = reports;
            _submissions = submissions;
            _users = users;
            _clock = clock;
        }

        private bool IsPrivileged => User.IsInRole("Admin");

        //-----------------------------
        //My Reports: the signed in person's own time for a whole week or month (?view=week|month and a date inside it, the current month to begin with)
        //or any from/to range, with the billable split and the breakdowns by category and project. Always their own, whoever is asking:
        //the detailed report, where an admin can group the hours and pick anyone, is on the team's Submissions page
        [HttpGet]
        public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? view = null, DateTime? date = null)
        {
            var model = new ReportPageModel { From = from ?? _clock.Today, To = to ?? _clock.Today };

            //a week or month sets the range, unless an explicit range was given
            if (from == null && to == null)
            {
                if (!Period.TryResolve(string.IsNullOrWhiteSpace(view) ? "month" : view, date, _clock.Today, out var period))
                {
                    model.Error = Period.InvalidViewMessage;
                    return View(model);
                }
                model.From = period.From;
                model.To = period.To;
                model.View = period.View;
                model.PeriodLabel = period.Label;
                model.Previous = period.View == "week" ? period.From.AddDays(-7) : period.From.AddMonths(-1);
                model.Next = period.View == "week" ? period.From.AddDays(7) : period.From.AddMonths(1);
            }

            var account = await _users.GetUserAsync(User);
            if (account?.AppUserId is not int me)
            {
                model.Error = "Your account is not linked to a time tracking profile, ask an administrator.";
                return View(model);
            }

            if (ReportService.ValidateRange(model.From, model.To) is string problem)
            {
                model.Error = problem;
                return View(model);
            }

            //populates past periods for week or month views
            if (model.View is "week" or "month")
            {
                var count = model.View == "week" ? 8 : 6;
                var currentFrom = model.From;
                for (var i = 0; i < count; i++)
                {
                    if (Period.TryResolve(model.View, currentFrom.AddDays(-1), _clock.Today, out var pastPeriod))
                    {
                        model.PastPeriods.Add(new PastPeriod(pastPeriod.Label, pastPeriod.From, pastPeriod.To));
                        currentFrom = pastPeriod.From;
                    }
                }
            }

            model.Summary = await _reports.GetSummaryAsync(model.From, model.To, me);
            model.SubmittedDays = (await _submissions.ForUserAsync(me, model.From, model.To)).Count;
            return View(model);
        }

        //-----------------------------
        //downloads one person's timesheet in the company layout
        [HttpGet]
        public async Task<IActionResult> Export(DateTime? from, DateTime? to, int? userId = null, string format = "xlsx")
        {
            if (ReportService.ValidateRange(from, to) is string problem)
                return BadRequest(problem);

            var account = await _users.GetUserAsync(User);
            if (account?.AppUserId is not int me)
                return Forbid();
            if (!ReportService.TryScope(userId, me, IsPrivileged, out _))
                return Forbid();

            //csv export branch
            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
            {
                var csvBytes = await _reports.ExportTimesheetCsvAsync(userId ?? me, from!.Value, to!.Value);
                return File(csvBytes, "text/csv", $"timesheet_{from:yyyyMMdd}_{to:yyyyMMdd}.csv");
            }

            var xlsxBytes = await _reports.ExportTimesheetXlsxAsync(userId ?? me, from!.Value, to!.Value);
            return File(xlsxBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"timesheet_{from:yyyyMMdd}_{to:yyyyMMdd}.xlsx");
        }
    }
}
