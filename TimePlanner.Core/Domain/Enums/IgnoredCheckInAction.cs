namespace TimePlanner.Core.Domain.Enums
{
    //-----------------------------
    //this enum describes what the check in engine does when a check in goes unanswered for IgnoredCheckInMinutes
    public enum IgnoredCheckInAction
    {
        KeepAsking,
        LogAsUntracked,
        AutoSkip
    }
}
//------------------------------EOF-----------------------------\\
