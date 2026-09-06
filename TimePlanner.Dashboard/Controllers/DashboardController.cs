using Microsoft.AspNetCore.Mvc;

namespace TimePlanner.Dashboard.Controllers
{
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
