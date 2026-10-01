using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Models;
using TimePlanner.Dashboard.Services.Reports;

namespace TimePlanner.Dashboard.Controllers
{
    public class ReportController : Controller
    {
        private readonly ReportService _reports;
        private readonly UserManager<ApplicationUser> _users;

        public ReportController(ReportService reports, UserManager<ApplicationUser> users)
        {
            _reports = reports;
            _users = users;
        }

        private bool IsPrivileged => User.IsInRole("Admin") || User.IsInRole("Billing");

        //-----------------------------
        //hours per project, company, user, day or activity. Developers only ever see their own, admin and billing may pick anyone
        [HttpGet]
        public async Task<IActionResult> Index(DateTime? from, DateTime? to, ReportGrouping groupBy = ReportGrouping.Project, int? userId = null)
        {
            //the first screen shows the month so far
            var today = DateTime.Today;
            var model = new ReportPageModel
            {
                From = from ?? new DateTime(today.Year, today.Month, 1),
                To = to ?? today,
                GroupBy = groupBy,
                CanPickUser = IsPrivileged,
                UserId = userId
            };
            if (model.CanPickUser)
                model.Users = await _reports.GetUserOptionsAsync();

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
            if (!ReportService.TryScope(userId, me, IsPrivileged, out var filter))
            {
                model.Error = "You can only view your own hours.";
                return View(model);
            }

            model.Report = await _reports.GetHoursAsync(model.From, model.To, groupBy, filter, null);
            return View(model);
        }

        //-----------------------------
        //downloads one person's timesheet in the company layout
        [HttpGet]
        public async Task<IActionResult> Export(DateTime? from, DateTime? to, int? userId = null)
        {
            if (ReportService.ValidateRange(from, to) is string problem)
                return BadRequest(problem);

            var account = await _users.GetUserAsync(User);
            if (account?.AppUserId is not int me)
                return Forbid();
            if (!ReportService.TryScope(userId, me, IsPrivileged, out _))
                return Forbid();

            var bytes = await _reports.ExportTimesheetCsvAsync(userId ?? me, from!.Value, to!.Value);
            return File(bytes, "text/csv", $"timesheet_{from:yyyyMMdd}_{to:yyyyMMdd}.csv");
        }
    }
}
