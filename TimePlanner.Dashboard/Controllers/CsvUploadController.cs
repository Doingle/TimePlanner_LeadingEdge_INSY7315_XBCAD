using Microsoft.AspNetCore.Mvc;

namespace TimePlanner.Dashboard.Controllers
{
    public class CsvUploadController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
