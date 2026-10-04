namespace TimePlanner.Core.Services.Models
{
    //-----------------------------
    //snapshot of the engine for the widget
    public sealed record CheckInStatus(
        CheckInPhase Phase,
        DateTime? NextCheckInAt,
        DateTime? DueSince,
        DateTime? SnoozedUntil,
        int SnoozesLeft,
        int SkipsLeft,
        bool CanSnooze,
        bool CanSkip);
}
//------------------------------EOF-----------------------------\\
