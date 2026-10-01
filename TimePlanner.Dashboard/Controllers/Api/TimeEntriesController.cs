using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TimePlanner.Core.Repositories.Interfaces;

namespace TimePlanner.Dashboard.Controllers.Api
{
    public class TimeEntriesController : ApiControllerBase
    {
        //longest window one request may ask for, keeps responses small
        public const int MaxRangeDays = 92;

        private readonly ITimeEntryRepository _entries;

        public TimeEntriesController(ITimeEntryRepository entries) => _entries = entries;

        //-----------------------------
        //time entries between from and to. Developers only ever receive their own, admins and billing may ask for another user with userId.
        //the owner is decided here from the token, never from the request, so changing an id in the url cannot expose someone else's data
        [HttpGet]
        public async Task<ActionResult<List<TimeEntryDto>>> Get([FromQuery, Required] DateTime? from, [FromQuery, Required] DateTime? to, [FromQuery] int? userId)
        {
            if (from > to)
                return ValidationProblem(new ValidationProblemDetails { Title = "'from' must not be after 'to'." });
            if ((to!.Value - from!.Value).TotalDays > MaxRangeDays)
                return ValidationProblem(new ValidationProblemDetails { Title = $"The range may not be longer than {MaxRangeDays} days." });

            if (CurrentAppUserId is not int me)
                return NoProfile();

            var owner = userId ?? me;
            if (owner != me && !IsPrivileged)
                return NotAllowed();

            return (await _entries.GetForUserAsync(owner, from.Value, to.Value))
                .Select(e => new TimeEntryDto(e.TimeEntryId, e.UserId, e.TaskId, e.Task?.ProjectID ?? 0, e.Task?.Project?.Name ?? string.Empty,
                    e.StartTime, e.EndTime, e.GetDuration().TotalMinutes, e.Note, e.Method.ToString()))
                .ToList();
        }
    }
}
//------------------------------EOF-----------------------------\\
