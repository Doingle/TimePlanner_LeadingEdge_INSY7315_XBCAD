using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TimePlanner.Dashboard.Data;

namespace TimePlanner.Dashboard.Controllers.Api
{
    //-----------------------------
    //the audit trail, for administrators only
    [Authorize(Roles = "Admin")]
    public class AuditController : ApiControllerBase
    {
        private readonly AuthDbContext _db;

        public AuditController(AuthDbContext db) => _db = db;

        public record AuditEventDto(int Id, DateTime TimestampUtc, string? Email, string Action, string? Detail, string? IpAddress);

        //-----------------------------
        //the newest events first, optionally only one kind of action
        [HttpGet]
        public async Task<ActionResult<List<AuditEventDto>>> Get([FromQuery] string? action, [FromQuery] int take = 100)
        {
            var query = _db.AuditEvents.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(action))
                query = query.Where(e => e.Action == action);

            return await query
                .OrderByDescending(e => e.Id)
                .Take(Math.Clamp(take, 1, 500))
                .Select(e => new AuditEventDto(e.Id, e.TimestampUtc, e.Email, e.Action, e.Detail, e.IpAddress))
                .ToListAsync();
        }
    }
}
//------------------------------EOF-----------------------------\\
