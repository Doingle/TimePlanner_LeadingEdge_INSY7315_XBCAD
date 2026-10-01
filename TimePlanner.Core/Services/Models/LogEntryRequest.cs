using TimePlanner.Core.Domain.Enums;

namespace TimePlanner.Core.Services.Models
{
    //-----------------------------
    //this is what the log form sends to save an entry
    public sealed record LogEntryRequest(int UserId, int ProjectId, int CategoryId, string? Note, EntryMethod Method, DateTime Now);
}
//------------------------------EOF-----------------------------\\
