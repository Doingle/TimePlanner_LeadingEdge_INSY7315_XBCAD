using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimePlanner.Dashboard.Security;
using TimePlanner.Dashboard.Data;
using TimePlanner.Dashboard.Services.TimesheetImport;

namespace TimePlanner.Dashboard.Controllers
{
    public class CsvUploadController : Controller
    {
        private readonly TimesheetImportService _import;
        private readonly UserManager<ApplicationUser> _users;

        public CsvUploadController(TimesheetImportService import, UserManager<ApplicationUser> users)
        {
            _import = import;
            _users = users;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return View((ImportResult?)null);
        }

        //-----------------------------
        //the signed in user uploads a timesheet exported by the widget. Entries are stored for that user only
        [HttpPost]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting(SecurityExtensions.ImportLimiter)]
        [RequestSizeLimit(1_100_000)]
        public async Task<IActionResult> Index(IFormFile? file)
        {
            var account = await _users.GetUserAsync(User);
            if (account?.AppUserId is not int userId)
                return View(ImportResult.Failed(0, "Your account is not linked to a time tracking profile, ask an administrator."));
            if (file == null)
                return View(ImportResult.Failed(0, "Choose a CSV file to upload."));

            await using var stream = file.OpenReadStream();
            return View(await _import.ImportCsvAsync(userId, stream, file.Length, file.FileName));
        }

        //-----------------------------
        //an example file showing the expected columns
        [HttpGet]
        public IActionResult Template()
        {
            return File(Encoding.UTF8.GetBytes(TimesheetImportService.TemplateCsv), "text/csv", "timesheet-template.csv");
        }
    }
}
