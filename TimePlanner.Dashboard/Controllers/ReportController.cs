using Microsoft.AspNetCore.Mvc;

namespace TimePlanner.Dashboard.Controllers
{
    public class ReportController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
