using TimePlanner.Dashboard.Data;

namespace TimePlanner.Dashboard.Services
{
    //-----------------------------
    //records audit events. The person and address come from the current request unless given, and a failure to write never breaks the request being audited.
    //ponytail: rows are never purged, add a scheduled delete of old rows if the table grows large
    public class AuditLogger
    {
        private const int MaxEmail = 256, MaxDetail = 500;

        private readonly AuthDbContext _db;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<AuditLogger> _logger;

        public AuditLogger(AuthDbContext db, IHttpContextAccessor http, ILogger<AuditLogger> logger)
        {
            _db = db;
            _http = http;
            _logger = logger;
        }

        //-----------------------------
        //email and userId default to the signed in caller, a sign in attempt passes what was typed because nobody is signed in yet
        public async Task LogAsync(string action, string? detail = null, string? email = null, string? userId = null)
        {
            try
            {
                var context = _http.HttpContext;
                var user = context?.User;
                _db.AuditEvents.Add(new AuditEvent
                {
                    TimestampUtc = DateTime.UtcNow,
                    UserId = userId ?? user?.FindFirst("sub")?.Value ?? user?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                    Email = Clean(email ?? user?.FindFirst("email")?.Value ?? user?.Identity?.Name, MaxEmail),
                    Action = action,
                    Detail = Clean(detail, MaxDetail),
                    IpAddress = context?.Connection.RemoteIpAddress?.ToString()
                });
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not write the audit event {Action}", action);
            }
        }

        //text that came from a request is shortened and stripped of control characters, so a crafted value cannot forge or hide lines in a log
        private static string? Clean(string? value, int max)
        {
            if (value == null)
                return null;
            var clean = new string(value.Where(c => !char.IsControl(c)).ToArray());
            return clean.Length <= max ? clean : clean[..max];
        }
    }
}
//------------------------------EOF-----------------------------\\
