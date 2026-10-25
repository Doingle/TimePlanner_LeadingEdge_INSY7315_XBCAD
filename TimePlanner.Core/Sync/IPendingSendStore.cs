namespace TimePlanner.Core.Sync
{
    //-----------------------------
    //the days the user pressed Send day on that are still waiting to reach the dashboard
    public interface IPendingSendStore
    {
        Task<IReadOnlyList<PendingDay>> GetAsync();

        //adds a day or replaces the same day
        Task SaveAsync(PendingDay pending);

        //does nothing when the day is not waiting
        Task RemoveAsync(DateOnly day);
    }
}
//------------------------------EOF-----------------------------\\
