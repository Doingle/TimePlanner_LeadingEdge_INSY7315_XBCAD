using Microsoft.AspNetCore.Mvc;

namespace TimePlanner.Dashboard.Controllers
{
    public class TimesheetController : Controller
    {
        //-----------------------------
        //the signed in user's week of time entries grouped by day. The page is design only for now, no entries are loaded yet
        public IActionResult Index()
        {
            return View();
        }
    }
}
