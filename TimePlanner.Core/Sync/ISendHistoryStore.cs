namespace TimePlanner.Core.Sync
{
    //-----------------------------
    //local record of days already sent
    public interface ISendHistoryStore
    {
        //newest first
        Task<IReadOnlyList<SentDay>> GetAsync();

        //adds a day or replaces the same day
        Task AddAsync(SentDay sent);

        //notes that a sent day was edited afterwards
        Task MarkChangedAsync(DateOnly day, DateTime at);
    }
}
//------------------------------EOF-----------------------------\\
