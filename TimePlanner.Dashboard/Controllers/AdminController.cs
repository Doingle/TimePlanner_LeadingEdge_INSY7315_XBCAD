using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimePlanner.Dashboard.Models;
using TimePlanner.Dashboard.Services;
using TimePlanner.Dashboard.Services.Overview;
using TimePlanner.Dashboard.Services.Reports;

namespace TimePlanner.Dashboard.Controllers
{
    //-----------------------------
    //the administrators' pages: team overview (/Admin), submissions with the detailed report (/Admin/Submissions) and exports (/Admin/Exports).
    //accounts are managed on /Users. All of it is a record of what was submitted, there is nothing to approve or chase
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly AdminService _admin;
        private readonly ReportService _reports;
        private readonly CompanyClock _clock;

        public AdminController(AdminService admin, ReportService reports, CompanyClock clock)
        {
            _admin = admin;
            _reports = reports;
            _clock = clock;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? view, DateTime? date)
        {
            var (page, period) = Resolve(view, date);
            page.Overview = await _admin.OverviewAsync(period);
            return View(page);
        }

        //-----------------------------
        //the submissions grid for the week or month, and under it the detailed report: hours for any range (the grid's period to begin with),
        //grouped by project, company, person, day or activity, for everyone or one person
        [HttpGet]
        public async Task<IActionResult> Submissions(string? view, DateTime? date, DateTime? from, DateTime? to,
            ReportGrouping groupBy = ReportGrouping.Project, int? userId = null)
        {
            var (page, period) = Resolve(view, date);
            page.Grid = await _admin.GridAsync(period);

            page.ReportFrom = from ?? period.From;
            page.ReportTo = to ?? period.To;
            page.GroupBy = groupBy;
            page.ReportUserId = userId;
            page.People = await _reports.GetUserOptionsAsync();
            if (ReportService.ValidateRange(page.ReportFrom, page.ReportTo) is string problem)
                page.ReportError = problem;
            else
                page.Report = await _reports.GetHoursAsync(page.ReportFrom, page.ReportTo, groupBy, userId, null);
            return View(page);
        }

        [HttpGet]
        public async Task<IActionResult> Exports(string? view, DateTime? date)
        {
            var (page, _) = Resolve(view, date);
            page.People = await _reports.GetUserOptionsAsync();
            return View(page);
        }

        //-----------------------------
        //one person's file, or without a person a zip with one file per person who logged time, for the week or month
        [HttpGet]
        public async Task<IActionResult> Export(string? view, DateTime? date, int? userId, string format = "xlsx")
        {
            if (!Period.TryResolve(view, date, _clock.Today, out var period))
                return BadRequest(Period.InvalidViewMessage);

            var file = await _admin.ExportAsync(period, userId, format);
            if (file == null)
            {
                TempData["Error"] = "There is nothing to export for that person and period.";
                return RedirectToAction(nameof(Exports), new { view = period.View, date = period.From });
            }
            return File(file.Bytes, file.ContentType, file.FileName);
        }

        //an unknown view falls back to the current week with a message, a page never fails on a bad link
        private (AdminPageModel Page, Period Period) Resolve(string? view, DateTime? date)
        {
            var ok = Period.TryResolve(view, date, _clock.Today, out var period);
            if (!ok)
                Period.TryResolve("week", null, _clock.Today, out period);

            return (new AdminPageModel
            {
                View = period.View,
                Label = period.Label,
                From = period.From,
                To = period.To,
                Previous = period.View == "week" ? period.From.AddDays(-7) : period.From.AddMonths(-1),
                Next = period.View == "week" ? period.From.AddDays(7) : period.From.AddMonths(1),
                Error = ok ? (TempData["Error"] as string) : Period.InvalidViewMessage
            }, period);
        }
    }
}
