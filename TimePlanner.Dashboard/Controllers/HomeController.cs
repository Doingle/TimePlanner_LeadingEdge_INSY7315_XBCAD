using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimePlanner.Dashboard.Models;
using TimePlanner.Dashboard.Security;
using TimePlanner.Dashboard.Services.Overview;

namespace TimePlanner.Dashboard.Controllers
{
    public class HomeController : Controller
    {
        private readonly OverviewService _overview;

        public HomeController(OverviewService overview) => _overview = overview;

        //-----------------------------
        //the signed in person's home: today's timeline, last submission, and where the time went today (the default) or this week (?period=week).
        //a login without a time tracking profile gets the page with no overview, which says why
        [HttpGet]
        public async Task<IActionResult> Index(string? period)
        {
            if (User.ProfileId() is not int me)
                return View((MyOverview?)null);

            return View(await _overview.GetAsync(me, period));
        }

        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }

        //the exception handler renders this for anonymous users too
        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
