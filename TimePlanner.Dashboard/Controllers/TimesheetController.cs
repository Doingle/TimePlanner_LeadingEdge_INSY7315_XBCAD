using Microsoft.AspNetCore.Mvc;
using TimePlanner.Dashboard.Models;
using TimePlanner.Dashboard.Security;
using TimePlanner.Dashboard.Services;
using TimePlanner.Dashboard.Services.Overview;
using TimePlanner.Dashboard.Services.Reports;

namespace TimePlanner.Dashboard.Controllers
{
    //-----------------------------
    //My Timesheet: the signed in person's week or month day by day, which days were submitted, and what was logged. Read only:
    //a correction is made in the widget and submitted again
    public class TimesheetController : Controller
    {
        private readonly TimesheetViewService _view;
        private readonly CompanyClock _clock;

        public TimesheetController(TimesheetViewService view, CompanyClock clock)
        {
            _view = view;
            _clock = clock;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? view, DateTime? date)
        {
            if (User.ProfileId() is not int me)
                return View(new TimesheetPageModel { NoProfile = true });

            //an unknown view falls back to the current week with a message, the page never fails
            var ok = Period.TryResolve(view, date, _clock.Today, out var period);
            if (!ok)
                Period.TryResolve("week", null, _clock.Today, out period);

            return View(new TimesheetPageModel
            {
                Data = await _view.GetAsync(me, period),
                Previous = period.View == "week" ? period.From.AddDays(-7) : period.From.AddMonths(-1),
                Next = period.View == "week" ? period.From.AddDays(7) : period.From.AddMonths(1),
                Error = ok ? null : Period.InvalidViewMessage
            });
        }
    }
}
