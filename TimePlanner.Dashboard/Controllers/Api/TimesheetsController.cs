using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TimePlanner.Dashboard.Security;
using TimePlanner.Dashboard.Services;
using TimePlanner.Dashboard.Services.Overview;
using TimePlanner.Dashboard.Services.Reports;
using TimePlanner.Dashboard.Services.TimesheetImport;

namespace TimePlanner.Dashboard.Controllers.Api
{
    public class TimesheetsController : ApiControllerBase
    {
        private readonly TimesheetImportService _import;
        private readonly TimesheetViewService _view;
        private readonly CompanyClock _clock;

        public TimesheetsController(TimesheetImportService import, TimesheetViewService view, CompanyClock clock)
        {
            _import = import;
            _view = view;
            _clock = clock;
        }

        //-----------------------------
        //a week or month laid out day by day with what was logged and whether each day was submitted. Read only.
        //view is week (the default) or month and date is any day inside it. Developers see their own, an admin may ask for another person with userId
        [HttpGet]
        public async Task<ActionResult<TimesheetView>> Get([FromQuery] string? view, [FromQuery] DateTime? date, [FromQuery] int? userId)
        {
            if (!Period.TryResolve(view, date, _clock.Today, out var period))
                return ValidationProblem(new ValidationProblemDetails { Title = Period.InvalidViewMessage });
            if (CurrentAppUserId is not int me)
                return NoProfile();
            if (!ReportService.TryScope(userId, me, IsPrivileged, out var owner))
                return NotAllowed();

            return await _view.GetAsync(owner ?? me, period);
        }

        public record ImportRequest(List<ImportEntry>? Entries);

        //-----------------------------
        //imports worked time as json, used by the widget after the user has reviewed their timesheet.
        //the entries always belong to the caller (taken from the token), the body has no user field.
        //200 when stored (Created and Skipped count the entries), 400 with a row by row error list and nothing stored otherwise
        [HttpPost("import")]
        [EnableRateLimiting(SecurityExtensions.ImportLimiter)]
        [RequestSizeLimit(2_000_000)]
        public async Task<ActionResult<ImportResult>> Import(ImportRequest request)
        {
            if (CurrentAppUserId is not int userId)
                return NoProfile();

            var rows = (request.Entries ?? new List<ImportEntry>())
                .Select((entry, index) => (Row: index + 1, Entry: entry))
                .ToList();

            var result = await _import.ImportAsync(userId, rows);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        //-----------------------------
        //the same import for a csv file sent as multipart form data in a field called file
        [HttpPost("import/csv")]
        [EnableRateLimiting(SecurityExtensions.ImportLimiter)]
        [RequestSizeLimit(1_100_000)]
        public async Task<ActionResult<ImportResult>> ImportCsv(IFormFile file)
        {
            if (CurrentAppUserId is not int userId)
                return NoProfile();

            await using var stream = file.OpenReadStream();
            var result = await _import.ImportCsvAsync(userId, stream, file.Length, file.FileName);
            return result.Success ? Ok(result) : BadRequest(result);
        }
    }
}
//------------------------------EOF-----------------------------\\
